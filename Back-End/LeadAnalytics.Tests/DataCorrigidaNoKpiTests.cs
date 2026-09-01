using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// A data corrigida tem de chegar ao NÚMERO, não só ao banco.
///
/// POR QUE ESTE ARQUIVO EXISTE
/// ---------------------------
/// O campo CorrectedChangedAt existia havia tempo, a tela de correção existia, e o
/// admin podia corrigir a data de uma transição. Só que o KPI de receita filtrava por
/// ChangedAt cru e ignorava a correção — então corrigir não mudava nada, em silêncio.
///
/// Medido na Imperatriz em 01/09/2026: 115 movimentações foram corrigidas com a data do
/// lançamento na franquia (mutirões de 24/07 e 26/08) e a receita dos meses não se moveu
/// um centavo. Janeiro tinha 11 tratamentos e R$ 0; julho tinha 10 tratamentos e
/// R$ 162.470 — ticket de R$ 16 mil. O dinheiro estava todo empilhado no dia do mutirão.
///
/// É o pior tipo de defeito: a ferramenta de conserto parecia funcionar.
/// </summary>
public class DataCorrigidaNoKpiTests
{
    private const int Tenant = 1;
    private const int Unidade = 15;
    private const int EtapaEmTratamento = 108773168;
    private const long CampoValor = 2440829;

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

    private static AppDbContext NovoBanco([System.Runtime.CompilerServices.CallerMemberName] string nome = "") =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("datacorrigida-" + nome)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics
                .InMemoryEventId.TransactionIgnoredWarning))
            .Options, new UsuarioNulo());

    private static KpiConfigService Servico(AppDbContext db) =>
        new(db, null!, null!, NullLogger<KpiConfigService>.Instance);

    /// Config real da Imperatriz: soma o campo de valor de quem ENTROU na etapa.
    private static JsonElement Config() =>
        JsonDocument.Parse($$"""
            {"fieldId":{{CampoValor}},"stageIds":[{{EtapaEmTratamento}}],"porEntradaNaEtapa":true}
            """).RootElement;

    private static void Semear(AppDbContext db, int leadId, decimal valor,
                               DateTime arrastadoEm, DateTime? dataCorrigida)
    {
        db.Leads.Add(new Lead
        {
            Id = leadId, ExternalId = 900_000 + leadId, Name = $"Paciente {leadId}",
            Phone = $"6399000{leadId:0000}", TenantId = Tenant, UnitId = Unidade,
            CreatedAt = arrastadoEm.AddMonths(-2), UpdatedAt = arrastadoEm, Status = "active",
            CustomFieldsJson = $$"""
                [{"field_id":{{CampoValor}},"field_name":"¤ Valor do tratamento","value":"{{valor}}"}]
                """,
        });
        db.LeadStageHistories.Add(new LeadStageHistory
        {
            Id = leadId, LeadId = leadId, StageId = EtapaEmTratamento,
            StageLabel = "EM TRATAMENTO", ChangedAt = arrastadoEm,
            EntrySource = LeadStageHistory.SourceWebhook, CorrectedChangedAt = dataCorrigida,
        });
        db.SaveChanges();
    }

    private static Task<(double Value, int Sample, string? Note)> Receita(
        AppDbContext db, string mesDe, string mesAte) =>
        Servico(db).ComputeAsync(
            Tenant, Unidade, KpiSourceTypes.CustomFieldSum, Config(),
            DateTime.Parse(mesDe), DateTime.Parse(mesAte), null, "receita");

    /// O caso da Imperatriz: card arrastado em 26/08 num mutirão, tratamento fechado em
    /// 14/08. O dinheiro tem de contar em AGOSTO na data real, não na do arraste — e
    /// aqui as duas caem no mesmo mês, então o teste que separa é o seguinte.
    [Fact]
    public async Task Receita_conta_pela_data_corrigida()
    {
        using var db = NovoBanco();
        // Arrastado em agosto, mas o tratamento fechou em JANEIRO.
        Semear(db, 1, 3680m,
            arrastadoEm: new DateTime(2026, 8, 26, 19, 13, 0, DateTimeKind.Utc),
            dataCorrigida: new DateTime(2026, 1, 12, 15, 0, 0, DateTimeKind.Utc));

        var janeiro = await Receita(db, "2026-01-01", "2026-02-01");
        var agosto = await Receita(db, "2026-08-01", "2026-09-01");

        Assert.Equal(3680d, janeiro.Value);
        Assert.Equal(0d, agosto.Value);
    }

    /// Sem correção, vale a data do arraste — o comportamento de sempre não muda.
    [Fact]
    public async Task Sem_correcao_vale_a_data_do_arraste()
    {
        using var db = NovoBanco();
        Semear(db, 1, 3680m,
            arrastadoEm: new DateTime(2026, 8, 26, 19, 13, 0, DateTimeKind.Utc),
            dataCorrigida: null);

        var agosto = await Receita(db, "2026-08-01", "2026-09-01");
        var janeiro = await Receita(db, "2026-01-01", "2026-02-01");

        Assert.Equal(3680d, agosto.Value);
        Assert.Equal(0d, janeiro.Value);
    }

    /// O mutirão inteiro: três tratamentos de meses diferentes empilhados num dia só.
    /// Antes da correção, agosto somava os três; depois, cada mês fica com o seu.
    [Fact]
    public async Task Mutirao_devolve_cada_tratamento_ao_seu_mes()
    {
        using var db = NovoBanco();
        var mutirao = new DateTime(2026, 8, 26, 19, 13, 0, DateTimeKind.Utc);
        Semear(db, 1, 3680m, mutirao, new DateTime(2026, 1, 12, 15, 0, 0, DateTimeKind.Utc));
        Semear(db, 2, 4200m, mutirao, new DateTime(2026, 4, 24, 15, 0, 0, DateTimeKind.Utc));
        Semear(db, 3, 3500m, mutirao, new DateTime(2026, 8, 14, 15, 0, 0, DateTimeKind.Utc));

        Assert.Equal(3680d, (await Receita(db, "2026-01-01", "2026-02-01")).Value);
        Assert.Equal(4200d, (await Receita(db, "2026-04-01", "2026-05-01")).Value);
        Assert.Equal(3500d, (await Receita(db, "2026-08-01", "2026-09-01")).Value);
    }

    /// Correção que cai FORA do período pedido não pode vazar para dentro dele.
    [Fact]
    public async Task Correcao_fora_do_periodo_nao_entra()
    {
        using var db = NovoBanco();
        Semear(db, 1, 3680m,
            arrastadoEm: new DateTime(2026, 8, 26, 19, 13, 0, DateTimeKind.Utc),
            dataCorrigida: new DateTime(2025, 12, 30, 15, 0, 0, DateTimeKind.Utc));

        Assert.Equal(0d, (await Receita(db, "2026-01-01", "2026-02-01")).Value);
        Assert.Equal(0d, (await Receita(db, "2026-08-01", "2026-09-01")).Value);
    }
}
