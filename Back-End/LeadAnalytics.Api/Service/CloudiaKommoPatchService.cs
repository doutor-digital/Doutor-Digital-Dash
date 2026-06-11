using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.Jobs;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Pega um batch de import já aplicado e PATCHa os custom fields no Kommo
/// (Tipo de lead, Data de criação lead, Origem, Interação, Motivo, Tipo de
/// resgaste, Sexo, Qualificação, Observações de consulta). Roda em background.
/// </summary>
public class CloudiaKommoPatchService(
    AppDbContext db,
    IHttpClientFactory httpFactory,
    ICloudiaKommoPatchJobQueue queue,
    ILogger<CloudiaKommoPatchService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly IHttpClientFactory _httpFactory = httpFactory;
    private readonly ICloudiaKommoPatchJobQueue _queue = queue;
    private readonly ILogger<CloudiaKommoPatchService> _logger = logger;

    private const int RateMs = 280; // ~3.5 req/s (folgado vs limit ~7/s da Kommo)

    public static readonly string[] AllowedFields =
        ["tipo_lead","data_criacao","origem","interacao","motivo","tipo_resgate","data_agendamento","sexo","qualificacao","observacao"];

    public async Task<CloudiaKommoJob> StartJobAsync(int batchId, List<string> fields, int? userId, CancellationToken ct)
    {
        var batch = await _db.CloudiaImportBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct)
            ?? throw new InvalidOperationException("Batch não encontrado");
        if (batch.Status != "applied")
            throw new InvalidOperationException("Batch precisa estar 'applied' (não revertido) pra rodar Kommo PATCH.");
        var csv = JsonSerializer.Deserialize<List<CloudiaCsvImportService.CsvDataEntry>>(batch.CsvDataJson) ?? new();
        if (csv.Count == 0)
            throw new InvalidOperationException("Batch sem csv_data (pré-feature). Re-aplica o CSV pra criar um batch com dados.");

        // Valida fields
        var validFields = fields.Where(f => AllowedFields.Contains(f)).Distinct().ToList();
        if (validFields.Count == 0)
            throw new InvalidOperationException("Nenhum campo válido selecionado.");

        var job = new CloudiaKommoJob
        {
            BatchId = batchId,
            UnitId = batch.UnitId,
            TenantId = batch.TenantId,
            Status = "queued",
            Total = csv.Count,
            FieldsJson = JsonSerializer.Serialize(validFields),
            CreatedByUserId = userId,
        };
        _db.CloudiaKommoJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        await _queue.EnqueueAsync(new CloudiaKommoPatchJobRequest(job.Id), ct);
        return job;
    }

    public async Task<CloudiaKommoJob?> GetJobAsync(string jobId, CancellationToken ct)
        => await _db.CloudiaKommoJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);

    public async Task<List<CloudiaKommoJob>> ListJobsAsync(int unitId, CancellationToken ct)
        => await _db.CloudiaKommoJobs.AsNoTracking()
            .Where(j => j.UnitId == unitId)
            .OrderByDescending(j => j.CreatedAt)
            .Take(20)
            .ToListAsync(ct);

    public async Task<bool> RequestCancelAsync(string jobId, CancellationToken ct)
    {
        var job = await _db.CloudiaKommoJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return false;
        if (job.Status is "completed" or "failed" or "cancelled") return false;
        job.CancelRequested = true;
        if (job.Status == "queued") job.Status = "cancelling";
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Worker (chamado pelo BackgroundService)
    // ════════════════════════════════════════════════════════════════════════

    public async Task RunJobAsync(string jobId, CancellationToken stoppingToken)
    {
        var job = await _db.CloudiaKommoJobs.FirstOrDefaultAsync(j => j.Id == jobId, stoppingToken);
        if (job is null) return;
        var batch = await _db.CloudiaImportBatches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == job.BatchId, stoppingToken);
        if (batch is null) { await FailAsync(job, "Batch sumiu", stoppingToken); return; }

        var unit = await _db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == job.UnitId, stoppingToken);
        if (unit is null || string.IsNullOrWhiteSpace(unit.KommoSubdomain) || string.IsNullOrWhiteSpace(unit.KommoAccessToken))
        { await FailAsync(job, "Unidade sem subdomínio/token Kommo", stoppingToken); return; }

        var csv = JsonSerializer.Deserialize<List<CloudiaCsvImportService.CsvDataEntry>>(batch.CsvDataJson) ?? new();
        var fields = JsonSerializer.Deserialize<List<string>>(job.FieldsJson) ?? new();
        var fieldSet = new HashSet<string>(fields);

        job.Status = "running";
        job.StartedAt = DateTime.UtcNow;
        job.Total = csv.Count;
        await _db.SaveChangesAsync(stoppingToken);

        // Busca enums das selects necessárias (1x — cache por field_id)
        var enums = await BuildEnumMapsAsync(unit.KommoSubdomain!, unit.KommoAccessToken!, stoppingToken);

        var http = _httpFactory.CreateClient("kommo");
        http.BaseAddress = new Uri($"https://{unit.KommoSubdomain}.kommo.com");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", unit.KommoAccessToken);

        var processed = 0; var ok = 0; var fail = 0; var lastSave = DateTime.UtcNow;

        foreach (var entry in csv)
        {
            if (stoppingToken.IsCancellationRequested) break;

            // checa cancelamento via DB (a cada 25 leads pra não bater muito)
            if (processed % 25 == 0)
            {
                var cancel = await _db.CloudiaKommoJobs.AsNoTracking()
                    .Where(j => j.Id == jobId).Select(j => j.CancelRequested).FirstOrDefaultAsync(stoppingToken);
                if (cancel) { job.Status = "cancelled"; job.FinishedAt = DateTime.UtcNow; await _db.SaveChangesAsync(stoppingToken); return; }
            }

            try
            {
                var payload = BuildPatchPayload(entry, fieldSet, enums);
                if (payload.Count == 0) { processed++; continue; } // nada pra mandar

                var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/v4/leads/{entry.ExternalId}")
                {
                    Content = JsonContent.Create(new { custom_fields_values = payload }),
                };
                var resp = await http.SendAsync(req, stoppingToken);
                if ((int)resp.StatusCode is 200 or 202) ok++;
                else { fail++; _logger.LogWarning("Kommo PATCH falhou lead={Id} status={Status}", entry.ExternalId, (int)resp.StatusCode); }
            }
            catch (Exception ex)
            {
                fail++;
                _logger.LogError(ex, "PATCH erro lead={Id}", entry.ExternalId);
            }
            processed++;
            await Task.Delay(RateMs, stoppingToken);

            // Salva progresso a cada 5s pra UI ver
            if ((DateTime.UtcNow - lastSave).TotalSeconds >= 5)
            {
                job.Processed = processed; job.Succeeded = ok; job.Failed = fail;
                await _db.SaveChangesAsync(stoppingToken);
                lastSave = DateTime.UtcNow;
            }
        }

        job.Processed = processed; job.Succeeded = ok; job.Failed = fail;
        job.Status = stoppingToken.IsCancellationRequested ? "cancelled" : "completed";
        job.FinishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(stoppingToken);

        _logger.LogInformation("Kommo PATCH job {JobId} terminou. ok={Ok} fail={Fail}", jobId, ok, fail);
    }

    private async Task FailAsync(CloudiaKommoJob job, string msg, CancellationToken ct)
    {
        job.Status = "failed";
        job.ErrorMessage = msg;
        job.FinishedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    // ─── PATCH payload ──────────────────────────────────────────────────────

    private const long FIELD_TIPO       = 2433585;
    private const long FIELD_DATA       = 2433587;
    private const long FIELD_ORIGEM     = 2424466;
    private const long FIELD_INTERACAO  = 2424818;
    private const long FIELD_MOTIVO     = 2424474;
    private const long FIELD_DATA_AGEN  = 2424488;
    private const long FIELD_TIPO_RESG  = 2424816;
    private const long FIELD_SEXO       = 2424482;
    private const long FIELD_QUALIF     = 2424524;
    private const long FIELD_OBS_CONS   = 2426322;

    private const long ENUM_TIPO_RESGATE = 1819901;

    private List<object> BuildPatchPayload(
        CloudiaCsvImportService.CsvDataEntry e,
        HashSet<string> fields,
        Dictionary<long, Dictionary<string, long>> enums)
    {
        var cfv = new List<object>();

        if (fields.Contains("tipo_lead"))
            cfv.Add(new { field_id = FIELD_TIPO, values = new[] { new { enum_id = ENUM_TIPO_RESGATE } } });

        if (fields.Contains("data_criacao"))
        {
            var unix = ParseBrUnix(e.DataOrigem);
            if (unix > 0) cfv.Add(new { field_id = FIELD_DATA, values = new[] { new { value = unix } } });
        }

        if (fields.Contains("origem"))
        {
            var enumId = ResolveEnum(enums, FIELD_ORIGEM, MapOrigem(e.Origem));
            if (enumId > 0) cfv.Add(new { field_id = FIELD_ORIGEM, values = new[] { new { enum_id = enumId } } });
        }

        if (fields.Contains("interacao"))
        {
            var v = e.Interacao.Trim();
            var key = v.Equals("Sim", StringComparison.OrdinalIgnoreCase) ? "Sim"
                    : (v.Equals("Não", StringComparison.OrdinalIgnoreCase) || v.Equals("Nao", StringComparison.OrdinalIgnoreCase) ? "Não" : null);
            if (key != null)
            {
                var enumId = ResolveEnum(enums, FIELD_INTERACAO, key);
                if (enumId > 0) cfv.Add(new { field_id = FIELD_INTERACAO, values = new[] { new { enum_id = enumId } } });
            }
        }

        if (fields.Contains("motivo"))
        {
            var key = MapMotivo(e.Motivo);
            if (key != null)
            {
                var enumId = ResolveEnum(enums, FIELD_MOTIVO, key);
                if (enumId > 0) cfv.Add(new { field_id = FIELD_MOTIVO, values = new[] { new { enum_id = enumId } } });
            }
        }

        if (fields.Contains("data_agendamento"))
        {
            var unix = ParseBrUnix(e.DataAgendamento);
            if (unix > 0) cfv.Add(new { field_id = FIELD_DATA_AGEN, values = new[] { new { value = unix } } });
        }

        if (fields.Contains("tipo_resgate"))
        {
            var key = MapTipoResgate(e.TipoResgate);
            if (key != null)
            {
                var enumId = ResolveEnum(enums, FIELD_TIPO_RESG, key);
                if (enumId > 0) cfv.Add(new { field_id = FIELD_TIPO_RESG, values = new[] { new { enum_id = enumId } } });
            }
        }

        if (fields.Contains("sexo"))
        {
            var key = GuessSex(e.Nome);
            if (key != null)
            {
                var enumId = ResolveEnum(enums, FIELD_SEXO, key);
                if (enumId > 0) cfv.Add(new { field_id = FIELD_SEXO, values = new[] { new { enum_id = enumId } } });
            }
        }

        if (fields.Contains("qualificacao"))
        {
            var key = GuessQualif(e.Agendou, e.Interacao);
            if (key != null)
            {
                var enumId = ResolveEnum(enums, FIELD_QUALIF, key);
                if (enumId > 0) cfv.Add(new { field_id = FIELD_QUALIF, values = new[] { new { enum_id = enumId } } });
            }
        }

        if (fields.Contains("observacao") && !string.IsNullOrWhiteSpace(e.Observacao))
        {
            var text = e.Observacao.Trim();
            if (text.Length > 1000) text = text[..1000];
            cfv.Add(new { field_id = FIELD_OBS_CONS, values = new[] { new { value = text } } });
        }

        return cfv;
    }

    private static long ResolveEnum(Dictionary<long, Dictionary<string, long>> enums, long fieldId, string? value)
    {
        if (value is null) return 0;
        if (!enums.TryGetValue(fieldId, out var map)) return 0;
        return map.TryGetValue(value, out var id) ? id : 0;
    }

    private async Task<Dictionary<long, Dictionary<string, long>>> BuildEnumMapsAsync(
        string subdomain, string token, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient("kommo");
        http.BaseAddress = new Uri($"https://{subdomain}.kommo.com");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var result = new Dictionary<long, Dictionary<string, long>>();
        long[] selectFields = [FIELD_TIPO, FIELD_ORIGEM, FIELD_INTERACAO, FIELD_MOTIVO, FIELD_TIPO_RESG, FIELD_SEXO, FIELD_QUALIF];
        foreach (var fid in selectFields)
        {
            try
            {
                var resp = await http.GetFromJsonAsync<JsonElement>($"/api/v4/leads/custom_fields/{fid}", ct);
                var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                if (resp.TryGetProperty("enums", out var enumsEl) && enumsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in enumsEl.EnumerateArray())
                    {
                        if (e.TryGetProperty("id", out var idEl) && e.TryGetProperty("value", out var valEl))
                            map[valEl.GetString() ?? ""] = idEl.GetInt64();
                    }
                }
                result[fid] = map;
                await Task.Delay(250, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao buscar enums field {Id}", fid);
                result[fid] = new();
            }
        }
        return result;
    }

    // ─── Maps idênticos ao script Node ─────────────────────────────────────

    private static string? MapOrigem(string raw)
    {
        var s = (raw ?? "").Trim();
        return s switch
        {
            "Campanha Meta (Instagram)" => "Meta-Instagram",
            "Campanha Meta (Facebook)" => "Meta-Facebook",
            "Campanha Google" => "Google",
            "Indicação" => "Indicação",
            "Sem origem" or "SEM ORIGEM" or "" => "Sem origem",
            "Site Oficial Doutor Hérnia" => "Site oficial - Franquia",
            "FACEBOOK" => "Meta-Facebook",
            "INSTAGRAM" => "Meta-Instagram",
            "Ligação Google" => "Google",
            "Fachada" => "Fachada",
            "Direto Instagram" => "Org-Instagram",
            "Panfleto" => "Panfleto",
            _ => null,
        };
    }

    private static string? MapMotivo(string raw)
    {
        var s = (raw ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(s)) return null;
        if (s.Contains("sem interaç") || s.Contains("sem intereç") || s.Contains("sem interacao")) return "Não interagiu";
        if (s.Contains("não deu continui") || s.Contains("nao deu continui")) return "Não deu continuidade ao atendimento";
        if (s.Contains("mora fora") || s.Contains("outra cidade")) return "Outra cidade";
        if (s.Contains("vai se organi")) return "Vai se organizar";
        if (s.Contains("terceiros") || s.Contains("terceiro")) return "Informação para terceiro";
        if (s.Contains("plano de sa")) return "Plano de Saúde";
        if (s.Contains("engano")) return "Clicou por engano";
        if (s.Contains("sem condi")) return "Sem condições financeira";
        if (s == "sem interesse" || s.Contains("desinteresse")) return "Sem interesse";
        if (s.Contains("outro tipo de tratamento") || s.Contains("outra patologia") || s.Contains("busca laudo")) return "Outra patologia";
        if (s.Contains("viagem") || s.Contains("viajando")) return "Está viajando";
        return null;
    }

    private static string? MapTipoResgate(string raw)
    {
        var s = (raw ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(s)) return null;
        if (s.Contains("mensagem")) return "Mensagem";
        if (s.Contains("ligaç") || s.Contains("ligac")) return "Ligação";
        if (s.Contains("disparo")) return "Disparo";
        return null;
    }

    // Heurística simplificada de sexo por primeiro nome (cobertura ~75% pt-BR)
    private static readonly HashSet<string> NamesF = new(StringComparer.OrdinalIgnoreCase)
    {
        "maria","ana","francisca","antonia","adriana","juliana","fernanda","patricia","aline","sandra",
        "camila","amanda","bruna","jessica","leticia","julia","luciana","marcia","rosangela","vanessa",
        "daniela","carla","silvia","marta","rita","elaine","vera","rosa","andrea","larissa","renata",
        "tania","simone","carolina","gabriela","rafaela","priscila","bianca","tatiana","michele","helena",
        "alice","clara","sofia","laura","manuela","beatriz","vitoria","lara","livia","eloa","luzia",
        "rosilandia","oneide","dora","edileusa","raimunda","zete","josa","ester","jaqueline","cleia",
        "edinalva","lourdes","lourdinha","claudia","sonia","eliete","jaina","kelly","francineide",
    };
    private static readonly HashSet<string> NamesM = new(StringComparer.OrdinalIgnoreCase)
    {
        "jose","joao","antonio","francisco","carlos","paulo","pedro","luiz","luis","marcos","marcelo",
        "andre","roberto","sergio","daniel","rafael","rodrigo","fernando","gabriel","mateus","matheus",
        "lucas","vinicius","bruno","jorge","leonardo","marcio","eduardo","adriano","alex","alexandre",
        "anderson","renato","ricardo","rogerio","edson","geraldo","wagner","wellington","wilson",
        "amauri","arnaldo","artur","arthur","aurelio","augusto","benedito","caio","claudio","cleber",
        "diego","dimas","dorival","douglas","edinaldo","edmilson","edmir","edvaldo","emerson","fabio",
        "felipe","gilmar","gilberto","gustavo","humberto","ivan","jefferson","junior","kleber","lincoln",
        "manoel","mario","mauricio","mauro","milton","moacir","murilo","nelson","newton","nilson","otavio",
        "patrick","raul","reginaldo","robson","ronaldo","romildo","romulo","ronaldo","saulo","sebastiao",
        "silas","silvio","tadeu","thiago","tiago","valter","walter","wladimir","yago","yuri",
        "queiroz","allan","raimundo",
    };

    private static string? GuessSex(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return null;
        var first = Normalize(fullName).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrEmpty(first)) return null;
        if (NamesF.Contains(first)) return "Feminino";
        if (NamesM.Contains(first)) return "Masculino";
        if (first.Length >= 4 && first.EndsWith("a") && !first.EndsWith("ca") && !first.EndsWith("ka")) return "Feminino";
        if (first.EndsWith("o") || first.EndsWith("os")) return "Masculino";
        return null;
    }

    private static string? GuessQualif(string agendou, string interacao)
    {
        var a = (agendou ?? "").Trim().ToLowerInvariant();
        var i = (interacao ?? "").Trim().ToLowerInvariant();
        if (a == "sim") return "Quente";
        if (i == "sim") return "Morno";
        if (i == "não" || i == "nao") return "Frio";
        return null;
    }

    private static long ParseBrUnix(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        var m = System.Text.RegularExpressions.Regex.Match(s.Trim(),
            @"^(\d{1,2})/(\d{1,2})/(\d{4})(?:\s+(\d{1,2}):(\d{2})(?::(\d{2}))?)?$");
        if (!m.Success) return 0;
        try
        {
            var local = new DateTime(int.Parse(m.Groups[3].Value), int.Parse(m.Groups[2].Value),
                int.Parse(m.Groups[1].Value),
                m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 12,
                m.Groups[5].Success ? int.Parse(m.Groups[5].Value) : 0,
                m.Groups[6].Success ? int.Parse(m.Groups[6].Value) : 0,
                DateTimeKind.Unspecified);
            var brTz = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
            var utc = TimeZoneInfo.ConvertTimeToUtc(local, brTz);
            return new DateTimeOffset(utc).ToUnixTimeSeconds();
        }
        catch { return 0; }
    }

    private static string Normalize(string s)
    {
        var norm = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in norm)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch) || ch == ' ') sb.Append(char.ToLower(ch, CultureInfo.InvariantCulture));
            else sb.Append(' ');
        }
        return sb.ToString().Trim();
    }
}
