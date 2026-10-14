using System.Globalization;
using System.Text;
using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Uma conta da Kommo que atende MAIS DE UMA unidade.
///
/// POR QUE ISTO EXISTE
/// -------------------
/// Até aqui "unidade = conta da Kommo": o webhook e o sync gravavam todo cartão da conta
/// no tenant da unidade dona da URL. Petrópolis (<c>doutorhernialvp</c>) quebrou a regra:
/// é UM Kommo para DUAS clínicas, Petrópolis e Caxias, e cada cartão diz de qual é no
/// campo <c>⌂ Unidade de atendimento</c>. Regra do João (14/10/2026): são duas unidades,
/// tudo separa as duas. Somar uma na outra seria um número plausível e errado.
///
/// COMO FUNCIONA
/// -------------
/// A divisão é uma linha em <c>AppConfigurations</c> com a chave
/// <c>kommo:divisao:{subdominio}</c>. Sem essa linha, nada muda para as outras contas.
/// Com ela, todo evento de lead que entra (webhook OU sync) passa por
/// <see cref="DivisaoDaContaService.RepartirAsync"/>, que decide a unidade pelo campo do cartão:
/// <list type="bullet">
/// <item>campo com uma opção conhecida → a unidade daquela opção;</item>
/// <item>campo vazio (ou opção que a configuração não conhece) → a unidade
/// "sem unidade definida". Ela existe para que esses cartões fiquem À VISTA, com nome
/// próprio, e não somados a nenhuma das duas sem ninguém perceber;</item>
/// <item>evento sem o campo (o webhook manda parte dos campos) → fica onde o cartão já
/// está; cartão novo vai para "sem unidade definida" até o campo chegar.</item>
/// </list>
/// Quando a SDR troca o campo, o cartão MUDA de unidade: a linha do lead (e as linhas
/// filhas que carregam o tenant) passam para o tenant da outra unidade.
///
/// FALHA FECHADA
/// -------------
/// Configuração inválida (slug que não existe, unidade repetida) faz a ingestão recusar,
/// em vez de cair no comportamento antigo — que gravaria a conta inteira numa unidade só.
/// Dado parado aparece no monitor de webhooks; dado misturado não aparece em lugar nenhum.
/// </summary>
public sealed class DivisaoDaConta
{
    public required string Subdominio { get; init; }
    public required long CampoId { get; init; }
    public string NomeDoCampo { get; init; } = "Unidade de atendimento";

    /// <summary>Unidade que recebe o webhook e roda o sync da conta (as outras não).</summary>
    public required Unit Dona { get; init; }

    /// <summary>Onde ficam os cartões com o campo vazio. Não é clínica: é um aviso com nome.</summary>
    public required Unit SemUnidade { get; init; }

    public required IReadOnlyList<Opcao> Opcoes { get; init; }

    public sealed record Opcao(Unit Unidade, long? EnumId, string Valor);

    /// <summary>Todas as unidades da conta (as das opções + a "sem unidade"), sem repetir.</summary>
    public IReadOnlyList<Unit> Unidades =>
        Opcoes.Select(o => o.Unidade).Append(SemUnidade)
            .GroupBy(u => u.Id).Select(g => g.First()).ToList();

    public IReadOnlyCollection<int> Tenants => Unidades.Select(u => u.ClinicId).ToHashSet();

    public bool Contem(int unitId) => Unidades.Any(u => u.Id == unitId);

    public Unit? UnidadeDoTenant(int tenantId) => Unidades.FirstOrDefault(u => u.ClinicId == tenantId);

