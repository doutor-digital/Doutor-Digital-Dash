using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeadAnalytics.Tests;

/// <summary>
/// O conserto do rótulo de etapa no histórico — e as duas maneiras de ele estragar tudo.
///
/// POR QUE ESTE ARQUIVO EXISTE
/// ---------------------------
/// O histórico guardava o NOME da etapa no momento da mudança. Renomeamos etapas na rede
/// inteira e sobraram 203 rótulos distintos para ~12 etapas reais, com 22% dos registros
/// carregando o ID CRU ("143") no lugar do nome. Daí o backfill que relê o nome pelo mapa
/// da conta.
///
/// Só que ele pode piorar o número de dois jeitos, e os dois já aconteceram na simulação:
///
/// 1. GRAVAR O NOME DA KOMMO. O StageLabel é o rótulo canônico que os KPIs comparam com
///    LeadStages.*. Escrever "AGENDADO - SEM PAGAMENTO" no lugar de
///    "04_AGENDADO_SEM_PAGAMENTO" não dá erro nenhum — só zera o card. Na simulação isso
///    apareceu como uma unidade reescrevendo 3.695 de 3.695 linhas.
///
/// 2. CHUTAR O AMBÍGUO. 142/143 (Ganho/Perdido) existem em TODOS os funis da conta: o
///    mesmo id é PERDIDO no comercial e TRATAMENTO CANCELADO no de tratamento. Sem o
///    funil gravado na linha, escolher um dos dois é inventar dado — e inventar em 23.405
///    linhas.
/// </summary>
public class RotuloDeEtapaTests
{
    private const int Unidade = 15;
    private const long FunilComercial = 14388599;
    private const long FunilTratamento = 14388600;
    private const int Ganho = 142;   // "Ganho" nativo: nome diferente em cada funil
    private const int Perdido = 143;
    private const int Qualificacao = 70180000; // id próprio, só existe no comercial

    private sealed class UsuarioNulo : ICurrentUser
    {
        public int? UserId => null;
        public int? TenantId => 1;
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
            .UseInMemoryDatabase("rotulo-" + nome)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics
                .InMemoryEventId.TransactionIgnoredWarning))
            .Options, new UsuarioNulo());

    /// <summary>Mapa da conta: os dois funis, com 142/143 repetidos e nomes diferentes.</summary>
    private static void SemearMapa(AppDbContext db)
    {
        db.KommoStages.AddRange(
            new KommoStage { UnitId = Unidade, PipelineId = FunilComercial, PipelineName = "COMERCIAL", StatusId = Qualificacao, StatusName = "Qualificação" },
            new KommoStage { UnitId = Unidade, PipelineId = FunilComercial, PipelineName = "COMERCIAL", StatusId = Ganho, StatusName = "COMPARECEU" },
            new KommoStage { UnitId = Unidade, PipelineId = FunilComercial, PipelineName = "COMERCIAL", StatusId = Perdido, StatusName = "PERDIDO" },
            new KommoStage { UnitId = Unidade, PipelineId = FunilTratamento, PipelineName = "TRATAMENTO", StatusId = Ganho, StatusName = "ALTA" },
            new KommoStage { UnitId = Unidade, PipelineId = FunilTratamento, PipelineName = "TRATAMENTO", StatusId = Perdido, StatusName = "TRATAMENTO CANCELADO" });
        db.SaveChanges();
    }

    private static LeadStageHistory SemearLinha(
        AppDbContext db, int id, int etapa, long? funil, string rotuloAtual)
    {
        db.Leads.Add(new Lead
        {
            Id = id, ExternalId = id, TenantId = 1, UnitId = Unidade,
            Name = $"Paciente {id}", Phone = $"5599900000{id:00}",
        });
        var h = new LeadStageHistory
        {
            Id = id,
            LeadId = id,
            StageId = etapa,
            PipelineId = funil,
            StageLabel = rotuloAtual,
            ChangedAt = DateTime.UtcNow.AddDays(-1),
        };
        db.LeadStageHistories.Add(h);
        db.SaveChanges();
        return h;
    }

    private static KommoStageMapService Servico(AppDbContext db) =>
        new(db, null!, NullLogger<KommoStageMapService>.Instance);

    [Fact]
    public async Task Grava_o_rotulo_canonico_e_nao_o_nome_da_Kommo()
    {
        using var db = NovoBanco();
        SemearMapa(db);
        SemearLinha(db, 1, Qualificacao, FunilComercial, "70180000"); // id cru

        await Servico(db).CorrigirRotulosAsync(Unidade, 90, simular: false, CancellationToken.None);

        var salvo = db.LeadStageHistories.Single().StageLabel;
        Assert.Equal(LeadStages.Qualificacao, salvo);
        Assert.NotEqual("Qualificação", salvo); // o nome da Kommo não pode vazar pro KPI
    }

    [Fact]
    public async Task Nao_reescreve_quando_o_id_da_etapa_existe_em_dois_funis()
    {
        using var db = NovoBanco();
        SemearMapa(db);
        SemearLinha(db, 1, Perdido, null, "143"); // linha antiga: sem funil gravado

        var r = await Servico(db).CorrigirRotulosAsync(Unidade, 90, simular: false, CancellationToken.None);

        Assert.Equal("143", db.LeadStageHistories.Single().StageLabel);
        Assert.Equal(0, r.Corrigidos);
        Assert.Equal(1, r.Ambiguos);
    }

    [Fact]
    public async Task Com_o_funil_gravado_o_mesmo_id_resolve_para_cada_funil()
    {
        using var db = NovoBanco();
        SemearMapa(db);
        SemearLinha(db, 1, Perdido, FunilComercial, "143");
        SemearLinha(db, 2, Perdido, FunilTratamento, "143");

        await Servico(db).CorrigirRotulosAsync(Unidade, 90, simular: false, CancellationToken.None);

        Assert.Equal(LeadStages.Perdido,
            db.LeadStageHistories.Single(h => h.LeadId == 1).StageLabel);
        Assert.Equal(LeadStages.TratamentoCancelado,
            db.LeadStageHistories.Single(h => h.LeadId == 2).StageLabel);
    }

    [Fact]
    public async Task Simulacao_conta_mas_nao_grava()
    {
        using var db = NovoBanco();
        SemearMapa(db);
        SemearLinha(db, 1, Qualificacao, FunilComercial, "70180000");

        var r = await Servico(db).CorrigirRotulosAsync(Unidade, 90, simular: true, CancellationToken.None);

        Assert.Equal(1, r.Corrigidos);
        Assert.Equal("70180000", db.LeadStageHistories.Single().StageLabel);
    }
}
