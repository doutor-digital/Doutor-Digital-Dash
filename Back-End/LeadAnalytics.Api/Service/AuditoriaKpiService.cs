using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Auditoria dos cards: para cada KPI, o número, de onde ele vem e QUEM está
/// fora do lugar — nominalmente.
///
/// POR QUE ISTO EXISTE
/// -------------------
/// Metade dos cards vem do CRM da franquia (o fato clínico: quem compareceu,
/// quem fechou tratamento) e metade vem da Kommo (a intenção comercial: quem
/// agendou, qual a origem, por que não agendou). O da franquia é digitado pela
/// recepção; o da Kommo é digitado pela SDR — e é aí que os números deixam de
/// bater, porque campo em branco não vira zero na tela: vira número errado.
///
/// Um painel que só mostra o total não resolve isso: a gestora vê "2 agendados"
/// e não tem como saber que houve 5 e três ficaram sem o carimbo. Por isso cada
/// bloco aqui devolve três coisas:
///   • o número que o card mostra hoje;
///   • a mesma pergunta medida por outro caminho (etapa, evento ou franquia);
///   • a LISTA dos cartões que explicam a diferença.
///
/// A lista é o que faz o número ser corrigido — é ela que a SDR abre e conserta.
/// </summary>
public sealed class AuditoriaKpiService(AppDbContext db)
{
    /// <summary>Um cartão que está fora do padrão, com o motivo em português.</summary>
    public sealed record Divergencia(int LeadId, string Nome, string Motivo);

    /// <summary>Uma linha de quebra (origem, motivo, tipo de tratamento…).</summary>
    public sealed record Fatia(string Rotulo, int Quantidade, decimal? Valor = null);

    /// <summary>
    /// Quanto do período o card consegue LER. Sem isto um card com metade do
    /// histórico ilegível mostra um número redondo e ninguém desconfia — foi o
    /// caso por 90 dias: 25% das linhas apontam para etapas apagadas na Kommo
    /// durante as replicações, e o nome delas não existe mais em lugar nenhum.
    /// </summary>
    public sealed record Cobertura(int Total, int Legiveis, string Nota)
    {
        public int Percentual => Total == 0 ? 100 : (int)Math.Round(100.0 * Legiveis / Total);
    }

    public sealed record Bloco(
        string Kpi,
        string Fonte,
        int Numero,
        int? Conferencia,
        string? Leitura,
        IReadOnlyList<Fatia> Quebra,
        IReadOnlyList<Divergencia> Divergentes,
        Cobertura? Cobertura = null);

    private static string NomeCurto(string? n) => string.IsNullOrWhiteSpace(n) ? "(sem nome)" : n.Trim();

    /// <summary>
    /// AGENDAMENTOS — quebra por pagamento antecipado.
    /// O card conta pelo carimbo "◷ Agendado pela SDR em"; a conferência conta
    /// quem está na etapa AGENDADO. Cartão na etapa sem carimbo é agendamento
    /// que o relatório não enxerga.
    /// </summary>
    public async Task<Bloco> AgendamentosAsync(int unitId, DateTime de, DateTime ate, CancellationToken ct)
    {
        var carimbados = await db.Leads.AsNoTracking()
            .Where(l => l.UnitId == unitId
                && l.AppointmentScheduledAtFilledAt >= de && l.AppointmentScheduledAtFilledAt < ate)
            .Select(l => new { l.Id, l.Name, l.HasPayment, l.CurrentStage })
            .ToListAsync(ct);

        var comPag = carimbados.Count(x => x.HasPayment);
        var quebra = new List<Fatia>
        {
            new("Com pagamento antecipado", comPag),
            new("Sem pagamento antecipado", carimbados.Count - comPag),
        };

        // Quem entrou na etapa AGENDADO no período e não recebeu o carimbo.
        var naEtapa = await db.Leads.AsNoTracking()
            .Where(l => l.UnitId == unitId
                && l.CurrentStage != null && l.CurrentStage.ToUpper().Contains("AGENDADO")
                && l.UpdatedAt >= de && l.UpdatedAt < ate)
            .Select(l => new { l.Id, l.Name, l.AppointmentScheduledAtFilledAt })
            .ToListAsync(ct);

        var semCarimbo = naEtapa
            .Where(l => l.AppointmentScheduledAtFilledAt == null)
            .Select(l => new Divergencia(l.Id, NomeCurto(l.Name),
                "está na etapa AGENDADO mas o campo \"Agendado pela SDR em\" está vazio — não entra no card"))
            .ToList();

        return new Bloco("agendamentos", "CRM (Kommo)", carimbados.Count, naEtapa.Count,
            semCarimbo.Count == 0
                ? "carimbo e etapa batem"
                : $"{semCarimbo.Count} cartão(ões) na etapa AGENDADO sem o carimbo — o card está subcontando",
            quebra, semCarimbo,
            await CoberturaDoHistoricoAsync(unitId, de, ate, ct));
    }

