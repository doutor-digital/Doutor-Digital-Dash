using LeadAnalytics.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>Onde fica o tratamento nesta unidade: funil, etapa e campo do valor.</summary>
public record EstruturaDeTratamento(int FunilId, int EtapaId, long CampoValor);

/// <summary>Um tratamento fechado na clínica cujo card ainda não reflete isso.</summary>
public record PendenciaDeTratamento(
    long IdTreatment,
    long LeadId,
    string? Paciente,
    DateOnly DiaLancamento,
    decimal? PrecoFranquia,
    bool PrecisaMover,
    bool PrecisaValor,
    long? ResponsavelId);

/// <summary>
/// Cobra da SDR as duas coisas que só ela pode fazer: mover o card para EM TRATAMENTO
/// e preencher o valor do tratamento.
///
/// POR QUE TAREFA NO CARD, E NÃO AVISO NO GRUPO
/// --------------------------------------------
/// Aviso em grupo não tem dono: todo mundo lê, ninguém faz, e no dia seguinte o mesmo
/// aviso volta. A tarefa cai na fila da própria SDR, presa ao card, a um clique da
/// ação — e fica registrado quem resolveu. O resumo no grupo continua existindo, mas
/// como termômetro para o gestor, não como pedido.
///
/// O QUE ISTO NÃO É
/// ----------------
/// Não é muleta do número. A receita do painel passou a sair do cruzamento com a
/// franquia, então ela não espera mais por esse gesto. Isto aqui existe para o CRM
/// ficar verdadeiro — funil, semáforo e follow-up dependem do card estar no lugar
/// certo. Se a cobrança falhar um dia, ninguém perde receita.
/// </summary>
public class PendenciasDeTratamentoService(
    AppDbContext db, KommoApiClient kommo, ILogger<PendenciasDeTratamentoService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly KommoApiClient _kommo = kommo;
    private readonly ILogger<PendenciasDeTratamentoService> _logger = logger;

    /// <summary>
    /// Começo fixo do texto da tarefa. É por ele que a rotina reconhece a própria
    /// cobrança e não empilha a mesma coisa todo dia — repetir treina a equipe a fechar
    /// tarefa sem ler.
    /// </summary>
    public const string Marcador = "Tratamento fechado na clínica";

    /// <summary>
    /// O texto que a SDR vai ler. Diz o QUE aconteceu, QUANDO, e o que falta fazer —
    /// nessa ordem, porque sem o fato ela não sabe se a cobrança procede.
    /// </summary>
    public static string TextoDaTarefa(PendenciaDeTratamento p)
    {
        var falta = (p.PrecisaMover, p.PrecisaValor) switch
        {
            (true, true) => "mover para EM TRATAMENTO e preencher o valor do tratamento",
            (true, false) => "mover para EM TRATAMENTO",
            (false, true) => "preencher o valor do tratamento",
            _ => "conferir o card",
        };

        var valor = p.PrecoFranquia is > 0
            ? $" (R$ {p.PrecoFranquia.Value:N0})".Replace(",", ".")
            : string.Empty;

        return $"{Marcador} em {p.DiaLancamento:dd/MM}{valor} — {falta}.";
    }

    /// <summary>
    /// Descobre funil, etapa e campo de valor da unidade pelos NOMES, na própria conta.
    ///
    /// Não dá para fixar ids: cada unidade tem os seus, e um id chutado moveria card
    /// para o lugar errado. Os nomes, esses, são padronizados na rede — foi assim que a
    /// replicação foi feita. O resultado fica guardado para não bater na Kommo toda vez.
    /// </summary>
    public async Task<EstruturaDeTratamento?> DescobrirEstruturaAsync(
        int unitId, CancellationToken ct = default)
    {
        var chave = $"tratamento:estrutura:{unitId}";
        var guardado = await _db.AppConfigurations.AsNoTracking()
            .Where(c => c.Key == chave).Select(c => c.Value).FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(guardado))
        {
            var p = guardado.Split('|');
            if (p.Length == 3 && int.TryParse(p[0], out var f)
                && int.TryParse(p[1], out var e) && long.TryParse(p[2], out var cv))
                return new EstruturaDeTratamento(f, e, cv);
        }

        var unidade = await _db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unidade is null || string.IsNullOrWhiteSpace(unidade.KommoSubdomain)
            || string.IsNullOrWhiteSpace(unidade.KommoAccessToken))
            return null;

        var pipelines = await _kommo.GetPipelinesAsync(
            unidade.KommoSubdomain, unidade.KommoAccessToken, ct);

        int? funil = null, etapa = null;
        foreach (var pl in pipelines?.Embedded?.Pipelines ?? new List<KommoApiPipeline>())
        {
            foreach (var st in pl.Embedded?.Statuses ?? new List<KommoApiStatus>())
            {
                var nome = (st.Name ?? string.Empty).ToUpperInvariant();
                // "EM TRATAMENTO" e não só "TRATAMENTO": a mesma conta tem
                // "TRATAMENTO CANCELADO" e "RETORNO PÓS-TRATAMENTO".
                if (nome.Contains("EM TRATAMENTO"))
                {
                    funil = (int)pl.Id;
                    etapa = (int)st.Id;
                }
            }
        }

        var campos = await _kommo.GetCustomFieldsAsync(
            unidade.KommoSubdomain, unidade.KommoAccessToken, ct);
        var campoValor = campos?.Embedded?.CustomFields?
            .FirstOrDefault(c => (c.Name ?? string.Empty).ToUpperInvariant().Contains("VALOR")
                                 && (c.Name ?? string.Empty).ToUpperInvariant().Contains("TRATAMENTO"))?.Id;

        if (funil is null || etapa is null || campoValor is null)
        {
            _logger.LogWarning(
                "Estrutura de tratamento incompleta na unidade {UnitId}: funil={Funil} etapa={Etapa} campo={Campo}",
                unitId, funil, etapa, campoValor);
            return null;
        }

        var valor = $"{funil}|{etapa}|{campoValor}";
        var linha = await _db.AppConfigurations.FirstOrDefaultAsync(c => c.Key == chave, ct);
        if (linha is null)
            _db.AppConfigurations.Add(new Models.AppConfiguration
            {
                Key = chave, Value = valor,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
        else
        {
            linha.Value = valor;
            linha.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);

        return new EstruturaDeTratamento(funil.Value, etapa.Value, campoValor.Value);
    }

    /// <summary>
    /// Levanta o que está pendente. Card já no funil de tratamento conta como resolvido,
    /// mesmo em ALTA ou CANCELADO — o paciente passou por lá, e cobrar de novo seria
    /// pedir para desfazer uma alta.
    /// </summary>
    public async Task<List<PendenciaDeTratamento>> LevantarAsync(
        int unitId, DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        var pendencias = new List<PendenciaDeTratamento>();

        var estrutura = await DescobrirEstruturaAsync(unitId, ct);
        if (estrutura is null) return pendencias;

        var unidade = await _db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unidade is null || string.IsNullOrWhiteSpace(unidade.KommoSubdomain)
            || string.IsNullOrWhiteSpace(unidade.KommoAccessToken))
            return pendencias;

        var vinculos = await _db.FranquiaLeadLinks.AsNoTracking()
            .Where(v => v.UnitId == unitId && v.LeadId != null
                        && v.DiaLancamento >= de && v.DiaLancamento <= ate)
            .OrderBy(v => v.DiaLancamento)
            .ToListAsync(ct);

        var vistos = new HashSet<long>();
        foreach (var v in vinculos)
        {
            var leadId = v.LeadId!.Value;
            if (!vistos.Add(leadId)) continue;

            try
            {
                var pagina = await _kommo.GetLeadsByIdsAsync(
                    unidade.KommoSubdomain, unidade.KommoAccessToken, new[] { leadId }, ct);
                var lead = pagina?.Embedded?.Leads?.FirstOrDefault();
                if (lead is null) continue;

                var noFunil = lead.PipelineId == estrutura.FunilId;
                var campo = lead.CustomFieldsValues?
                    .FirstOrDefault(f => f.FieldId == estrutura.CampoValor);
                var valor = campo?.Values?.FirstOrDefault()?.Value?.ToString();
                var temValor = !string.IsNullOrWhiteSpace(valor) && valor != "0";

                if (noFunil && temValor) continue;

                pendencias.Add(new PendenciaDeTratamento(
                    v.IdTreatment, leadId, v.Paciente, v.DiaLancamento, v.PrecoFranquia,
                    PrecisaMover: !noFunil, PrecisaValor: !temValor,
                    ResponsavelId: lead.ResponsibleUserId));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Não consegui conferir o lead {LeadId}", leadId);
            }
        }

        return pendencias;
    }

    /// <summary>
    /// Cria a tarefa de cobrança em cada card pendente, pulando quem já tem uma aberta.
    /// </summary>
    /// <returns>Quantas tarefas foram criadas.</returns>
    public async Task<int> CobrarAsync(
        int unitId, IEnumerable<PendenciaDeTratamento> pendencias, CancellationToken ct = default)
    {
        var unidade = await _db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unidade is null || string.IsNullOrWhiteSpace(unidade.KommoSubdomain)
            || string.IsNullOrWhiteSpace(unidade.KommoAccessToken))
            return 0;

        // Fim do dia local (UTC−3): a cobrança é para hoje, não para "daqui a 24h".
        var prazo = DateTime.UtcNow.Date.AddHours(23);

        var criadas = 0;
        foreach (var p in pendencias)
        {
            try
            {
                var abertas = await _kommo.TextosDeTarefasAbertasAsync(
                    unidade.KommoSubdomain, unidade.KommoAccessToken, p.LeadId, ct);
                if (abertas.Any(t => t.Contains(Marcador, StringComparison.OrdinalIgnoreCase)))
                    continue;

                await _kommo.CriarTarefaAsync(
                    unidade.KommoSubdomain, unidade.KommoAccessToken, p.LeadId,
                    TextoDaTarefa(p), prazo, p.ResponsavelId, ct);
                criadas++;

                // A Kommo limita requisições por conta; sem respiro o lote volta 429 no
                // meio e metade da equipe não recebe a cobrança.
                await Task.Delay(400, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Não consegui cobrar o lead {LeadId}", p.LeadId);
            }
        }

        return criadas;
    }
}
