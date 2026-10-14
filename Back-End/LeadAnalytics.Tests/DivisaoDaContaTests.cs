using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service;
using LeadAnalytics.Api.Service.Stages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// Uma conta da Kommo, duas unidades.
///
/// POR QUE ESTE ARQUIVO EXISTE
/// ---------------------------
/// Petrópolis (doutorhernialvp) é um Kommo só para duas clínicas, Petrópolis e Caxias; cada
/// cartão diz de qual é no campo ⌂ Unidade de atendimento. Regra do João (14/10/2026): são
/// DUAS unidades e tudo separa as duas. O erro que estes testes impedem não grita: o painel
/// de Petrópolis mostrando também os leads de Caxias é um número plausível — maior, mas
/// plausível — e ninguém desconfia.
///
/// E os cartões com o campo VAZIO (180 da carga de implantação, em 14/10) não podem ser
/// somados a nenhuma das duas sem ninguém perceber: vão para a unidade "sem unidade
/// definida", que aparece com nome próprio.
/// </summary>
public class DivisaoDaContaTests
{
    private const long Campo = 479786;
    private const long EnumPetropolis = 386142;
    private const long EnumCaxias = 386144;
    private const string Sub = "doutorhernialvp";

    private sealed class UsuarioNulo : ICurrentUser
    {
        public int? UserId => null;
        public int? TenantId => null;
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
            .UseInMemoryDatabase("divisao-" + nome)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics
                .InMemoryEventId.TransactionIgnoredWarning))
            .Options, new UsuarioNulo());

    private static Unit Petropolis => new()
    {
        Id = 40, ClinicId = 8043, Name = "Doutor Hérnia Petrópolis", Slug = "doutor-hernia-petropolis",
        KommoSubdomain = Sub, KommoAccessToken = "tk", CreatedAt = DateTime.UtcNow,
    };
    private static Unit Caxias => new()
    {
        Id = 41, ClinicId = 8044, Name = "Doutor Hérnia Caxias", Slug = "doutor-hernia-caxias",
        KommoSubdomain = Sub, KommoAccessToken = "tk", CreatedAt = DateTime.UtcNow,
    };
    private static Unit SemUnidade => new()
    {
        Id = 42, ClinicId = 8045, Name = "Petrópolis/Caxias · sem unidade definida",
        Slug = "petropolis-caxias-sem-unidade", KommoSubdomain = Sub, CreatedAt = DateTime.UtcNow,
    };
    private static Unit Serra => new()
    {
        Id = 24, ClinicId = 8031, Name = "Doutor Hérnia Serra", Slug = "doutor-hernia-serra",
        KommoSubdomain = "drherniaserra", KommoAccessToken = "tk", CreatedAt = DateTime.UtcNow,
    };

    private const string ConfigValida = """
        {
          "campoId": 479786,
          "nomeDoCampo": "⌂ Unidade de atendimento",
          "dona": "doutor-hernia-petropolis",
          "semUnidade": "petropolis-caxias-sem-unidade",
          "opcoes": [
            { "enumId": 386142, "valor": "Petrópolis", "unidade": "doutor-hernia-petropolis" },
            { "enumId": 386144, "valor": "Caxias", "unidade": "doutor-hernia-caxias" }
          ]
        }
        """;

    private static async Task<AppDbContext> BancoComDivisao(
        string config = ConfigValida, [System.Runtime.CompilerServices.CallerMemberName] string nome = "")
    {
        var db = NovoBanco(nome);
        db.Units.AddRange(Petropolis, Caxias, SemUnidade, Serra);
        db.AppConfigurations.Add(new AppConfiguration
        {
            Key = DivisaoDaContaService.ChaveDe(Sub), Value = config,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return db;
    }

    private static DivisaoDaContaService Divisao(AppDbContext db) =>
        new(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<DivisaoDaContaService>.Instance);

    private static KommoIngestionService Ingestao(AppDbContext db) =>
        new(db,
            new KommoStageProcessor(db, NullLogger<KommoStageProcessor>.Instance),
            new KpiConfigService(db, null!, null!, NullLogger<KpiConfigService>.Instance),
            Divisao(db),
            NullLogger<KommoIngestionService>.Instance);

    /// <summary>Formato do sync REST: traz o id da opção e a lista inteira de campos.</summary>
    private static LeadEvent DoSync(int id, long? enumId, string? valor, string acao = "add")
    {
        var campos = new List<object>
        {
            new { field_id = 111L, field_name = "⚑ Origem", field_code = (string?)null, type = "select", value = "Instagram", enum_id = 9L, enum_code = (string?)null },
        };
        if (enumId is not null || valor is not null)
            campos.Add(new { field_id = Campo, field_name = "⌂ Unidade de atendimento", field_code = (string?)null, type = "select", value = valor, enum_id = enumId, enum_code = (string?)null });
        return new LeadEvent
        {
            SourceSystem = "Kommo", EntityType = "lead", Action = acao, ExternalId = id.ToString(),
            Name = "Paciente " + id, CustomFieldsJson = JsonSerializer.Serialize(campos), CamposCompletos = true,
        };
    }

    /// <summary>Formato do webhook: só o texto da opção, e só os campos que vieram.</summary>
    private static LeadEvent DoWebhook(int id, string? valor, string acao = "update")
    {
        string? json = valor is null
            ? null
            : JsonSerializer.Serialize(new[] { new { field_id = Campo, field_name = "⌂ Unidade de atendimento", field_code = (string?)null, value = valor } });
        return new LeadEvent
        {
            SourceSystem = "Kommo", EntityType = "lead", Action = acao, ExternalId = id.ToString(),
            CustomFieldsJson = json, CamposCompletos = false,
        };
    }

    private static DivisaoDaConta DivisaoEmMemoria() => new()
    {
        Subdominio = Sub, CampoId = Campo, Dona = Petropolis, SemUnidade = SemUnidade,
        Opcoes =
        [
            new DivisaoDaConta.Opcao(Petropolis, EnumPetropolis, "Petrópolis"),
            new DivisaoDaConta.Opcao(Caxias, EnumCaxias, "Caxias"),
        ],
    };

    // ─── Leitura do campo ────────────────────────────────────────────────

    [Fact]
    public void Le_o_campo_no_formato_do_webhook_so_com_texto()
    {
        var (presente, enumId, valor) = DivisaoDaContaService.LerCampo(DoWebhook(1, "Caxias").CustomFieldsJson, Campo);
        Assert.True(presente);
        Assert.Null(enumId);
        Assert.Equal("Caxias", valor);
    }

    [Fact]
    public void Le_o_campo_no_formato_do_sync_com_o_id_da_opcao()
    {
        var (presente, enumId, _) = DivisaoDaContaService.LerCampo(DoSync(1, EnumPetropolis, "Petrópolis").CustomFieldsJson, Campo);
        Assert.True(presente);
        Assert.Equal(EnumPetropolis, enumId);
    }

    [Fact]
    public void Campo_ausente_nao_e_campo_presente()
    {
        Assert.False(DivisaoDaContaService.LerCampo(DoSync(1, null, null).CustomFieldsJson, Campo).Presente);
        Assert.False(DivisaoDaContaService.LerCampo(null, Campo).Presente);
        Assert.False(DivisaoDaContaService.LerCampo("isto não é json", Campo).Presente);
    }

    // ─── A regra ─────────────────────────────────────────────────────────

    [Fact]
    public void Campo_decide_pela_opcao_por_id_ou_por_texto_sem_acento()
    {
        var d = DivisaoEmMemoria();
        Assert.Equal(41, DivisaoDaContaService.Decidir(d, DoSync(1, EnumCaxias, "Caxias").CustomFieldsJson, true, null, null, false).Destino!.Id);
        Assert.Equal(40, DivisaoDaContaService.Decidir(d, DoWebhook(1, "petropolis").CustomFieldsJson, false, null, null, false).Destino!.Id);
        // O id da opção vence o texto: renomear a opção no Kommo não muda a unidade.
        Assert.Equal(41, DivisaoDaContaService.Decidir(d, DoSync(1, EnumCaxias, "Duque de Caxias").CustomFieldsJson, true, null, null, false).Destino!.Id);
    }

    [Fact]
    public void Opcao_que_a_configuracao_nao_conhece_vai_para_sem_unidade_e_nao_para_a_dona()
    {
        var d = DivisaoEmMemoria();
        var rota = DivisaoDaContaService.Decidir(d, DoWebhook(1, "Niterói").CustomFieldsJson, false, null, null, false);
        Assert.Equal(42, rota.Destino!.Id);
    }

    [Fact]
    public void Campo_vazio_no_sync_vai_para_sem_unidade_mesmo_que_o_cartao_estivesse_numa_unidade()
    {
        var d = DivisaoEmMemoria();
        var rota = DivisaoDaContaService.Decidir(d, DoSync(1, null, null).CustomFieldsJson, true, Caxias, null, false);
        Assert.Equal(42, rota.Destino!.Id);
    }

    [Fact]
    public void Webhook_sem_o_campo_nao_tira_o_cartao_de_onde_esta()
    {
        var d = DivisaoEmMemoria();
        Assert.Equal(41, DivisaoDaContaService.Decidir(d, null, false, Caxias, null, false).Destino!.Id);
        // Cartão novo sem o campo: espera em "sem unidade" até o campo chegar.
        Assert.Equal(42, DivisaoDaContaService.Decidir(d, null, false, null, null, false).Destino!.Id);
        // Mas no mesmo lote vale a decisão anterior (update com o campo + status sem ele).
        Assert.Equal(41, DivisaoDaContaService.Decidir(d, null, false, null, Caxias, false).Destino!.Id);
    }

    [Fact]
    public void Exclusao_vai_para_onde_o_cartao_esta_e_some_se_ele_nao_existe()
    {
        var d = DivisaoEmMemoria();
        Assert.Equal(41, DivisaoDaContaService.Decidir(d, null, false, Caxias, null, true).Destino!.Id);
        Assert.Null(DivisaoDaContaService.Decidir(d, null, false, null, null, true).Destino);
    }

    // ─── A ingestão inteira (webhook e sync passam pelo mesmo IngestAsync) ──

    [Fact]
    public async Task Sync_da_conta_reparte_os_cartoes_nas_tres_unidades_sem_repetir()
    {
        await using var db = await BancoComDivisao();
        var ing = Ingestao(db);

        await ing.IngestAsync([DoSync(1, EnumCaxias, "Caxias"), DoSync(2, EnumPetropolis, "Petrópolis"), DoSync(3, null, null)],
            await db.Units.FirstAsync(u => u.Id == 40));

        var leads = await db.Leads.AsNoTracking().ToListAsync();
        Assert.Equal(3, leads.Count);
        Assert.Equal((8044, 41), leads.Where(l => l.ExternalId == 1).Select(l => (l.TenantId, l.UnitId ?? 0)).Single());
        Assert.Equal((8043, 40), leads.Where(l => l.ExternalId == 2).Select(l => (l.TenantId, l.UnitId ?? 0)).Single());
        Assert.Equal((8045, 42), leads.Where(l => l.ExternalId == 3).Select(l => (l.TenantId, l.UnitId ?? 0)).Single());
    }

    [Fact]
    public async Task Sdr_troca_o_campo_e_o_cartao_muda_de_unidade_com_o_que_e_dele()
    {
        await using var db = await BancoComDivisao();
        var ing = Ingestao(db);
        var petropolis = await db.Units.FirstAsync(u => u.Id == 40);

        await ing.IngestAsync([DoSync(1, EnumCaxias, "Caxias")], petropolis);
        var lead = await db.Leads.SingleAsync();
        db.RecoveryAttempts.Add(new RecoveryAttempt { LeadId = lead.Id, TenantId = 8044, Outcome = "no_answer" });
        await db.SaveChangesAsync();

        await ing.IngestAsync([DoWebhook(1, "Petrópolis")], petropolis);
        // Evento seguinte sem o campo (mudança de etapa): fica onde está.
        await ing.IngestAsync([DoWebhook(1, null, "status")], petropolis);

        db.ChangeTracker.Clear();
        var depois = await db.Leads.SingleAsync();
        Assert.Equal(8043, depois.TenantId);
        Assert.Equal(40, depois.UnitId);
        Assert.Equal(8043, (await db.RecoveryAttempts.SingleAsync()).TenantId);
    }

    [Fact]
    public async Task Cartao_novo_com_update_e_status_no_mesmo_webhook_vira_uma_linha_so()
    {
        await using var db = await BancoComDivisao();
        var ing = Ingestao(db);

        await ing.IngestAsync([DoWebhook(7, "Caxias"), DoWebhook(7, null, "status")],
            await db.Units.FirstAsync(u => u.Id == 40));

        var leads = await db.Leads.AsNoTracking().Where(l => l.ExternalId == 7).ToListAsync();
        Assert.Single(leads);
        Assert.Equal(8044, leads[0].TenantId);
    }

    [Fact]
    public async Task Copia_antiga_em_outra_unidade_da_conta_e_marcada_deleted_para_nao_contar_duas_vezes()
    {
        await using var db = await BancoComDivisao();
        db.Leads.AddRange(
            new Lead { ExternalId = 9, TenantId = 8043, UnitId = 40, Name = "x", Phone = "", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Lead { ExternalId = 9, TenantId = 8044, UnitId = 41, Name = "x", Phone = "", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow.AddMinutes(-5) });
        await db.SaveChangesAsync();

        await Ingestao(db).IngestAsync([DoSync(9, EnumCaxias, "Caxias")], await db.Units.FirstAsync(u => u.Id == 40));

        db.ChangeTracker.Clear();
        var vivas = await db.Leads.Where(l => l.ExternalId == 9).ExcludeDeleted().ToListAsync();
        Assert.Single(vivas);
        Assert.Equal(8044, vivas[0].TenantId);
    }

    [Fact]
    public async Task Conta_de_uma_unidade_so_continua_igual()
    {
        await using var db = await BancoComDivisao();
        await Ingestao(db).IngestAsync([DoSync(5, EnumCaxias, "Caxias")], await db.Units.FirstAsync(u => u.Id == 24));

        var lead = await db.Leads.AsNoTracking().SingleAsync();
        Assert.Equal(8031, lead.TenantId);
    }

    [Fact]
    public async Task Configuracao_errada_para_a_entrada_em_vez_de_misturar()
    {
        var quebrada = ConfigValida.Replace("doutor-hernia-caxias", "doutor-hernia-caxias-que-nao-existe");
        await using var db = await BancoComDivisao(quebrada);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Ingestao(db).IngestAsync([DoSync(1, EnumCaxias, "Caxias")], db.Units.First(u => u.Id == 40)));
        Assert.Empty(await db.Leads.ToListAsync());
    }

    [Fact]
    public async Task Sync_agendado_le_a_conta_dividida_uma_vez_so_pela_dona()
    {
        await using var db = await BancoComDivisao();
        var agendado = new ScheduledSyncService(db, null!, null!, Divisao(db), NullLogger<ScheduledSyncService>.Instance);

        var ids = (await agendado.ListActiveKommoUnitsAsync(default)).Select(u => u.UnitId).OrderBy(i => i).ToList();

        // Serra (conta própria) + Petrópolis (dona). Caxias tem o token, mas não entra;
        // "sem unidade" nem token tem.
        Assert.Equal(new List<int> { 24, 40 }, ids);
    }

    // ─── Cruzamento com a franquia ───────────────────────────────────────

    private static KommoApiLead CartaoMarcado(long id, long? enumId) => new()
    {
        Id = id,
        CustomFieldsValues = enumId is null ? null :
        [
            new KommoApiCustomField
            {
                FieldId = Campo,
                Values = [new KommoApiCustomFieldValue { EnumId = enumId }],
            },
        ],
    };

    [Fact]
    public void Cruzamento_so_aceita_cartao_da_propria_unidade()
    {
        var d = DivisaoEmMemoria();
        var achados = new List<KommoApiLead> { CartaoMarcado(100, EnumPetropolis), CartaoMarcado(200, EnumCaxias), CartaoMarcado(300, null) };

        Assert.Equal(200, DivisaoDaContaService.EscolherCartaoDaUnidade(achados, d, Caxias)!.Id);
        Assert.Equal(100, DivisaoDaContaService.EscolherCartaoDaUnidade(achados, d, Petropolis)!.Id);
        // Só o cartão sem unidade definida: não vincula.
        Assert.Null(DivisaoDaContaService.EscolherCartaoDaUnidade([CartaoMarcado(300, null)], d, Caxias));
        // Conta de uma unidade só: o primeiro, como sempre foi.
        Assert.Equal(100, DivisaoDaContaService.EscolherCartaoDaUnidade(achados, null, Serra)!.Id);
    }
}