    /// <summary>
    /// CONSULTAS — quebra por origem do lead.
    /// O número do card é da franquia (quem compareceu de verdade); a origem só
    /// existe na Kommo, então vem daqui e casa pelo lead que compareceu.
    /// </summary>
    public async Task<Bloco> ConsultasAsync(int unitId, DateTime de, DateTime ate, CancellationToken ct)
    {
        // O campo AttendanceStatus só é preenchido por quem usa a tela do painel, e
        // quase ninguém usa: na Imperatriz ele deu ZERO consultas num mês com 190
        // agendamentos. Quem sabe do comparecimento é a ETAPA no Kommo — a SDR move
        // o cartão porque o funil obriga. Então a etapa é a fonte e o campo vira
        // conferência, não o contrário.
        var etapas = new[] { LeadStages.Compareceu, LeadStages.EmTratamento };
        var entradas = await db.LeadStageHistories.AsNoTracking()
            .Where(h => h.ChangedAt >= de && h.ChangedAt < ate
                && etapas.Contains(h.StageLabel)
                && h.EntrySource != LeadStageHistory.SourceLegacy
                && db.Leads.Any(l => l.Id == h.LeadId && l.UnitId == unitId))
            .Select(h => new { h.LeadId, h.Lead.Name, h.Lead.Source, h.Lead.AttendanceStatus })
            .Distinct()
            .ToListAsync(ct);

        var quebra = entradas
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Source) ? "Sem origem" : x.Source)
            .Select(g => new Fatia(g.Key, g.Count()))
            .OrderByDescending(f => f.Quantidade)
            .ToList();

        var semOrigem = entradas
            .Where(x => string.IsNullOrWhiteSpace(x.Source) || x.Source == "DESCONHECIDO")
            .Select(x => new Divergencia(x.LeadId, NomeCurto(x.Name),
                "compareceu mas está sem origem — a consulta não é atribuída a nenhum anúncio"))
            .ToList();

        var pelaTela = entradas.Count(x =>
            x.AttendanceStatus != null && x.AttendanceStatus.ToUpper().Contains("COMPARECEU"));

        return new Bloco("consultas", "etapa no Kommo (fato) + origem do lead", entradas.Count, pelaTela,
            semOrigem.Count == 0
                ? "toda consulta tem origem"
                : $"{semOrigem.Count} consulta(s) sem origem — some da conta de retorno por canal",
            quebra, semOrigem,
            await CoberturaDoHistoricoAsync(unitId, de, ate, ct));
    }

    /// <summary>
    /// Quantas linhas do histórico do período dá pra ler. Rótulo com o id cru
    /// ("143") não casa com etapa nenhuma: o cartão existe, a movimentação
    /// aconteceu, e mesmo assim ele não entra em card nenhum.
    /// </summary>
    private async Task<Cobertura> CoberturaDoHistoricoAsync(
        int unitId, DateTime de, DateTime ate, CancellationToken ct)
    {
        var doPeriodo = db.LeadStageHistories.AsNoTracking()
            .Where(h => h.ChangedAt >= de && h.ChangedAt < ate
                && db.Leads.Any(l => l.Id == h.LeadId && l.UnitId == unitId));

        var total = await doPeriodo.CountAsync(ct);
        var crus = await doPeriodo.CountAsync(
            h => h.StageLabel != null
                && System.Text.RegularExpressions.Regex.IsMatch(h.StageLabel, "^[0-9]+$"), ct);

        return new Cobertura(total, total - crus,
            crus == 0
                ? "todo o histórico do período é legível"
                : $"{crus} movimentação(ões) com a etapa apagada na Kommo — não entram em card nenhum");
    }

    /// <summary>TRATAMENTOS — quebra pelo tipo do plano fechado.</summary>
    public async Task<Bloco> TratamentosAsync(int unitId, DateTime de, DateTime ate, CancellationToken ct)
    {
        var fechados = await db.Leads.AsNoTracking()
            .Where(l => l.UnitId == unitId && l.ClosedTreatment == true
                && l.UpdatedAt >= de && l.UpdatedAt < ate)
            .Select(l => new { l.Id, l.Name, l.TreatmentPlanCategory, l.TreatmentPlanValue })
            .ToListAsync(ct);

        var quebra = fechados
            .GroupBy(x => string.IsNullOrWhiteSpace(x.TreatmentPlanCategory) ? "Tipo não informado" : x.TreatmentPlanCategory!)
            .Select(g => new Fatia(g.Key, g.Count(), g.Sum(x => x.TreatmentPlanValue ?? 0m)))
            .OrderByDescending(f => f.Quantidade)
            .ToList();

        var problemas = fechados
            .Where(x => string.IsNullOrWhiteSpace(x.TreatmentPlanCategory))
            .Select(x => new Divergencia(x.Id, NomeCurto(x.Name),
                "fechou tratamento sem informar o tipo — não dá pra saber o que a unidade vende"))
            .ToList();

        // A clínica é o fato: o tratamento foi lançado na franquia, com valor. O CRM
        // é a intenção. Contar só o CRM escondia o buraco mais grave que a auditoria
        // achou — "0 tratamentos fechados" convivendo com R$48 mil lançados na
        // clínica no mesmo período, no mesmo painel, sem ninguém estranhar.
        var deDia = DateOnly.FromDateTime(de);
        var ateDia = DateOnly.FromDateTime(ate.AddDays(-1));
        var naFranquia = await db.FranquiaLeadLinks.AsNoTracking()
            .Where(v => v.UnitId == unitId && v.DiaLancamento >= deDia && v.DiaLancamento <= ateDia)
            .Select(v => new { v.Paciente, v.LeadId, v.PrecoFranquia })
            .ToListAsync(ct);

        var idsNoCrm = fechados.Select(x => (long)x.Id).ToHashSet();
        foreach (var v in naFranquia.Where(v => v.LeadId is null || !idsNoCrm.Contains(v.LeadId.Value)))
            problemas.Add(new Divergencia((int)(v.LeadId ?? 0), NomeCurto(v.Paciente),
                $"tratamento lançado na clínica ({v.PrecoFranquia ?? 0m:C0}) e o cartão não está marcado como fechado no CRM"));

        var soNaClinica = naFranquia.Count - fechados.Count;
        return new Bloco("tratamentos", "CRM (Kommo) × franquia (fato)", fechados.Count, naFranquia.Count,
            problemas.Count == 0
                ? "CRM e clínica contam o mesmo, e todo tratamento tem tipo"
                : soNaClinica > 0
                    ? $"a clínica lançou {naFranquia.Count} e o CRM marcou {fechados.Count} — {problemas.Count} cartão(ões) a acertar"
                    : $"{problemas.Count} tratamento(s) sem tipo informado",
            quebra, problemas);
    }

    /// <summary>
    /// RECEITA — o teste que importa: o valor que a SDR digitou na Kommo bate
    /// com o preço que a clínica lançou na franquia?
    /// A regra do card é "população da franquia, valor da Kommo"; aqui os dois
    /// aparecem lado a lado e cada divergência é nominal.
    /// </summary>
    public async Task<Bloco> ReceitaAsync(int unitId, DateTime de, DateTime ate, CancellationToken ct)
    {
        var deDia = DateOnly.FromDateTime(de);
        var ateDia = DateOnly.FromDateTime(ate.AddDays(-1));

        var vinculos = await db.FranquiaLeadLinks.AsNoTracking()
            .Where(v => v.UnitId == unitId && v.DiaLancamento >= deDia && v.DiaLancamento <= ateDia)
            .Select(v => new { v.Paciente, v.PrecoFranquia, v.ValorKommo, v.LeadId })
            .ToListAsync(ct);

        var totalFranquia = vinculos.Sum(v => v.PrecoFranquia ?? 0m);
        var totalKommo = vinculos.Sum(v => v.ValorKommo ?? 0m);

        var quebra = new List<Fatia>
        {
            new("Valor lançado na franquia", vinculos.Count, totalFranquia),
            new("Valor preenchido na Kommo", vinculos.Count(v => (v.ValorKommo ?? 0m) > 0m), totalKommo),
        };

        var problemas = new List<Divergencia>();
        foreach (var v in vinculos)
        {
            var kommo = v.ValorKommo ?? 0m;
            var franquia = v.PrecoFranquia ?? 0m;
            if (kommo <= 0m)
                problemas.Add(new Divergencia((int)(v.LeadId ?? 0), NomeCurto(v.Paciente),
                    $"tratamento lançado na clínica ({franquia:C0}) e o valor não foi preenchido na Kommo"));
            else if (franquia > 0m && Math.Abs(kommo - franquia) > 1m)
                problemas.Add(new Divergencia((int)(v.LeadId ?? 0), NomeCurto(v.Paciente),
                    $"valores diferentes: franquia {franquia:C0} × Kommo {kommo:C0}"));
        }

        var dif = totalKommo - totalFranquia;
        return new Bloco("receita", "franquia × Kommo", (int)Math.Round(totalKommo), (int)Math.Round(totalFranquia),
            problemas.Count == 0
                ? "receita da Kommo bate com o lançado na clínica"
                : $"{problemas.Count} tratamento(s) com valor divergente — diferença de {dif:C0}",
            quebra, problemas);
    }

    /// <summary>LEADS QUALIFICADOS — quebra por origem.</summary>
    public async Task<Bloco> QualificadosAsync(int unitId, DateTime de, DateTime ate, CancellationToken ct)
    {
        var qualificados = await db.Leads.AsNoTracking()
            .Where(l => l.UnitId == unitId && l.Qualification != null
                && l.QualificationFilledAt >= de && l.QualificationFilledAt < ate)
            .Select(l => new { l.Id, l.Name, l.Source, l.Qualification })
            .ToListAsync(ct);

        var quebra = qualificados
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Source) ? "Sem origem" : x.Source)
            .Select(g => new Fatia(g.Key, g.Count()))
            .OrderByDescending(f => f.Quantidade)
            .ToList();

        // Lead que entrou no período e ninguém qualificou: é o buraco silencioso.
        var semQualificar = await db.Leads.AsNoTracking()
            .Where(l => l.UnitId == unitId && l.CreatedAt >= de && l.CreatedAt < ate && l.Qualification == null)
            .Select(l => new { l.Id, l.Name })
            .Take(200)
            .ToListAsync(ct);

        var divergentes = semQualificar
            .Select(x => new Divergencia(x.Id, NomeCurto(x.Name), "entrou no período e não foi qualificado"))
            .ToList();

        return new Bloco("leads_qualificados", "CRM (Kommo)", qualificados.Count, null,
            divergentes.Count == 0
                ? "todo lead do período foi qualificado"
                : $"{divergentes.Count} lead(s) do período sem qualificação",
            quebra, divergentes);
    }

    /// <summary>NO-SHOW — quebra pelo motivo do não agendamento.</summary>
    public async Task<Bloco> NoShowAsync(int unitId, DateTime de, DateTime ate, CancellationToken ct)
    {
        var faltaram = await db.Leads.AsNoTracking()
            .Where(l => l.UnitId == unitId
                && l.AttendanceStatusAt >= de && l.AttendanceStatusAt < ate
                && l.AttendanceStatus != null && l.AttendanceStatus.ToUpper().Contains("NAO"))
            .Select(l => new { l.Id, l.Name, l.NoAppointmentReason })
            .ToListAsync(ct);

        var quebra = faltaram
            .GroupBy(x => string.IsNullOrWhiteSpace(x.NoAppointmentReason) ? "Motivo não informado" : x.NoAppointmentReason!)
            .Select(g => new Fatia(g.Key, g.Count()))
            .OrderByDescending(f => f.Quantidade)
            .ToList();

        var semMotivo = faltaram
            .Where(x => string.IsNullOrWhiteSpace(x.NoAppointmentReason))
            .Select(x => new Divergencia(x.Id, NomeCurto(x.Name),
                "não compareceu e ninguém registrou o motivo — não dá pra atacar a causa"))
            .ToList();

        return new Bloco("no_show", "franquia (fato) + motivo da Kommo", faltaram.Count, null,
            semMotivo.Count == 0 ? "todo no-show tem motivo" : $"{semMotivo.Count} no-show(s) sem motivo registrado",
            quebra, semMotivo);
    }

    public async Task<IReadOnlyList<Bloco>> TudoAsync(int unitId, DateTime de, DateTime ate, CancellationToken ct)
    {
        return new List<Bloco>
        {
            await AgendamentosAsync(unitId, de, ate, ct),
            await ConsultasAsync(unitId, de, ate, ct),
            await TratamentosAsync(unitId, de, ate, ct),
            await ReceitaAsync(unitId, de, ate, ct),
            await QualificadosAsync(unitId, de, ate, ct),
            await NoShowAsync(unitId, de, ate, ct),
        };
    }
}
