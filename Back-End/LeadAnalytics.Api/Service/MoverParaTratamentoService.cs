using LeadAnalytics.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>Um card candidato a ir para a etapa de tratamento.</summary>
public record CandidatoAoTratamento(
    long IdTreatment,
    long LeadId,
    string? Paciente,
    DateOnly DiaLancamento,
    long? EtapaAtual,
    long? FunilAtual);

/// <summary>O que fazer com o card, e por quê. <c>Mover=false</c> sempre traz o motivo.</summary>
public record DecisaoDeMovimento(CandidatoAoTratamento Card, bool Mover, string Motivo);

/// <summary>
/// Move para a etapa de tratamento os cards de quem a clínica registrou como tratamento
/// fechado — e, mais importante, sabe de quem NÃO chegar perto.
///
/// POR QUE ISTO PRECISA DE REGRA, E NÃO DE UM PATCH EM LAÇO
/// -------------------------------------------------------
/// Mover card em CRM de produção é escrita irreversível na conta do cliente: dispara
/// automação, pode disparar mensagem para paciente real, e o histórico da etapa fica
/// carimbado para sempre. Três situações parecem iguais no `status_id` e não são:
///
///  • 142 no funil COMERCIAL é GANHO — o comercial fechou, e ir para tratamento é o
///    passo seguinte correto;
///  • 142 no funil TRATAMENTO é ALTA — o paciente TERMINOU. Mover de volta para
///    "em tratamento" desfaria uma alta e faria o paciente reaparecer como ativo;
///  • 143 no funil TRATAMENTO é TRATAMENTO CANCELADO — desistência registrada, que
///    também não se reverte por conta própria.
///
/// A Kommo reutiliza os ids 142/143 entre funis, então só o par (funil, etapa) decide.
/// Sem essa distinção, "mover todo mundo que fechou" ressuscitaria altas e cancelamentos.
/// </summary>
public class MoverParaTratamentoService(AppDbContext db, KommoApiClient kommo,
                                        ILogger<MoverParaTratamentoService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly KommoApiClient _kommo = kommo;
    private readonly ILogger<MoverParaTratamentoService> _logger = logger;

    /// <summary>
    /// Decide um card. Pura de propósito: é a regra que impede desfazer alta, e regra
    /// que decide escrita em CRM de cliente não pode depender de rede para ser testada.
    /// </summary>
    /// <param name="funilDestino">Funil de tratamento da unidade.</param>
    /// <param name="etapaDestino">Etapa "em tratamento" dentro dele.</param>
    public static DecisaoDeMovimento Decidir(
        CandidatoAoTratamento c, int funilDestino, int etapaDestino)
    {
        if (c.EtapaAtual is null || c.FunilAtual is null)
            return new(c, false, "não consegui ler a etapa atual na Kommo");

        if (c.EtapaAtual == etapaDestino)
            return new(c, false, "já está em tratamento");

        if (c.FunilAtual == funilDestino)
        {
            // Dentro do funil de tratamento, só o começo do fluxo pode avançar. Alta e
            // cancelamento são desfechos: mexer neles seria reescrever o que aconteceu.
            if (c.EtapaAtual == KommoStatusNativos.Ganho)
                return new(c, false, "já teve ALTA — mover desfaria a alta");
            if (c.EtapaAtual == KommoStatusNativos.Perdido)
                return new(c, false, "tratamento cancelado — não se reverte sozinho");
        }

        return new(c, true, "fechou tratamento na clínica e o card não está na etapa");
    }

    /// <summary>
    /// Monta a lista a partir dos tratamentos já cruzados por telefone, consultando a
    /// etapa atual de cada card na Kommo.
    /// </summary>
    public async Task<List<DecisaoDeMovimento>> PrepararAsync(
        int unitId, DateOnly de, DateOnly ate, int funilDestino, int etapaDestino,
        CancellationToken ct = default)
    {
        var unidade = await _db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unidade is null || string.IsNullOrWhiteSpace(unidade.KommoSubdomain)
            || string.IsNullOrWhiteSpace(unidade.KommoAccessToken))
            return new List<DecisaoDeMovimento>();

        var vinculos = await _db.FranquiaLeadLinks.AsNoTracking()
            .Where(v => v.UnitId == unitId && v.LeadId != null
                        && v.DiaLancamento >= de && v.DiaLancamento <= ate)
            .OrderBy(v => v.DiaLancamento)
            .ToListAsync(ct);

        var decisoes = new List<DecisaoDeMovimento>();
        // Um paciente pode ter dois tratamentos e um só card. Decidir duas vezes o mesmo
        // card geraria duas escritas idênticas — a segunda um no-op ruidoso.
        var jaVistos = new HashSet<long>();

        foreach (var v in vinculos)
        {
            var leadId = v.LeadId!.Value;
            if (!jaVistos.Add(leadId)) continue;

            long? etapa = null, funil = null;
            try
            {
                var pagina = await _kommo.GetLeadsByIdsAsync(
                    unidade.KommoSubdomain, unidade.KommoAccessToken, new[] { leadId }, ct);
                var lead = pagina?.Embedded?.Leads?.FirstOrDefault();
                etapa = lead?.StatusId;
                funil = lead?.PipelineId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Não consegui ler o lead {LeadId} na Kommo", leadId);
            }

            var card = new CandidatoAoTratamento(
                v.IdTreatment, leadId, v.Paciente, v.DiaLancamento, etapa, funil);
            decisoes.Add(Decidir(card, funilDestino, etapaDestino));
        }

        return decisoes;
    }

    /// <summary>Executa os movimentos aprovados. Devolve quantos cards foram movidos.</summary>
    public async Task<int> MoverAsync(
        int unitId, IEnumerable<DecisaoDeMovimento> decisoes, int funilDestino, int etapaDestino,
        int limite, CancellationToken ct = default)
    {
        var unidade = await _db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unidade is null || string.IsNullOrWhiteSpace(unidade.KommoSubdomain)
            || string.IsNullOrWhiteSpace(unidade.KommoAccessToken))
            return 0;

        var movidos = 0;
        foreach (var d in decisoes.Where(x => x.Mover).Take(limite))
        {
            await _kommo.MoverLeadDeEtapaAsync(
                unidade.KommoSubdomain, unidade.KommoAccessToken,
                d.Card.LeadId, funilDestino, etapaDestino, ct);
            movidos++;
            _logger.LogInformation(
                "Movido para tratamento: lead {LeadId} ({Paciente}) na unidade {UnitId}",
                d.Card.LeadId, d.Card.Paciente, unitId);

            // A Kommo limita requisições por conta; um laço apressado volta 429 no meio e
            // deixa metade dos cards movidos, que é o pior estado possível.
            await Task.Delay(400, ct);
        }

        return movidos;
    }
}

/// <summary>
/// Os dois status que a Kommo cria sozinha em TODO funil e reaproveita entre eles.
/// O número não diz o que significa — quem diz é o funil em que ele aparece.
/// </summary>
public static class KommoStatusNativos
{
    /// <summary>"Venda ganha". No funil comercial é GANHO; no de tratamento, ALTA.</summary>
    public const int Ganho = 142;

    /// <summary>"Venda perdida". No comercial é PERDIDO; no de tratamento, TRATAMENTO CANCELADO.</summary>
    public const int Perdido = 143;
}
