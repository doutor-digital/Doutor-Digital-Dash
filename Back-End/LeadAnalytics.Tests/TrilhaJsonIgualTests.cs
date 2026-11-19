using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LeadAnalytics.Tests;

/// 19/11: o webhook e a sincronização gravavam o MESMO JSON de campos/etiquetas com texto
/// diferente e ficavam se alternando — ~250 mil linhas falsas por dia em entity_change_logs
/// (96% das mudanças de CustomFieldsJson), 20 GB de tabela e dumps de 4,5 GB.
/// Mesmo conteúdo em outra grafia não pode virar trilha nem UPDATE; mudança de verdade continua.
public class TrilhaJsonIgualTests
{
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

    private static AppDbContext NovoBanco(string nome) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(nome)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics
                .InMemoryEventId.TransactionIgnoredWarning))
            .Options, new UsuarioNulo());

    private const string Campos =
        """[{"field_id":2446619,"field_name":"◉ Semáforo","value":"verde"},{"field_id":10,"field_name":"Origem","value":1.0}]""";

    // Mesmo conteúdo: outra ordem de chaves, espaços, unicode escapado e 1.0 → 1.
    private const string CamposOutraGrafia =
        """[ { "value" : "verde", "field_name" : "◉ Semáforo", "field_id" : 2446619 }, { "field_name":"Origem", "value":1, "field_id":10 } ]""";

    private const string CamposMudados =
        """[{"field_id":2446619,"field_name":"◉ Semáforo","value":"vermelho"},{"field_id":10,"field_name":"Origem","value":1.0}]""";

    private static async Task<AppDbContext> BancoComLead(string nome)
    {
        var db = NovoBanco(nome);
        db.Leads.Add(new Lead
        {
            Id = 1, ExternalId = 1, TenantId = 1, UnitId = 1, Name = "Paciente", Phone = "",
            CustomFieldsJson = Campos, TagsJson = """["a","b"]""",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task MesmoJsonEmOutraGrafia_NaoViraTrilha_NemMudaOTextoGravado()
    {
        await using var db = await BancoComLead(nameof(MesmoJsonEmOutraGrafia_NaoViraTrilha_NemMudaOTextoGravado));
        var antes = await db.EntityChangeLogs.CountAsync();

        var lead = await db.Leads.SingleAsync();
        lead.CustomFieldsJson = CamposOutraGrafia;
        lead.TagsJson = """[ "a", "b" ]""";
        await db.SaveChangesAsync();

        Assert.Equal(antes, await db.EntityChangeLogs.CountAsync());
        Assert.Equal(EntityState.Unchanged, db.Entry(lead).State);

        // Banco novo (sem cache do contexto): o texto gravado continua o original.
        await using var outro = NovoBanco(nameof(MesmoJsonEmOutraGrafia_NaoViraTrilha_NemMudaOTextoGravado));
        var gravado = await outro.Leads.AsNoTracking().SingleAsync();
        Assert.Equal(Campos, gravado.CustomFieldsJson);
        Assert.Equal("""["a","b"]""", gravado.TagsJson);
    }

    [Fact]
    public async Task MudancaDeVerdade_ContinuaNaTrilha()
    {
        await using var db = await BancoComLead(nameof(MudancaDeVerdade_ContinuaNaTrilha));
        var antes = await db.EntityChangeLogs.CountAsync();

        var lead = await db.Leads.SingleAsync();
        lead.CustomFieldsJson = CamposMudados;
        await db.SaveChangesAsync();

        var logs = await db.EntityChangeLogs.Skip(antes).ToListAsync();
        var log = Assert.Single(logs);
        Assert.Equal("Modified", log.Action);
        Assert.Contains("CustomFieldsJson", log.ChangesJson);
    }

    [Fact]
    public async Task JsonIgualJuntoComOutraMudanca_SoAOutraEntraNaTrilha()
    {
        await using var db = await BancoComLead(nameof(JsonIgualJuntoComOutraMudanca_SoAOutraEntraNaTrilha));
        var antes = await db.EntityChangeLogs.CountAsync();

        var lead = await db.Leads.SingleAsync();
        lead.CustomFieldsJson = CamposOutraGrafia;
        lead.Name = "Paciente Novo";
        await db.SaveChangesAsync();

        var log = Assert.Single(await db.EntityChangeLogs.Skip(antes).ToListAsync());
        Assert.Contains("\"Name\"", log.ChangesJson);
        Assert.DoesNotContain("CustomFieldsJson", log.ChangesJson);
    }

    [Fact]
    public async Task TrocarArrayDeOrdem_EhMudanca()
    {
        await using var db = await BancoComLead(nameof(TrocarArrayDeOrdem_EhMudanca));
        var antes = await db.EntityChangeLogs.CountAsync();

        var lead = await db.Leads.SingleAsync();
        lead.TagsJson = """["b","a"]""";
        await db.SaveChangesAsync();

        Assert.Single(await db.EntityChangeLogs.Skip(antes).ToListAsync());
    }

    [Theory]
    [InlineData("""{"a":1,"b":[1,2]}""", """{ "b":[1,2], "a":1.0 }""", true)]
    [InlineData("""{"a":1}""", """{"a":1,"b":null}""", false)]
    [InlineData("não é json", "não é json ", false)]
    [InlineData(null, """[]""", false)]
    [InlineData("""[]""", "", false)]
    public void MesmoJson(string? a, string? b, bool esperado) =>
        Assert.Equal(esperado, AppDbContext.MesmoJson(a, b));
}
