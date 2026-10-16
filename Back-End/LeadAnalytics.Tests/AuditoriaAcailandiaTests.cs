using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service;
using LeadAnalytics.Api.Service.Spine;
using LeadAnalytics.Api.Service.Stages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// Os erros que a auditoria do dashboard da Açailândia (01–16/10/2026) achou. Cada bloco
/// trava um defeito medido em produção, com os ids reais da conta.
/// </summary>
public class AuditoriaAcailandiaTests
{
    private const long Comercial = 13795975;
    private const long Tratamento = 14304315;
    private const int EmEspera = 111237819;
    private const int EmTratamento = 110468755;
    private const int Compareceu = 110468747;

    private const int Tenant = 8014;
    private const int UnidadeId = 14;

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
            .UseInMemoryDatabase("aca-" + nome + "-" + Guid.NewGuid())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics
                .InMemoryEventId.TransactionIgnoredWarning))
            .Options, new UsuarioNulo());

    private static Unit Acailandia(string? mapaManual = null) => new()
    {
        Id = UnidadeId, ClinicId = Tenant, Name = "Doutor Hérnia Açailândia", Slug = "doutor-hernia-acailandia",
        KommoSubdomain = "doutorherniaacailandia", KommoAccessToken = "tk", CreatedAt = DateTime.UtcNow,
        KommoStageMapJson = mapaManual,
    };

    /// <summary>Os dois funis da conta, como a Kommo devolve (lido em 16/10/2026).</summary>
    private static readonly EtapaDaConta[] EtapasDaConta =
    [
        new(Comercial, 106446875, "Incoming leads"),
        new(Comercial, 106809907, "EM QUALIFICAÇÃO"),
        new(Comercial, EmEspera, "EM ESPERA"),
        new(Comercial, 106809915, "AGENDADO"),
        new(Comercial, 110846015, "NÃO COMPARECEU"),
        new(Comercial, Compareceu, "COMPARECEU"),
        new(Comercial, 106810351, "EM NEGOCIAÇÃO"),
        new(Comercial, 142, "GANHO / CONCLUIDO"),
        new(Comercial, 143, "PERDIDO"),
        new(Tratamento, 110468751, "Incoming leads"),
        new(Tratamento, EmTratamento, "EM TRATAMENTO"),
        new(Tratamento, 142, "ALTA"),
        new(Tratamento, 143, "TRATAMENTO CANCELADO"),
    ];

    private static KommoIngestionService Ingestao(AppDbContext db) =>
        new(db,
            new KommoStageProcessor(db, NullLogger<KommoStageProcessor>.Instance),
            new KpiConfigService(db, null!, null!, NullLogger<KpiConfigService>.Instance),
            new DivisaoDaContaService(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<DivisaoDaContaService>.Instance),
            NullLogger<KommoIngestionService>.Instance);

    private static LeadEvent Evento(int id, long funil, int etapa, string acao = "status") => new()
    {
        SourceSystem = "Kommo", EntityType = "lead", Action = acao, ExternalId = id.ToString(),
        Name = "Paciente " + id, Stage = etapa.ToString(), PipelineId = funil.ToString(),
        KommoModifiedAtUtc = new DateTime(2026, 10, 10, 15, 0, 0, DateTimeKind.Utc),
    };

    // ─── 1. PERDIDO gravado como TRATAMENTO_CANCELADO ──────────────────────────

    [Fact]
    public void Mapa_traduz_142_e_143_pelo_funil()
    {
        var mapa = MapaCanonicoDeEtapas.Montar(EtapasDaConta);

        Assert.Equal(CanonicalStages.Perdido, mapa[$"{Comercial}:143"]);
        Assert.Equal(CanonicalStages.TratamentoCancelado, mapa[$"{Tratamento}:143"]);
        Assert.Equal(CanonicalStages.TratamentoFechado, mapa[$"{Comercial}:142"]);
        Assert.Equal(CanonicalStages.Alta, mapa[$"{Tratamento}:142"]);

        // Número ambíguo não ganha chave sem funil: escolher um dos dois seria chute.
        Assert.False(mapa.ContainsKey("143"));
        Assert.False(mapa.ContainsKey("142"));
        // Número único continua com a chave simples (compatibilidade).
        Assert.Equal(CanonicalStages.Negociacao, mapa["106810351"]);
    }

    [Fact]
    public void Funil_com_nome_que_nao_se_traduz_tambem_torna_o_numero_ambiguo()
    {
        // Terceiro funil com o nome padrão da Kommo: "Venda perdida" não casa com PERDIDO.
        // Antes ele era ignorado, o 143 parecia único e ganhava a chave simples — e os leads
        // desse funil caíam como PERDIDO.
        var mapa = MapaCanonicoDeEtapas.Montar(
        [
            new(Comercial, 143, "PERDIDO"),
            new(99999999, 143, "Venda perdida"),
        ]);

        Assert.Equal(CanonicalStages.Perdido, mapa[$"{Comercial}:143"]);
        Assert.False(mapa.ContainsKey("143"));
        Assert.Contains("143", MapaCanonicoDeEtapas.NumerosAmbiguos(mapa));
    }

    [Fact]
    public void Em_espera_vira_etapa_conhecida()
    {
        Assert.Equal(CanonicalStages.EmEspera, CanonicalStages.Resolve("EM ESPERA"));
        Assert.Equal(LeadStages.EmEspera, CanonicalStages.ToLeadStage(CanonicalStages.Resolve("EM ESPERA")));
        Assert.Equal(CanonicalStages.EmEspera, MapaCanonicoDeEtapas.Montar(EtapasDaConta)[EmEspera.ToString()]);
        // E não sequestra as outras etapas do funil.
        Assert.Equal(CanonicalStages.Negociacao, CanonicalStages.Resolve("EM NEGOCIAÇÃO"));
        Assert.Equal(CanonicalStages.Qualificacao, CanonicalStages.Resolve("EM QUALIFICAÇÃO"));
    }

    [Theory]
    [InlineData("143", 143, Tratamento, 143, Comercial, true)]   // mesmo número, outro funil
    [InlineData("143", 143, Comercial, 143, Comercial, false)]   // nada mudou
    [InlineData("143", 143, Comercial, 143, null, false)]        // lead antigo: conserta o rótulo, sem histórico
    [InlineData("142", 142, Comercial, 143, Comercial, true)]    // número mudou
    [InlineData("", null, Comercial, 143, Comercial, false)]     // evento sem etapa
    public void Mudanca_de_etapa_compara_funil_e_etapa(
        string crua, int? etapaEvento, long? funilEvento, int? etapaAtual, long? funilAtual, bool esperado)
    {
        Assert.Equal(esperado, KommoIngestionService.MudouDeEtapa(crua, etapaEvento, funilEvento, etapaAtual, funilAtual));
    }

    [Fact]
    public async Task Webhook_grava_o_perdido_do_comercial_como_PERDIDO()
    {
        await using var db = NovoBanco();
        var unidade = Acailandia();
        db.Units.Add(unidade);
        await db.SaveChangesAsync();
        var mapa = MapaCanonicoDeEtapas.Montar(EtapasDaConta);

        await Ingestao(db).IngestAsync([Evento(1, Comercial, 143), Evento(2, Tratamento, 143)],
            unidade, stageMapOverride: mapa, recordStageHistory: true);

        var perdido = await db.Leads.SingleAsync(l => l.ExternalId == 1);
        Assert.Equal(LeadStages.Perdido, perdido.CurrentStage);
        Assert.Equal(Comercial, perdido.CurrentPipelineId);
        var cancelado = await db.Leads.SingleAsync(l => l.ExternalId == 2);
        Assert.Equal(LeadStages.TratamentoCancelado, cancelado.CurrentStage);
        Assert.Equal(Tratamento, cancelado.CurrentPipelineId);
    }

    [Fact]
    public async Task Mapa_manual_so_com_numero_nao_vence_a_chave_com_funil()
    {
        await using var db = NovoBanco();
        var unidade = Acailandia("""{"143":"TRATAMENTO_CANCELADO"}""");
        db.Units.Add(unidade);
        await db.SaveChangesAsync();

        await Ingestao(db).IngestAsync([Evento(3, Comercial, 143)], unidade,
            stageMapOverride: MapaCanonicoDeEtapas.Montar(EtapasDaConta), recordStageHistory: true);

        Assert.Equal(LeadStages.Perdido, (await db.Leads.SingleAsync()).CurrentStage);
    }

    [Fact]
    public async Task Mapa_manual_de_numero_unico_continua_vencendo()
    {
        // O admin mapeou o AGENDADO como "com pagamento". O nome da etapa na Kommo daria
        // "sem pagamento"; a config do admin vence, como sempre venceu.
        await using var db = NovoBanco();
        var unidade = Acailandia("""{"106809915":"05_AGENDADO_COM_PAGAMENTO"}""");
        db.Units.Add(unidade);
        await db.SaveChangesAsync();

        await Ingestao(db).IngestAsync([Evento(6, Comercial, 106809915)], unidade,
            stageMapOverride: MapaCanonicoDeEtapas.Montar(EtapasDaConta), recordStageHistory: true);

        Assert.Equal(LeadStages.AgendadoComPagamento, (await db.Leads.SingleAsync()).CurrentStage);
    }

    [Fact]
    public async Task Lead_gravado_errado_e_consertado_no_proximo_evento_sem_historico_inventado()
    {
        await using var db = NovoBanco();
        var unidade = Acailandia();
        db.Units.Add(unidade);
        db.Leads.Add(new Lead
        {
            ExternalId = 4, TenantId = Tenant, UnitId = UnidadeId, Name = "Paciente 4", Phone = "",
            CurrentStage = LeadStages.TratamentoCancelado, CurrentStageId = 143, CurrentPipelineId = null,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        // O sync (recordStageHistory = false) e o webhook passam pelo mesmo caminho.
        await Ingestao(db).IngestAsync([Evento(4, Comercial, 143, "add")], unidade,
            stageMapOverride: MapaCanonicoDeEtapas.Montar(EtapasDaConta));

        var lead = await db.Leads.Include(l => l.StageHistory).SingleAsync();
        Assert.Equal(LeadStages.Perdido, lead.CurrentStage);
        Assert.Equal(Comercial, lead.CurrentPipelineId);
        Assert.Empty(lead.StageHistory);
    }

    [Fact]
    public async Task Sair_do_143_do_tratamento_para_o_143_do_comercial_registra_a_passagem()
    {
        await using var db = NovoBanco();
        var unidade = Acailandia();
        db.Units.Add(unidade);
        await db.SaveChangesAsync();
        var mapa = MapaCanonicoDeEtapas.Montar(EtapasDaConta);
        var ing = Ingestao(db);

        await ing.IngestAsync([Evento(5, Tratamento, 143)], unidade, stageMapOverride: mapa, recordStageHistory: true);
        await ing.IngestAsync([Evento(5, Comercial, 143)], unidade, stageMapOverride: mapa, recordStageHistory: true);

        var lead = await db.Leads.Include(l => l.StageHistory).SingleAsync();
        Assert.Equal(LeadStages.Perdido, lead.CurrentStage);
        Assert.Equal(2, lead.StageHistory.Count);
        var ultima = lead.StageHistory.OrderBy(h => h.Id).Last();
        Assert.Equal(LeadStages.Perdido, ultima.StageLabel);
        Assert.Equal(Comercial, ultima.PipelineId);
    }

    // ─── 1b. Conserto do dado já gravado (endpoint corrigir-etapa-atual) ────────

    private static async Task<AppDbContext> BancoParaConserto([System.Runtime.CompilerServices.CallerMemberName] string nome = "")
    {
        var db = NovoBanco(nome);
        db.Units.Add(Acailandia());
        foreach (var e in EtapasDaConta)
            db.KommoStages.Add(new KommoStage
            {
                UnitId = UnidadeId, PipelineId = e.Funil, StatusId = e.Etapa, StatusName = e.Nome!,
                UpdatedAt = DateTime.UtcNow,
            });

        Lead L(int id, string etapa, int etapaId, long? funil) => new()
        {
            Id = id, ExternalId = 1000 + id, TenantId = Tenant, UnitId = UnidadeId, Name = $"P{id}", Phone = "",
            CurrentStage = etapa, CurrentStageId = etapaId, CurrentPipelineId = funil,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        db.Leads.AddRange(
            L(1, LeadStages.TratamentoCancelado, 143, null),       // funil vem do histórico → PERDIDO
            L(2, LeadStages.TratamentoCancelado, 143, Tratamento), // está certo → não mexe
            L(3, LeadStages.TratamentoCancelado, 143, null),       // sem funil em lugar nenhum → semFunil
            L(4, EmEspera.ToString(), EmEspera, null),             // número cru → EM_ESPERA
            L(5, LeadStages.Alta, 142, Comercial),                 // GANHO do comercial → FECHOU
            L(6, LeadStages.FechouTratamento, 106810351, null));   // número único: ajuste manual, não mexe
        db.LeadStageHistories.Add(new LeadStageHistory
        {
            LeadId = 1, StageId = 143, PipelineId = Comercial, StageLabel = LeadStages.TratamentoCancelado,
            ChangedAt = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();
        return db;
    }

    private static KommoStageMapService Mapa(AppDbContext db) =>
        new(db, null!, NullLogger<KommoStageMapService>.Instance);

    [Fact]
    public async Task Conserto_em_simulacao_conta_e_nao_grava()
    {
        await using var db = await BancoParaConserto();

        var r = await Mapa(db).CorrigirEtapaAtualAsync(UnidadeId, simular: true, default);

        Assert.Equal(6, r.Examinados);
        Assert.Equal(3, r.Corrigidos);
        Assert.Equal(1, r.SemFunil);
        Assert.Equal(3, r.FunisPreenchidos); // 1 (histórico), 4 e 6 (número de um funil só)
        Assert.Contains(r.Mudancas, m => m.De == LeadStages.TratamentoCancelado && m.Para == LeadStages.Perdido && m.Quantidade == 1);
        Assert.Contains(r.Mudancas, m => m.De == LeadStages.Alta && m.Para == LeadStages.FechouTratamento);
        db.ChangeTracker.Clear();
        Assert.Equal(LeadStages.TratamentoCancelado, (await db.Leads.FindAsync(1))!.CurrentStage);
    }

    [Fact]
    public async Task Conserto_gravando_corrige_so_o_que_tem_funil_conhecido()
    {
        await using var db = await BancoParaConserto();

        await Mapa(db).CorrigirEtapaAtualAsync(UnidadeId, simular: false, default);
        db.ChangeTracker.Clear();

        var l1 = (await db.Leads.FindAsync(1))!;
        Assert.Equal(LeadStages.Perdido, l1.CurrentStage);
        Assert.Equal(Comercial, l1.CurrentPipelineId);
        Assert.Equal(LeadStages.TratamentoCancelado, (await db.Leads.FindAsync(2))!.CurrentStage);
        Assert.Equal(LeadStages.TratamentoCancelado, (await db.Leads.FindAsync(3))!.CurrentStage);
        Assert.Equal(LeadStages.EmEspera, (await db.Leads.FindAsync(4))!.CurrentStage);
        Assert.Equal(LeadStages.FechouTratamento, (await db.Leads.FindAsync(5))!.CurrentStage);
        var l6 = (await db.Leads.FindAsync(6))!;
        Assert.Equal(LeadStages.FechouTratamento, l6.CurrentStage); // fora do defeito: não mexe
        Assert.Equal(Comercial, l6.CurrentPipelineId);              // mas ganha o funil
        Assert.Equal(Comercial, (await db.Leads.FindAsync(4))!.CurrentPipelineId);

        // Rodar de novo não acha mais nada (idempotente).
        var segunda = await Mapa(db).CorrigirEtapaAtualAsync(UnidadeId, simular: true, default);
        Assert.Equal(0, segunda.Corrigidos);
        Assert.Equal(0, segunda.FunisPreenchidos);
    }

    [Fact]
    public async Task Conserto_sem_mapa_de_etapas_recusa_com_mensagem()
    {
        await using var db = NovoBanco();
        db.Units.Add(Acailandia());
        await db.SaveChangesAsync();

        var r = await Mapa(db).CorrigirEtapaAtualAsync(UnidadeId, simular: true, default);

        Assert.NotNull(r.Erro);
        Assert.Contains("stage-map/sync", r.Erro);
    }

    // ─── 3. Semáforo: 142 preso ao funil ────────────────────────────────────────

    private const long CampoSemaforo = 2446619;

    private static string Semaforo(string cor) =>
        $$"""[{"field_id":{{CampoSemaforo}},"field_name":"◉ Semáforo","value":"{{cor}}"}]""";

    private static async Task<AppDbContext> BancoSemaforo([System.Runtime.CompilerServices.CallerMemberName] string nome = "")
    {
        var db = NovoBanco(nome);
        var quando = new DateTime(2026, 10, 10, 15, 0, 0, DateTimeKind.Utc);
        Lead L(int id, string cor) => new()
        {
            Id = id, ExternalId = id, TenantId = Tenant, UnitId = UnidadeId, Name = $"P{id}", Phone = "",
            CreatedAt = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc), UpdatedAt = quando,
            CustomFieldsJson = Semaforo(cor), Status = "active",
        };
        db.Leads.AddRange(L(1, "VERDE"), L(2, "VERMELHO"), L(3, "AMARELO"), L(4, "LARANJA"));
        db.LeadStageHistories.AddRange(
            new LeadStageHistory { LeadId = 1, StageId = 142, PipelineId = Tratamento, StageLabel = LeadStages.Alta, ChangedAt = quando },
            new LeadStageHistory { LeadId = 2, StageId = 142, PipelineId = Comercial, StageLabel = LeadStages.FechouTratamento, ChangedAt = quando },
            new LeadStageHistory { LeadId = 3, StageId = 142, PipelineId = null, StageLabel = LeadStages.Alta, ChangedAt = quando },
            new LeadStageHistory { LeadId = 4, StageId = Compareceu, PipelineId = Comercial, StageLabel = LeadStages.Compareceu, ChangedAt = quando });
        await db.SaveChangesAsync();
        return db;
    }

    private static readonly DateTime De = new(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Ate = new(2026, 10, 17, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Semaforo_com_142_preso_ao_tratamento_conta_so_a_alta()
    {
        await using var db = await BancoSemaforo();
        var cfg = JsonSerializer.Deserialize<JsonElement>($$$"""
            {"fieldId":{{{CampoSemaforo}}},"stageIds":[{{{Compareceu}}},142],"porEntradaNaEtapa":true,
             "funilDaEtapa":{"142":{{{Tratamento}}}}}
            """);

        var r = await new KpiConfigService(db, null!, null!, NullLogger<KpiConfigService>.Instance)
            .ComputeBreakdownAsync(Tenant, UnidadeId, cfg, De, Ate);

        Assert.Equal(new[] { "LARANJA", "VERDE" }, r.Select(x => x.Label).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Semaforo_sem_funilDaEtapa_continua_como_antes()
    {
        await using var db = await BancoSemaforo();
        var cfg = JsonSerializer.Deserialize<JsonElement>($$"""
            {"fieldId":{{CampoSemaforo}},"stageIds":[{{Compareceu}},142],"porEntradaNaEtapa":true}
            """);

        var r = await new KpiConfigService(db, null!, null!, NullLogger<KpiConfigService>.Instance)
            .ComputeBreakdownAsync(Tenant, UnidadeId, cfg, De, Ate);

        Assert.Equal(4, r.Sum(x => x.Value));
    }

    [Fact]
    public void Filtro_de_etapas_codifica_o_par_funil_etapa()
    {
        var f = new FiltroDeEtapas([Compareceu, 142], new Dictionary<int, long> { [142] = Tratamento });
        Assert.Equal(new[] { Compareceu }, f.Livres);
        Assert.Equal(new[] { Tratamento * FiltroDeEtapas.FatorFunil + 142 }, f.Pares);

        var lido = FiltroDeEtapas.LerFunilDaEtapa(JsonSerializer.Deserialize<JsonElement>(
            """{"funilDaEtapa":{"142":14304315,"143":"13795975","x":1}}"""));
        Assert.Equal(Tratamento, lido[142]);
        Assert.Equal(Comercial, lido[143]);
        Assert.Equal(2, lido.Count);
    }

    // ─── 5. Janela com fim exclusivo, igual ao card de Leads ────────────────────

    [Fact]
    public async Task Lead_criado_exatamente_no_fim_da_janela_conta_so_no_dia_seguinte()
    {
        await using var db = NovoBanco();
        // Dia comercial: vira às 19h de Brasília = 22:00 UTC.
        var virada = new DateTime(2026, 10, 15, 22, 0, 0, DateTimeKind.Utc);
        db.Leads.AddRange(
            new Lead { Id = 1, ExternalId = 1, TenantId = Tenant, UnitId = UnidadeId, Name = "a", Phone = "", CreatedAt = virada, UpdatedAt = virada },
            new Lead { Id = 2, ExternalId = 2, TenantId = Tenant, UnitId = UnidadeId, Name = "b", Phone = "", CreatedAt = virada.AddTicks(-1), UpdatedAt = virada });
        await db.SaveChangesAsync();
        var svc = new KpiConfigService(db, null!, null!, NullLogger<KpiConfigService>.Instance);
        var vazio = JsonSerializer.Deserialize<JsonElement>("{}");

        var dia15 = await svc.MedirAsync(Tenant, UnidadeId, KpiSourceTypes.CreatedInPeriod, vazio, virada.AddDays(-1), virada);
        var dia16 = await svc.MedirAsync(Tenant, UnidadeId, KpiSourceTypes.CreatedInPeriod, vazio, virada, virada.AddDays(1));

        Assert.Equal(1d, dia15.Value); // só o das 18:59:59,9999999
        Assert.Equal(1d, dia16.Value); // o das 19:00:00 em ponto — uma vez só
    }

    [Fact]
    public async Task Entrada_na_etapa_exatamente_no_fim_da_janela_fica_para_a_janela_seguinte()
    {
        await using var db = NovoBanco();
        var virada = new DateTime(2026, 10, 15, 22, 0, 0, DateTimeKind.Utc);
        db.Leads.Add(new Lead { Id = 1, ExternalId = 1, TenantId = Tenant, UnitId = UnidadeId, Name = "a", Phone = "", CreatedAt = virada, UpdatedAt = virada });
        db.LeadStageHistories.Add(new LeadStageHistory { LeadId = 1, StageId = 106809915, StageLabel = LeadStages.AgendadoSemPagamento, ChangedAt = virada });
        await db.SaveChangesAsync();
        var svc = new KpiConfigService(db, null!, null!, NullLogger<KpiConfigService>.Instance);
        var cfg = JsonSerializer.Deserialize<JsonElement>("""{"stageIds":[106809915]}""");

        var antes = await svc.MedirAsync(Tenant, UnidadeId, KpiSourceTypes.KommoStage, cfg, virada.AddDays(-1), virada);
        var depois = await svc.MedirAsync(Tenant, UnidadeId, KpiSourceTypes.KommoStage, cfg, virada, virada.AddDays(1));

        Assert.Equal(0d, antes.Value);
        Assert.Equal(1d, depois.Value);
    }

    // ─── 2. Comparecimento divide pelos horários com desfecho ───────────────────

    private static List<SpineSchedule> AgendaAcailandia()
    {
        var rows = new List<SpineSchedule>();
        var id = 3_700_000L;
        void Add(int status, int n) { for (var i = 0; i < n; i++) rows.Add(new SpineSchedule { IdSchedule = id++, IdStatus = status, ClientName = $"P{id}" }); }
        Add(SpineApiClient.ScheduleStatus.Atendido, 11);
        Add(SpineApiClient.ScheduleStatus.NaoCompareceu, 4);
        Add(SpineApiClient.ScheduleStatus.Desmarcado, 11);
        Add(SpineApiClient.ScheduleStatus.Remarcado, 1);
        Add(SpineApiClient.ScheduleStatus.Confirmado, 4);
        return rows;
    }

    [Fact]
    public void Agendados_com_desfecho_tira_os_horarios_que_ainda_vem()
    {
        var dto = SpineAvaliacoesService.Montar(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 16), AgendaAcailandia());

        Assert.Equal(31, dto.Total);
        Assert.Equal(27, dto.Resolvidas);          // o que o painel publica como agendados_com_desfecho
        Assert.Equal(40.7, dto.TaxaComparecimento); // 11 ÷ 27, não 11 ÷ 31 = 35%
        Assert.Contains(KpiSourceTypes.AgendadosComDesfecho, KpiSourceTypes.MetricasFranquia);
    }

    // ─── 7. Categoria 7 é REAVALIAÇÃO ────────────────────────────────────────────

    [Fact]
    public async Task Reavaliacao_nao_conta_como_avaliacao_na_agenda_do_dia()
    {
        await using var db = NovoBanco();
        var dia = new DateOnly(2026, 10, 16);
        db.SpineScheduleSnapshots.AddRange(
            new SpineScheduleSnapshot { UnitId = UnidadeId, IdSchedule = 1, DiaLocal = dia, IdCategory = SpineApiClient.ScheduleCategory.Avaliacao, Categoria = "AVALIACAO", IdStatus = SpineApiClient.ScheduleStatus.Atendido },
            new SpineScheduleSnapshot { UnitId = UnidadeId, IdSchedule = 2, DiaLocal = dia, IdCategory = SpineApiClient.ScheduleCategory.Reavaliacao, Categoria = "REAVALIACAO", IdStatus = SpineApiClient.ScheduleStatus.Atendido });
        await db.SaveChangesAsync();

        var r = await new AgendaDoDiaService(db).GetAsync(Tenant, UnidadeId, dia);

        Assert.Equal(7, SpineApiClient.ScheduleCategory.Reavaliacao);
        Assert.Equal(2, r.TotalNaClinica);
        Assert.Equal(1, r.AvaliacoesFranquia);
    }

    // ─── 8. Registros de TESTE na agenda da franquia ────────────────────────────

    [Fact]
    public void Filtro_de_agendamentos_de_teste_vem_desligado()
    {
        var rows = AgendaAcailandia();
        Assert.Same(rows, SpineAvaliacoesService.SemIgnorados(rows, []));
        Assert.Empty(new LeadAnalytics.Api.Options.SpineOptions().AgendamentosIgnorados);
    }

    [Fact]
    public void Filtro_de_agendamentos_de_teste_tira_so_os_ids_apontados()
    {
        var rows = AgendaAcailandia();
        rows.Add(new SpineSchedule { IdSchedule = 3738045, IdStatus = SpineApiClient.ScheduleStatus.Atendido, ClientName = "TESTE" });

        var semTeste = SpineAvaliacoesService.SemIgnorados(rows, [3738045L]);
        var dto = SpineAvaliacoesService.Montar(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 16), semTeste);

        Assert.Equal(rows.Count - 1, semTeste.Count);
        Assert.Equal(11, dto.Realizadas);
    }
}