    /// <summary>
    /// Opção do campo → unidade. Casa pelo id da opção (o sync traz) e, sem ele, pelo
    /// texto sem acento e sem caixa (o webhook só traz o texto).
    /// </summary>
    public Unit? UnidadeDoValor(long? enumId, string? valor)
    {
        if (enumId is > 0)
        {
            var porId = Opcoes.FirstOrDefault(o => o.EnumId == enumId);
            if (porId is not null) return porId.Unidade;
        }
        var alvo = DivisaoDaContaService.Normalizar(valor);
        if (alvo.Length == 0) return null;
        return Opcoes.FirstOrDefault(o => DivisaoDaContaService.Normalizar(o.Valor) == alvo)?.Unidade;
    }
}

/// <summary>Decisão de rota de UM evento — com o porquê, para o log contar a história.</summary>
public readonly record struct RotaDoCartao(Unit? Destino, string Motivo);

public class DivisaoDaContaService(
    AppDbContext db,
    IMemoryCache cache,
    ILogger<DivisaoDaContaService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly IMemoryCache _cache = cache;
    private readonly ILogger<DivisaoDaContaService> _logger = logger;

    public const string PrefixoDaChave = "kommo:divisao:";
    private static readonly TimeSpan Validade = TimeSpan.FromMinutes(2);

    public static string ChaveDe(string subdominio) => PrefixoDaChave + subdominio.Trim().ToLowerInvariant();

    /// <summary>A divisão da conta desta unidade, ou null se a conta é de uma unidade só.</summary>
    public Task<DivisaoDaConta?> DaUnidadeAsync(Unit unit, CancellationToken ct = default)
        => string.IsNullOrWhiteSpace(unit.KommoSubdomain)
            ? Task.FromResult<DivisaoDaConta?>(null)
            : ObterAsync(unit.KommoSubdomain!, ct);

    /// <summary>
    /// Lê a divisão da conta (cache de 2 min, inclusive do "não tem"). Lança
    /// <see cref="InvalidOperationException"/> quando a configuração existe mas está
    /// errada — ver "falha fechada" no topo da classe.
    /// </summary>
    public async Task<DivisaoDaConta?> ObterAsync(string subdominio, CancellationToken ct = default)
    {
        var chave = ChaveDe(subdominio);
        if (_cache.TryGetValue(chave, out DivisaoDaConta? guardada)) return guardada;

        var linha = await _db.AppConfigurations.AsNoTracking()
            .Where(c => c.Key == chave)
            .Select(c => c.Value)
            .FirstOrDefaultAsync(ct);

        DivisaoDaConta? divisao = null;
        if (!string.IsNullOrWhiteSpace(linha))
            divisao = await MontarAsync(subdominio.Trim().ToLowerInvariant(), linha!, ct);

        _cache.Set(chave, divisao, Validade);
        return divisao;
    }

    private sealed class ConfigJson
    {
        public long CampoId { get; set; }
        public string? NomeDoCampo { get; set; }
        public string? Dona { get; set; }
        public string? SemUnidade { get; set; }
        public List<OpcaoJson>? Opcoes { get; set; }
    }

    private sealed class OpcaoJson
    {
        public long? EnumId { get; set; }
        public string? Valor { get; set; }
        public string? Unidade { get; set; }
    }

    private async Task<DivisaoDaConta> MontarAsync(string subdominio, string json, CancellationToken ct)
    {
        ConfigJson? cfg;
        try
        {
            cfg = JsonSerializer.Deserialize<ConfigJson>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            throw Invalida(subdominio, $"JSON ilegível ({ex.Message})");
        }

        if (cfg is null || cfg.CampoId <= 0) throw Invalida(subdominio, "sem campoId");
        if (string.IsNullOrWhiteSpace(cfg.Dona)) throw Invalida(subdominio, "sem a unidade dona");
        if (string.IsNullOrWhiteSpace(cfg.SemUnidade)) throw Invalida(subdominio, "sem a unidade \"sem unidade definida\"");
        if (cfg.Opcoes is not { Count: >= 2 }) throw Invalida(subdominio, "precisa de pelo menos 2 opções");

        var slugs = cfg.Opcoes.Select(o => o.Unidade).Append(cfg.Dona).Append(cfg.SemUnidade)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        var unidades = await _db.Units.AsNoTracking()
            .Where(u => u.Slug != null && slugs.Contains(u.Slug))
            .ToListAsync(ct);

        Unit Achar(string? slug, string papel)
        {
            var s = slug?.Trim().ToLowerInvariant();
            return unidades.FirstOrDefault(u => u.Slug == s)
                ?? throw Invalida(subdominio, $"{papel} '{slug}' não existe em units");
        }

        var opcoes = cfg.Opcoes.Select(o =>
        {
            if (string.IsNullOrWhiteSpace(o.Valor)) throw Invalida(subdominio, "opção sem valor");
            return new DivisaoDaConta.Opcao(Achar(o.Unidade, "unidade da opção"), o.EnumId, o.Valor!.Trim());
        }).ToList();

        var dona = Achar(cfg.Dona, "dona");
        var semUnidade = Achar(cfg.SemUnidade, "sem unidade");

        if (opcoes.Select(o => o.Unidade.Id).Distinct().Count() != opcoes.Count)
            throw Invalida(subdominio, "duas opções apontam para a mesma unidade");
        if (opcoes.Any(o => o.Unidade.Id == semUnidade.Id))
            throw Invalida(subdominio, "a unidade \"sem unidade definida\" não pode ser opção do campo");
        if (opcoes.All(o => o.Unidade.Id != dona.Id))
            throw Invalida(subdominio, "a dona precisa ser uma das opções");
        if (opcoes.Select(o => o.Unidade.ClinicId).Append(semUnidade.ClinicId).Distinct().Count() != opcoes.Count + 1)
            throw Invalida(subdominio, "duas unidades com o mesmo ClinicId");
        var outraConta = opcoes.Select(o => o.Unidade).Append(semUnidade)
            .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u.KommoSubdomain)
                && !string.Equals(u.KommoSubdomain!.Trim(), subdominio, StringComparison.OrdinalIgnoreCase));
        if (outraConta is not null)
            throw Invalida(subdominio, $"a unidade '{outraConta.Slug}' é de outra conta Kommo ({outraConta.KommoSubdomain})");

        return new DivisaoDaConta
        {
            Subdominio = subdominio,
            CampoId = cfg.CampoId,
            NomeDoCampo = string.IsNullOrWhiteSpace(cfg.NomeDoCampo) ? "Unidade de atendimento" : cfg.NomeDoCampo!,
            Dona = dona,
            SemUnidade = semUnidade,
            Opcoes = opcoes,
        };
    }

    private InvalidOperationException Invalida(string subdominio, string motivo)
    {
        _logger.LogError("❌ Divisão da conta Kommo {Sub} inválida: {Motivo}. A ingestão desta conta fica parada até corrigir.",
            subdominio, motivo);
        return new InvalidOperationException($"Divisão da conta Kommo '{subdominio}' inválida: {motivo}");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Regra pura (testada em DivisaoDaContaTests)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Texto da opção sem acento, sem caixa e sem espaço nas pontas.</summary>
    public static string Normalizar(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var decomposto = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        foreach (var c in decomposto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Lê o campo da divisão no <c>CustomFieldsJson</c> do evento. Aceita os dois formatos
    /// que entram aqui: o do sync (<c>{field_id, value, enum_id}</c>) e o do webhook
    /// (<c>{field_id, value}</c>, sem o id da opção). <c>Presente</c> = o campo veio E tem valor.
    /// </summary>
    public static (bool Presente, long? EnumId, string? Valor) LerCampo(string? customFieldsJson, long campoId)
    {
        if (string.IsNullOrWhiteSpace(customFieldsJson)) return (false, null, null);
        try
        {
            using var doc = JsonDocument.Parse(customFieldsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return (false, null, null);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                if (!el.TryGetProperty("field_id", out var fid)) continue;
                var id = fid.ValueKind == JsonValueKind.Number && fid.TryGetInt64(out var n) ? n
                    : fid.ValueKind == JsonValueKind.String && long.TryParse(fid.GetString(), out var p) ? p
                    : 0;
                if (id != campoId) continue;

                string? valor = null;
                long? enumId = null;
                if (el.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String)
                    valor = v.GetString();
                if (el.TryGetProperty("enum_id", out var e) && e.ValueKind == JsonValueKind.Number
                    && e.TryGetInt64(out var en) && en > 0)
                    enumId = en;
                if (el.TryGetProperty("values", out var vals) && vals.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in vals.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object) continue;
                        if (valor is null && item.TryGetProperty("value", out var iv) && iv.ValueKind == JsonValueKind.String)
                            valor = iv.GetString();
                        if (enumId is null)
                            foreach (var nome in new[] { "enum_id", "enum" })
                                if (item.TryGetProperty(nome, out var ie) && ie.ValueKind == JsonValueKind.Number
                                    && ie.TryGetInt64(out var ien) && ien > 0)
                                    enumId = ien;
                        break;
                    }
                }
                var presente = enumId is > 0 || !string.IsNullOrWhiteSpace(valor);
                return (presente, enumId, valor?.Trim());
            }
        }
        catch (JsonException) { /* json malformado = campo ausente */ }
        return (false, null, null);
    }

    /// <summary>
    /// Para qual unidade vai o evento. Regra inteira em um lugar só:
    /// <list type="number">
    /// <item>exclusão: vai para onde o cartão está (não tem campo para ler);</item>
    /// <item>campo presente: a opção decide; opção desconhecida → "sem unidade definida";</item>
    /// <item>campo ausente numa lista COMPLETA de campos (sync REST): o campo está vazio
    /// no Kommo → "sem unidade definida";</item>
    /// <item>campo ausente numa lista PARCIAL (webhook): fica com a decisão anterior do
    /// mesmo lote, senão onde o cartão já está, senão "sem unidade definida".</item>
    /// </list>
    /// </summary>
    public static RotaDoCartao Decidir(
        DivisaoDaConta d, string? customFieldsJson, bool camposCompletos,
        Unit? ondeEsta, Unit? decididoNoLote, bool exclusao)
    {
        if (exclusao)
        {
            var alvo = decididoNoLote ?? ondeEsta;
            return alvo is null
                ? new RotaDoCartao(null, "exclusão de cartão que não está no painel")
                : new RotaDoCartao(alvo, "exclusão: vai para onde o cartão está");
        }

        var (presente, enumId, valor) = LerCampo(customFieldsJson, d.CampoId);
        if (presente)
        {
            var porCampo = d.UnidadeDoValor(enumId, valor);
            return porCampo is null
                ? new RotaDoCartao(d.SemUnidade, $"opção desconhecida no campo ('{valor}', enum {enumId})")
                : new RotaDoCartao(porCampo, $"campo = {valor ?? enumId?.ToString()}");
        }

        if (camposCompletos)
            return new RotaDoCartao(d.SemUnidade, "campo vazio no cartão");

        if (decididoNoLote is not null) return new RotaDoCartao(decididoNoLote, "mesma decisão do lote");
        if (ondeEsta is not null) return new RotaDoCartao(ondeEsta, "evento sem o campo: fica onde está");
        return new RotaDoCartao(d.SemUnidade, "cartão novo sem o campo");
    }

    /// <summary>
    /// Entre os cartões que a busca da Kommo achou (por telefone), o primeiro que é DESTA
    /// unidade. Conta de uma unidade só: o primeiro, como sempre. Conta dividida: cartão da
    /// outra unidade ou sem unidade definida não serve — vincular o tratamento da franquia
    /// de Caxias a um cartão de Petrópolis poria o valor na unidade errada.
    /// </summary>
    public static KommoApiLead? EscolherCartaoDaUnidade(
        IReadOnlyList<KommoApiLead>? achados, DivisaoDaConta? divisao, Unit unidade)
    {
        if (achados is null || achados.Count == 0) return null;
        if (divisao is null) return achados[0];
        return achados.FirstOrDefault(l =>
        {
            var valor = l.CustomFieldsValues?
                .FirstOrDefault(f => f.FieldId == divisao.CampoId)?
                .Values?.FirstOrDefault();
            if (valor is null) return false;
            return divisao.UnidadeDoValor(valor.EnumId, valor.GetStringValueRawOnly())?.Id == unidade.Id;
        });
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Repartição (com mudança de unidade)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Separa os eventos por unidade de destino e, quando o cartão trocou de unidade,
    /// já move a linha do lead (e as filhas com tenant) antes da ingestão. Eventos que não
    /// são de lead ficam com a <paramref name="recebedora"/> (a ingestão só os registra).
    /// </summary>
    public async Task<List<(Unit Unidade, List<LeadEvent> Eventos)>> RepartirAsync(
        DivisaoDaConta d, Unit recebedora, IReadOnlyList<LeadEvent> eventos, CancellationToken ct = default)
    {
        var tenants = d.Tenants.ToList();
        var ids = eventos
            .Where(e => string.Equals(e.EntityType, "lead", StringComparison.OrdinalIgnoreCase))
            .Select(e => int.TryParse(e.ExternalId, out var i) ? i : 0)
            .Where(i => i > 0)
            .Distinct()
            .ToList();

        // Rastreadas de propósito: mover = alterar estas linhas.
        var existentes = ids.Count == 0
            ? new List<Lead>()
            : await _db.Leads
                .Where(l => ids.Contains(l.ExternalId) && tenants.Contains(l.TenantId))
                .ToListAsync(ct);
        var porId = existentes.GroupBy(l => l.ExternalId).ToDictionary(g => g.Key, g => g.ToList());

        var decididoNoLote = new Dictionary<int, Unit>();
        var partes = new Dictionary<int, (Unit Unidade, List<LeadEvent> Eventos)>();
        void Por(Unit u, LeadEvent ev)
        {
            if (!partes.TryGetValue(u.Id, out var p)) partes[u.Id] = p = (u, new List<LeadEvent>());
            p.Eventos.Add(ev);
        }

        var moveu = false;
        foreach (var ev in eventos)
        {
            if (!string.Equals(ev.EntityType, "lead", StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(ev.ExternalId, out var extId) || extId <= 0)
            {
                Por(recebedora, ev);
                continue;
            }

            porId.TryGetValue(extId, out var linhas);
            var principal = linhas?
                .Where(l => l.Status != LeadQueryExtensions.StatusDeleted)
                .OrderByDescending(l => l.UpdatedAt).FirstOrDefault()
                ?? linhas?.OrderByDescending(l => l.UpdatedAt).FirstOrDefault();
            var ondeEsta = principal is null ? null : d.UnidadeDoTenant(principal.TenantId);
            decididoNoLote.TryGetValue(extId, out var anterior);

            var rota = Decidir(d, ev.CustomFieldsJson, ev.CamposCompletos, ondeEsta, anterior,
                string.Equals(ev.Action, "delete", StringComparison.OrdinalIgnoreCase));

            if (rota.Destino is null)
            {
                _logger.LogInformation("↪️ Divisão {Sub}: lead {Id} ignorado ({Motivo})", d.Subdominio, extId, rota.Motivo);
                continue;
            }

            if (linhas is { Count: > 0 })
                moveu |= await AcomodarAsync(d, extId, linhas, rota.Destino, rota.Motivo, ct);

            decididoNoLote[extId] = rota.Destino;
            Por(rota.Destino, ev);
        }

        if (moveu) await _db.SaveChangesAsync(ct);
        return partes.Values.ToList();
    }

    /// <summary>
    /// Deixa UMA linha viva do lead, no tenant do destino. Se ela já está lá, as cópias em
    /// outras unidades da conta (só existem se algo gravou antes da divisão) viram
    /// "deleted" — somadas, contariam o mesmo cartão duas vezes.
    /// </summary>
    private async Task<bool> AcomodarAsync(
        DivisaoDaConta d, int extId, List<Lead> linhas, Unit destino, string motivo, CancellationToken ct)
    {
        var mudou = false;
        var noDestino = linhas.FirstOrDefault(l => l.TenantId == destino.ClinicId);
        var principal = noDestino
            ?? linhas.Where(l => l.Status != LeadQueryExtensions.StatusDeleted)
                     .OrderByDescending(l => l.UpdatedAt).FirstOrDefault()
            ?? linhas.OrderByDescending(l => l.UpdatedAt).First();

        if (principal.TenantId != destino.ClinicId)
        {
            var de = d.UnidadeDoTenant(principal.TenantId);
            _logger.LogInformation(
                "🔀 Divisão {Sub}: lead {Id} muda de unidade {De} → {Para} ({Motivo})",
                d.Subdominio, extId, de?.Slug ?? principal.TenantId.ToString(), destino.Slug, motivo);
            await MoverAsync(principal, destino, ct);
            mudou = true;
        }

        foreach (var sobra in linhas.Where(l => !ReferenceEquals(l, principal)
                                               && l.Status != LeadQueryExtensions.StatusDeleted))
        {
            _logger.LogWarning(
                "⚠️ Divisão {Sub}: lead {Id} tinha cópia no tenant {Tenant} — marcada como deleted (fica a de {Destino})",
                d.Subdominio, extId, sobra.TenantId, destino.Slug);
            sobra.Status = LeadQueryExtensions.StatusDeleted;
            sobra.UpdatedAt = DateTime.UtcNow;
            mudou = true;
        }

        if (principal.TenantId == destino.ClinicId && principal.UnitId != destino.Id)
        {
            principal.UnitId = destino.Id;
            mudou = true;
        }
        return mudou;
    }

    /// <summary>
    /// Troca o tenant do lead e de tudo que é dele e carrega o tenant. Sem as filhas, a
    /// consulta/pagamento/tentativa de resgate ficariam contando na unidade antiga.
    /// O responsável (Attendant) é por unidade e é refeito pela ingestão logo em seguida.
    /// </summary>
    private async Task MoverAsync(Lead lead, Unit destino, CancellationToken ct)
    {
        var t = destino.ClinicId;
        var u = destino.Id;
        lead.TenantId = t;
        lead.UnitId = u;
        lead.AttendantId = null;
        lead.UpdatedAt = DateTime.UtcNow;

        foreach (var x in await _db.RecoveryAttempts.Where(x => x.LeadId == lead.Id).ToListAsync(ct)) x.TenantId = t;
        foreach (var x in await _db.Consultations.Where(x => x.LeadId == lead.Id).ToListAsync(ct)) { x.TenantId = t; x.UnitId = u; }
        foreach (var x in await _db.Treatments.Where(x => x.LeadId == lead.Id).ToListAsync(ct)) { x.TenantId = t; x.UnitId = u; }
        foreach (var x in await _db.Payments.Where(x => x.LeadId == lead.Id).ToListAsync(ct)) { x.TenantId = t; x.UnitId = u; }
        foreach (var x in await _db.LeadPaymentReceipts.Where(x => x.LeadId == lead.Id).ToListAsync(ct)) x.TenantId = t;
        foreach (var x in await _db.LeadAttributions.Where(x => x.LeadId == lead.Id).ToListAsync(ct)) x.TenantId = t;
        foreach (var x in await _db.KpiExclusions.Where(x => x.LeadId == lead.Id).ToListAsync(ct)) { x.TenantId = t; x.UnitId = u; }
        foreach (var x in await _db.AgentConversations.Where(x => x.LeadId == lead.Id).ToListAsync(ct)) { x.TenantId = t; x.UnitId = u; }
    }
}
