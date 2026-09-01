using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>Uma movimentação de card que pode receber a data real da franquia.</summary>
public record MovimentoDeMigracao(
    int HistoryId,
    int LeadIdInterno,
    string? Paciente,
    /// <summary>Rótulo gravado na linha. NÃO é confiável para agrupar: a mesma etapa
    /// aparece ora com nome canônico, ora com o id cru (medido na Imperatriz em
    /// 01/09/2026 — o id 143 gravado como "TRATAMENTO_CANCELADO" e como "143").</summary>
    string Etapa,
    /// <summary>O id da etapa na Kommo. É por ele que se agrupa e se resolve o nome.</summary>
    int EtapaId,
    DateTime ArrastadoEm,
    DateOnly? LancadoEm);

/// <summary>O que a tela mostra antes de qualquer gravação.</summary>
public record PreviaDeMigracao(
    int UnitId,
    DateTime JanelaDe,
    DateTime JanelaAte,
    int MovimentacoesNaJanela,
    int LeadsComTratamento,
    int LeadsComMaisDeUmTratamento,
    IReadOnlyList<MovimentoDeMigracao> Datar,
    IReadOnlyList<MovimentoDeMigracao> SemVinculo);

/// <summary>
/// Devolve a data verdadeira aos cards movidos em mutirão de migração.
///
/// O PROBLEMA
/// ----------
/// A Kommo carimba a entrada na etapa com a hora do ARRASTE e não deixa editar. Quando a
/// SDR migra tratamentos retroativos, um mês inteiro desaba no dia da migração: o dia vira
/// o recorde histórico da unidade e os meses reais ficam vazios. Atinge todo KPI que conta
/// por entrada na etapa — receita, semáforo, funil.
///
/// POR QUE ISTO É UM SERVIÇO, E NÃO CÓDIGO DENTRO DA ROTA
/// ------------------------------------------------------
/// Existem duas portas para a mesma regra: a interna (cron/administração, por chave) e a
/// da tela, que a SDR usa com o login dela. Regra duplicada em duas rotas é regra que
/// diverge — e aqui divergir significa a tela carimbar uma data e o cron outra.
///
/// O LIMITE QUE FAZ ISTO SER SEGURO NA MÃO DA SDR
/// ----------------------------------------------
/// A data não é digitada: vem do dia em que a FRANQUIA lançou o tratamento, cruzado por
/// telefone. A SDR aceita ou não aceita — não escolhe. Isso importa porque data vira
/// resultado, e um campo livre nas mãos de quem tem meta é um convite a empurrar
/// tratamento para o mês que precisa fechar. Card sem tratamento casado ela não corrige:
/// sobe para o gestor, que é quem pode decidir uma data à mão.
/// </summary>
public class DatacaoDeMigracaoService(AppDbContext db, ILogger<DatacaoDeMigracaoService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly ILogger<DatacaoDeMigracaoService> _logger = logger;

    /// <summary>Motivo gravado na trilha de auditoria de cada correção.</summary>
    public const string Motivo = "migração retroativa: data do lançamento do tratamento na franquia";

    /// <summary>
    /// Meio-dia local (UTC−3) do dia do lançamento.
    ///
    /// Não é detalhe: o painel conta em dia COMERCIAL, cuja janela não começa à meia-noite.
    /// Carimbar 00:00 jogaria metade das correções para o dia anterior.
    /// </summary>
    public static DateTime MeioDoDia(DateOnly dia) =>
        new(dia.Year, dia.Month, dia.Day, 15, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Monta a prévia. Não grava nada — é o que a tela mostra para a pessoa decidir.
    /// </summary>
    /// <param name="de">Início da janela de LANÇAMENTO na franquia (o mês real do tratamento).</param>
    /// <param name="ate">Fim dessa janela.</param>
    /// <param name="movidoDe">Quando os cards foram arrastados. Padrão: hoje 00:00 UTC.</param>
    /// <param name="movidoAte">Fim do arraste. Padrão: agora.</param>
    public async Task<PreviaDeMigracao> PreverAsync(
        int unitId, DateOnly de, DateOnly ate,
        DateTime? movidoDe, DateTime? movidoAte, CancellationToken ct = default)
    {
        var janelaDe = movidoDe ?? DateTime.UtcNow.Date;
        var janelaAte = movidoAte ?? DateTime.UtcNow;

        // O vínculo guarda o id da KOMMO; o histórico de etapas usa o id interno.
        // leads.ExternalId é a ponte (medido em 01/09/2026: casa 100% dos vínculos).
        //
        // Um lead pode ter MAIS DE UM tratamento (paciente que voltou para operar outra
        // hérnia). Vale o mais ANTIGO: a primeira entrada em EM TRATAMENTO pertence ao
        // primeiro. Quantos estão nessa situação volta na prévia — se o card foi
        // arrastado uma vez só, o segundo tratamento fica sem data própria.
        var porLead = await (
            from v in _db.FranquiaLeadLinks.AsNoTracking()
            join l in _db.Leads.AsNoTracking()
                // Os dois lados do join precisam do MESMO tipo: ExternalId é int, o
                // vínculo guarda long?, Lead.UnitId é int? e o do vínculo é int.
                on new { E = (long?)v.LeadId, U = (int?)v.UnitId }
                equals new { E = (long?)l.ExternalId, U = l.UnitId }
            where v.UnitId == unitId && v.LeadId != null
                  && v.DiaLancamento >= de && v.DiaLancamento <= ate
            group v by l.Id into g
            select new { LeadId = g.Key, Primeiro = g.Min(x => x.DiaLancamento), Quantos = g.Count() })
            .ToListAsync(ct);

        var lancamento = porLead.ToDictionary(x => x.LeadId, x => x.Primeiro);

        var linhas = await (
            from h in _db.LeadStageHistories.AsNoTracking()
            join l in _db.Leads.AsNoTracking() on h.LeadId equals l.Id
            where l.UnitId == unitId
                  && h.ChangedAt >= janelaDe && h.ChangedAt <= janelaAte
                  // Correção humana existente nunca é sobrescrita: quem corrigiu à mão
                  // sabia de algo que a franquia não conta.
                  && h.CorrectedChangedAt == null
            orderby h.ChangedAt
            select new { h.Id, h.LeadId, l.Name, h.StageLabel, h.StageId, h.ChangedAt })
            .ToListAsync(ct);

        var datar = new List<MovimentoDeMigracao>();
        var semVinculo = new List<MovimentoDeMigracao>();

        foreach (var x in linhas)
        {
            var achou = lancamento.TryGetValue(x.LeadId, out var dia);
            var mov = new MovimentoDeMigracao(
                x.Id, x.LeadId, x.Name, x.StageLabel, x.StageId, x.ChangedAt, achou ? dia : null);

            // Só entra na lista de datar quando a data da franquia é DIFERENTE do dia do
            // arraste. No dia a dia normal as duas coincidem — carimbar aí seria encher a
            // tela de linhas que não mudam nada e treinar a pessoa a clicar sem ler.
            if (achou && dia != DateOnly.FromDateTime(x.ChangedAt))
                datar.Add(mov);
            else if (!achou)
                semVinculo.Add(mov);
        }

        return new PreviaDeMigracao(
            unitId, janelaDe, janelaAte, linhas.Count, lancamento.Count,
            porLead.Count(x => x.Quantos > 1), datar, semVinculo);
    }

    /// <summary>
    /// Grava a data da franquia nas movimentações indicadas.
    ///
    /// Recebe os ids que a tela mostrou, mas NÃO confia neles: recalcula a prévia e só
    /// grava o que ela autoriza. Sem isso, um id fora da lista aceitaria carimbar
    /// qualquer transição com qualquer data — que é exatamente o que a tela não deve
    /// permitir à SDR.
    /// </summary>
    /// <returns>Quantas linhas foram efetivamente corrigidas.</returns>
    public async Task<int> AplicarAsync(
        int unitId, DateOnly de, DateOnly ate,
        DateTime? movidoDe, DateTime? movidoAte,
        IReadOnlyCollection<int>? apenasEstes, string? quemEmail, int? quemId,
        CancellationToken ct = default)
    {
        var previa = await PreverAsync(unitId, de, ate, movidoDe, movidoAte, ct);

        var permitidos = previa.Datar
            .Where(m => apenasEstes is null || apenasEstes.Contains(m.HistoryId))
            .ToDictionary(m => m.HistoryId, m => m.LancadoEm!.Value);
        if (permitidos.Count == 0) return 0;

        var ids = permitidos.Keys.ToList();
        var linhas = await _db.LeadStageHistories
            .Where(h => ids.Contains(h.Id) && h.CorrectedChangedAt == null)
            .ToListAsync(ct);

        foreach (var h in linhas)
        {
            h.CorrectedChangedAt = MeioDoDia(permitidos[h.Id]);
            h.CorrectedAt = DateTime.UtcNow;
            h.CorrectedByUserId = quemId;
            h.CorrectedByEmail = quemEmail;
            h.CorrectionReason = Motivo;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Datação de migração: {N} movimentações corrigidas na unidade {UnitId} por {Quem}",
            linhas.Count, unitId, quemEmail ?? "(interno)");
        return linhas.Count;
    }
}
