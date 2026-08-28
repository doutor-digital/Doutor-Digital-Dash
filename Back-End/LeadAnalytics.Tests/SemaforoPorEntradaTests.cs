using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// O card do "◉ Semáforo" quebra o desfecho da consulta por cor. O período da tela
/// precisa recortar pela ENTRADA NA ETAPA, não pela criação do lead.
///
/// O DEFEITO QUE ESTES TESTES TRAVAM
/// ---------------------------------
/// Medido em 28/08/2026, em produção: 31 leads com semáforo preenchido nos últimos 30
/// dias e ZERO criados naquele dia. Com o filtro por data de criação, o card do dia
/// mostrava "—" enquanto a clínica registrava desfecho o dia inteiro — e um card vazio
/// é lido como "não aconteceu nada", não como "estou perguntando a data errada".
///
/// A causa é conceitual, não de código: um lead nasce em julho e a consulta dele
/// acontece em agosto. Criação e desfecho são datas diferentes, e o card promete a
/// segunda.
/// </summary>
public class SemaforoPorEntradaTests
{
    private const int Tenant = 8033;
    private const int Unidade = 15;
    private const int EtapaCompareceu = 108773012;
    private const long CampoSemaforo = 2446619;

    private static readonly DateTime De = new(2026, 8, 28, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Ate = new(2026, 8, 28, 23, 59, 59, DateTimeKind.Utc);

    /// O AppDbContext exige um ICurrentUser (usado na auditoria de escrita). Estes
    /// testes só leem, então um usuário anônimo basta.
    private sealed class UsuarioNulo : ICurrentUser
    {
        public int? UserId => null;
        public int? TenantId => Tenant;
        public string? Role => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsAdminLevel => false;
        public bool IsReadOnly => false;
        public bool IsAuthenticated => false;
        public long? SessionId => null;
        public bool IsOwner => false;
    }

    private static AppDbContext NovoBanco(string nome) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(nome)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics
                .InMemoryEventId.TransactionIgnoredWarning))
            .Options, new UsuarioNulo());

    private static KpiConfigService NovoServico(AppDbContext db) =>
        // ComputeBreakdownAsync não toca nos serviços da franquia; passá-los nulos
        // mantém o teste focado no recorte de data, que é o que está sob suspeita.
        new(db, null!, null!, NullLogger<KpiConfigService>.Instance);

    private static string Json(string valor) =>
        $$"""[{"field_id":{{CampoSemaforo}},"field_name":"◉ Semáforo","value":"{{valor}}"}]""";

    private static Lead NovoLead(int id, DateTime criadoEm, string? semaforo, int? etapaAtual)
        => new()
        {
            Id = id,
            Name = $"Paciente {id}",
            Phone = $"6399000{id:0000}",
            TenantId = Tenant,
            UnitId = Unidade,
            CreatedAt = criadoEm,
            UpdatedAt = criadoEm,
            CurrentStageId = etapaAtual,
            Status = "active",
            CustomFieldsJson = semaforo is null ? null : Json(semaforo),
        };

    private static LeadStageHistory Entrada(int id, int leadId, DateTime quando, string fonte)
        => new()
        {
            Id = id,
            LeadId = leadId,
            StageId = EtapaCompareceu,
            StageLabel = "COMPARECEU",
            ChangedAt = quando,
            EntrySource = fonte,
        };

    private static JsonElement Config(bool porEntrada) =>
        JsonDocument.Parse(
            $$"""
            {"fieldId": {{CampoSemaforo}}, "matchValues": [], "stageIds": [{{EtapaCompareceu}}],
             "porEntradaNaEtapa": {{(porEntrada ? "true" : "false")}}}
            """).RootElement;

    /// O caso real do dia 28/08: lead criado ONTEM, consulta registrada HOJE.
    [Fact]
    public async Task Conta_lead_criado_antes_que_entrou_na_etapa_dentro_do_periodo()
    {
        using var db = NovoBanco(nameof(Conta_lead_criado_antes_que_entrou_na_etapa_dentro_do_periodo));
        db.Leads.Add(NovoLead(1, new DateTime(2026, 8, 27, 10, 0, 0, DateTimeKind.Utc),
            "VERDE — fechou e pagou tudo", EtapaCompareceu));
        db.LeadStageHistories.Add(Entrada(1, 1,
            new DateTime(2026, 8, 28, 13, 26, 0, DateTimeKind.Utc), LeadStageHistory.SourceWebhook));
        await db.SaveChangesAsync();

        var r = await NovoServico(db).ComputeBreakdownAsync(Tenant, Unidade, Config(true), De, Ate);

        Assert.Single(r);
        Assert.Equal("VERDE — fechou e pagou tudo", r[0].Label);
        Assert.Equal(1, r[0].Value);
    }

    /// O comportamento antigo, para deixar o defeito explícito no teste.
    [Fact]
    public async Task Sem_a_flag_o_mesmo_lead_some_do_periodo()
    {
        using var db = NovoBanco(nameof(Sem_a_flag_o_mesmo_lead_some_do_periodo));
        db.Leads.Add(NovoLead(1, new DateTime(2026, 8, 27, 10, 0, 0, DateTimeKind.Utc),
            "VERDE — fechou e pagou tudo", EtapaCompareceu));
        db.LeadStageHistories.Add(Entrada(1, 1,
            new DateTime(2026, 8, 28, 13, 26, 0, DateTimeKind.Utc), LeadStageHistory.SourceWebhook));
        await db.SaveChangesAsync();

        var r = await NovoServico(db).ComputeBreakdownAsync(Tenant, Unidade, Config(false), De, Ate);

        Assert.Empty(r);
    }

    /// Entrou na etapa e depois seguiu para NEGOCIAÇÃO: o desfecho aconteceu do mesmo jeito.
    [Fact]
    public async Task Conta_mesmo_que_o_card_ja_tenha_saido_da_etapa()
    {
        using var db = NovoBanco(nameof(Conta_mesmo_que_o_card_ja_tenha_saido_da_etapa));
        db.Leads.Add(NovoLead(1, new DateTime(2026, 7, 2, 10, 0, 0, DateTimeKind.Utc),
            "AMARELO — não fechou: dinheiro", etapaAtual: 999999));
        db.LeadStageHistories.Add(Entrada(1, 1,
            new DateTime(2026, 8, 28, 9, 0, 0, DateTimeKind.Utc), LeadStageHistory.SourceEventsApi));
        await db.SaveChangesAsync();

        var r = await NovoServico(db).ComputeBreakdownAsync(Tenant, Unidade, Config(true), De, Ate);

        Assert.Single(r);
        Assert.Equal("AMARELO — não fechou: dinheiro", r[0].Label);
    }

    /// Linha `legacy` tem ChangedAt = updated_at do lead, não a data de entrada.
    /// Contá-la jogaria todo desfecho para o dia do último sync.
    [Fact]
    public async Task Ignora_historico_legacy_porque_a_data_dele_nao_e_a_entrada()
    {
        using var db = NovoBanco(nameof(Ignora_historico_legacy_porque_a_data_dele_nao_e_a_entrada));
        db.Leads.Add(NovoLead(1, new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc),
            "VERDE — fechou e pagou tudo", EtapaCompareceu));
        db.LeadStageHistories.Add(Entrada(1, 1,
            new DateTime(2026, 8, 28, 11, 0, 0, DateTimeKind.Utc), LeadStageHistory.SourceLegacy));
        await db.SaveChangesAsync();

        var r = await NovoServico(db).ComputeBreakdownAsync(Tenant, Unidade, Config(true), De, Ate);

        Assert.Empty(r);
    }

    /// Entrou na etapa hoje mas ninguém preencheu o campo: não inventa cor.
    [Fact]
    public async Task Lead_sem_semaforo_preenchido_nao_entra()
    {
        using var db = NovoBanco(nameof(Lead_sem_semaforo_preenchido_nao_entra));
        db.Leads.Add(NovoLead(1, new DateTime(2026, 8, 27, 10, 0, 0, DateTimeKind.Utc),
            semaforo: null, etapaAtual: EtapaCompareceu));
        db.LeadStageHistories.Add(Entrada(1, 1,
            new DateTime(2026, 8, 28, 13, 0, 0, DateTimeKind.Utc), LeadStageHistory.SourceWebhook));
        await db.SaveChangesAsync();

        var r = await NovoServico(db).ComputeBreakdownAsync(Tenant, Unidade, Config(true), De, Ate);

        Assert.Empty(r);
    }

    /// Entrada FORA da janela não conta — senão o filtro de período não filtra nada.
    [Fact]
    public async Task Entrada_de_ontem_nao_aparece_no_card_de_hoje()
    {
        using var db = NovoBanco(nameof(Entrada_de_ontem_nao_aparece_no_card_de_hoje));
        db.Leads.Add(NovoLead(1, new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc),
            "VERDE — fechou e pagou tudo", EtapaCompareceu));
        db.LeadStageHistories.Add(Entrada(1, 1,
            new DateTime(2026, 8, 27, 15, 0, 0, DateTimeKind.Utc), LeadStageHistory.SourceWebhook));
        await db.SaveChangesAsync();

        var r = await NovoServico(db).ComputeBreakdownAsync(Tenant, Unidade, Config(true), De, Ate);

        Assert.Empty(r);
    }

    /// Lead de OUTRA unidade não vaza para o card da unidade selecionada.
    [Fact]
    public async Task Nao_vaza_lead_de_outra_unidade()
    {
        using var db = NovoBanco(nameof(Nao_vaza_lead_de_outra_unidade));
        var outro = NovoLead(1, new DateTime(2026, 8, 27, 10, 0, 0, DateTimeKind.Utc),
            "VERDE — fechou e pagou tudo", EtapaCompareceu);
        outro.UnitId = 99;
        db.Leads.Add(outro);
        db.LeadStageHistories.Add(Entrada(1, 1,
            new DateTime(2026, 8, 28, 13, 0, 0, DateTimeKind.Utc), LeadStageHistory.SourceWebhook));
        await db.SaveChangesAsync();

        var r = await NovoServico(db).ComputeBreakdownAsync(Tenant, Unidade, Config(true), De, Ate);

        Assert.Empty(r);
    }

    /// Lead apagado na Kommo não pode ressuscitar como desfecho.
    [Fact]
    public async Task Lead_deletado_nao_entra()
    {
        using var db = NovoBanco(nameof(Lead_deletado_nao_entra));
        var morto = NovoLead(1, new DateTime(2026, 8, 27, 10, 0, 0, DateTimeKind.Utc),
            "VERDE — fechou e pagou tudo", EtapaCompareceu);
        morto.Status = "deleted";
        db.Leads.Add(morto);
        db.LeadStageHistories.Add(Entrada(1, 1,
            new DateTime(2026, 8, 28, 13, 0, 0, DateTimeKind.Utc), LeadStageHistory.SourceWebhook));
        await db.SaveChangesAsync();

        var r = await NovoServico(db).ComputeBreakdownAsync(Tenant, Unidade, Config(true), De, Ate);

        Assert.Empty(r);
    }

    /// Várias cores no mesmo dia: agrupa e ordena por volume.
    [Fact]
    public async Task Agrupa_por_cor_e_ordena_pela_maior()
    {
        using var db = NovoBanco(nameof(Agrupa_por_cor_e_ordena_pela_maior));
        var ontem = new DateTime(2026, 8, 27, 10, 0, 0, DateTimeKind.Utc);
        var hoje = new DateTime(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

        db.Leads.AddRange(
            NovoLead(1, ontem, "AMARELO — não fechou: dinheiro", EtapaCompareceu),
            NovoLead(2, ontem, "AMARELO — não fechou: dinheiro", EtapaCompareceu),
            NovoLead(3, ontem, "VERDE — fechou e pagou tudo", EtapaCompareceu));
        db.LeadStageHistories.AddRange(
            Entrada(1, 1, hoje, LeadStageHistory.SourceWebhook),
            Entrada(2, 2, hoje, LeadStageHistory.SourceWebhook),
            Entrada(3, 3, hoje, LeadStageHistory.SourceWebhook));
        await db.SaveChangesAsync();

        var r = await NovoServico(db).ComputeBreakdownAsync(Tenant, Unidade, Config(true), De, Ate);

        Assert.Equal(2, r.Count);
        Assert.Equal("AMARELO — não fechou: dinheiro", r[0].Label);
        Assert.Equal(2, r[0].Value);
        Assert.Equal("VERDE — fechou e pagou tudo", r[1].Label);
    }

    // ─── Receita pela Kommo: soma so o que FECHOU no periodo ────────────────────

    private const long CampoValor = 2445206;

    private static Lead LeadComValor(int id, DateTime criadoEm, string valor, int? etapa)
    {
        var l = NovoLead(id, criadoEm, null, etapa);
        l.CustomFieldsJson =
            $$"""[{"field_id":{{CampoValor}},"field_name":"¤ Valor do tratamento","value":"{{valor}}"}]""";
        return l;
    }

    private static JsonElement ConfigSoma() =>
        JsonDocument.Parse(
            $$"""
            {"fieldId": {{CampoValor}}, "stageIds": [{{EtapaCompareceu}}], "porEntradaNaEtapa": true}
            """).RootElement;

    /// Fechou hoje, mas o lead nasceu mes passado: o dinheiro é de hoje.
    [Fact]
    public async Task Soma_o_que_fechou_no_periodo_mesmo_com_lead_antigo()
    {
        using var db = NovoBanco(nameof(Soma_o_que_fechou_no_periodo_mesmo_com_lead_antigo));
        db.Leads.Add(LeadComValor(1, new DateTime(2026, 6, 10, 9, 0, 0, DateTimeKind.Utc), "3800", EtapaCompareceu));
        db.LeadStageHistories.Add(Entrada(1, 1,
            new DateTime(2026, 8, 28, 14, 0, 0, DateTimeKind.Utc), LeadStageHistory.SourceWebhook));
        await db.SaveChangesAsync();

        var (valor, _, _) = await NovoServico(db).ComputeAsync(
            Tenant, Unidade, "custom_field_sum", ConfigSoma(), De, Ate);

        Assert.Equal(3800d, valor);
    }

    /// O caso que motivou tudo: lead criado HOJE, com valor preenchido, que NAO fechou.
    /// Nao pode entrar na receita do dia.
    [Fact]
    public async Task Nao_soma_valor_de_lead_que_nao_fechou()
    {
        using var db = NovoBanco(nameof(Nao_soma_valor_de_lead_que_nao_fechou));
        db.Leads.Add(LeadComValor(1, new DateTime(2026, 8, 28, 9, 0, 0, DateTimeKind.Utc), "5000", etapa: 999999));
        await db.SaveChangesAsync();

        var (valor, _, _) = await NovoServico(db).ComputeAsync(
            Tenant, Unidade, "custom_field_sum", ConfigSoma(), De, Ate);

        Assert.Equal(0d, valor);
    }

    /// Fechou ONTEM: sai da receita de hoje.
    [Fact]
    public async Task Nao_soma_o_que_fechou_fora_da_janela()
    {
        using var db = NovoBanco(nameof(Nao_soma_o_que_fechou_fora_da_janela));
        db.Leads.Add(LeadComValor(1, new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc), "3800", EtapaCompareceu));
        db.LeadStageHistories.Add(Entrada(1, 1,
            new DateTime(2026, 8, 27, 14, 0, 0, DateTimeKind.Utc), LeadStageHistory.SourceWebhook));
        await db.SaveChangesAsync();

        var (valor, _, _) = await NovoServico(db).ComputeAsync(
            Tenant, Unidade, "custom_field_sum", ConfigSoma(), De, Ate);

        Assert.Equal(0d, valor);
    }

    /// Dois fechamentos no dia somam; o de outra unidade nao entra.
    [Fact]
    public async Task Soma_varios_e_respeita_a_unidade()
    {
        using var db = NovoBanco(nameof(Soma_varios_e_respeita_a_unidade));
        var criado = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc);
        var fechou = new DateTime(2026, 8, 28, 11, 0, 0, DateTimeKind.Utc);

        db.Leads.Add(LeadComValor(1, criado, "3800", EtapaCompareceu));
        db.Leads.Add(LeadComValor(2, criado, "1800", EtapaCompareceu));
        var deOutra = LeadComValor(3, criado, "9999", EtapaCompareceu);
        deOutra.UnitId = 99;
        db.Leads.Add(deOutra);
        db.LeadStageHistories.AddRange(
            Entrada(1, 1, fechou, LeadStageHistory.SourceWebhook),
            Entrada(2, 2, fechou, LeadStageHistory.SourceWebhook),
            Entrada(3, 3, fechou, LeadStageHistory.SourceWebhook));
        await db.SaveChangesAsync();

        var (valor, _, _) = await NovoServico(db).ComputeAsync(
            Tenant, Unidade, "custom_field_sum", ConfigSoma(), De, Ate);

        Assert.Equal(5600d, valor);
    }
}
