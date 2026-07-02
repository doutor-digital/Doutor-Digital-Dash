using System.Text.Json;
using LeadAnalytics.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Diagnóstico: quebra os leads do banco de uma unidade no período pra explicar
/// divergência com a Kommo. Mostra contagens por Source / Status / presença de
/// ExternalId, e devolve amostras dos leads "suspeitos" (sem ExternalId Kommo,
/// deletados, source != Kommo) pra você inspecionar.
///
/// Uso: GET /api/admin/lead-count-diagnostic/{unitId}?dateFrom=2026-06-01&dateTo=2026-06-12
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/admin/lead-count-diagnostic")]
public class AdminLeadCountDiagnosticController(AppDbContext db) : ControllerBase
{
    [HttpGet("{unitId:int}")]
    public Task<IActionResult> DiagnoseById(
        int unitId,
        [FromQuery] DateTime dateFrom,
        [FromQuery] DateTime dateTo,
        CancellationToken ct) => DiagnoseInternal(u => u.Id == unitId, $"id={unitId}", dateFrom, dateTo, ct);

    /// <summary>
    /// Mesmo diagnóstico, mas casa unidade por NOME (substring case-insensitive).
    /// Ex.: GET /api/admin/lead-count-diagnostic/by-name/imperatriz?dateFrom=...&dateTo=...
    /// </summary>
    [HttpGet("by-name/{nameLike}")]
    public Task<IActionResult> DiagnoseByName(
        string nameLike,
        [FromQuery] DateTime dateFrom,
        [FromQuery] DateTime dateTo,
        CancellationToken ct)
    {
        var pattern = $"%{nameLike}%";
        return DiagnoseInternal(u => EF.Functions.ILike(u.Name, pattern), $"name~={nameLike}", dateFrom, dateTo, ct);
    }

    private async Task<IActionResult> DiagnoseInternal(
        System.Linq.Expressions.Expression<Func<Models.Unit, bool>> match,
        string criterio,
        DateTime dateFrom, DateTime dateTo, CancellationToken ct)
    {
        var unitRow = await db.Units.AsNoTracking()
            .Where(match)
            .Select(u => new { u.Id, u.ClinicId, u.Name })
            .FirstOrDefaultAsync(ct);
        if (unitRow is null) return NotFound(new { error = $"unit não encontrada ({criterio})" });
        var unit = unitRow;

        var fromUtc = DateTime.SpecifyKind(dateFrom, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(dateTo, DateTimeKind.Utc);
        var endExclUtc = toUtc.TimeOfDay == TimeSpan.Zero ? toUtc.AddDays(1) : toUtc;

        // Mesma janela do dashboard (LeadService.GetDashboardOverviewAsync:1726-1728):
        // COALESCE(original_created_at, created_at) >= from && < endExcl.
        var inWindow = db.Leads.AsNoTracking()
            .Where(l => l.TenantId == unit.ClinicId
                     && l.UnitId == unit.Id
                     && (l.OriginalCreatedAt ?? l.CreatedAt) >= fromUtc
                     && (l.OriginalCreatedAt ?? l.CreatedAt) <  endExclUtc);

        var total = await inWindow.CountAsync(ct);

        var bySource = await inWindow
            .GroupBy(l => l.Source ?? "(null)")
            .Select(g => new { source = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToListAsync(ct);

        var byStatus = await inWindow
            .GroupBy(l => l.Status ?? "(null)")
            .Select(g => new { status = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToListAsync(ct);

        var deleted = await inWindow.CountAsync(l => l.Status == "deleted", ct);
        var withoutKommoExternal = await inWindow.CountAsync(l => l.ExternalId == 0, ct);
        var fromKommo = await inWindow.CountAsync(l => l.Source == "Kommo" && l.ExternalId != 0 && l.Status != "deleted", ct);

        // Quebra Cadastro × Resgate × Indefinido (mesma lógica do BreakdownsAsync:430-431).
        // IsCadastro: LeadType null/vazio OU contém "cadastro"/"novo".
        // IsResgate: LeadType contém "resgate".
        // Conta só ativos (Status != "deleted") pra bater com o card.
        var ativos = inWindow.Where(l => l.Status != "deleted");
        var byLeadType = await ativos
            .GroupBy(l => l.LeadType ?? "(null)")
            .Select(g => new { lead_type = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToListAsync(ct);
        var cadastros = await ativos.CountAsync(l =>
            l.LeadType == null
            || l.LeadType == ""
            || EF.Functions.ILike(l.LeadType, "%cadastro%")
            || EF.Functions.ILike(l.LeadType, "%novo%"), ct);
        var resgates = await ativos.CountAsync(l =>
            l.LeadType != null && EF.Functions.ILike(l.LeadType, "%resgate%"), ct);
        var outros = await ativos.CountAsync() - cadastros - resgates;

        // ── Quebra pelo campo custom "Tipo lead" (o que as SDRs preenchem na Kommo) ──
        // Responde: "quantos leads têm o campo Tipo lead PREENCHIDO?" e com quais valores.
        // Também lista TODOS os nomes de campo que contêm "tipo" — pra confirmar o nome
        // exato do campo (ex.: "Tipo lead" vs "Tipo" vs "Tipo de agendamento").
        var cfJsons = await ativos.Select(l => l.CustomFieldsJson).ToListAsync(ct);
        var camposTipoNoNome = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var tipoLeadValores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var tipoLeadPreenchido = 0;
        var tipoLeadEmBranco = 0;
        foreach (var json in cfJsons)
        {
            string? tipoLeadValue = null;
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    using var docd = JsonDocument.Parse(json);
                    if (docd.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in docd.RootElement.EnumerateArray())
                        {
                            if (el.ValueKind != JsonValueKind.Object) continue;
                            if (!el.TryGetProperty("field_name", out var n) || n.ValueKind != JsonValueKind.String) continue;
                            var name = n.GetString();
                            if (string.IsNullOrWhiteSpace(name) || !name.ToLowerInvariant().Contains("tipo")) continue;
                            string? val = null;
                            if (el.TryGetProperty("value", out var v))
                            {
                                if (v.ValueKind == JsonValueKind.String) val = v.GetString();
                                else if (v.ValueKind == JsonValueKind.Number) val = v.GetRawText();
                            }
                            if (!string.IsNullOrWhiteSpace(val))
                                camposTipoNoNome[name] = camposTipoNoNome.GetValueOrDefault(name) + 1;
                            // "Tipo lead" = nome contém "lead", ou é exatamente "tipo".
                            var lname = name.ToLowerInvariant();
                            if (lname == "tipo" || lname.Contains("lead")) tipoLeadValue = val;
                        }
                    }
                }
                catch (JsonException) { /* ignora */ }
            }
            if (!string.IsNullOrWhiteSpace(tipoLeadValue))
            {
                tipoLeadPreenchido++;
                var key = tipoLeadValue!.Trim();
                tipoLeadValores[key] = tipoLeadValores.GetValueOrDefault(key) + 1;
            }
            else tipoLeadEmBranco++;
        }

        // Amostras de leads "suspeitos" — os que provavelmente justificam a divergência.
        var suspeitosBase = inWindow
            .Where(l => l.Status == "deleted" || l.ExternalId == 0 || (l.Source != null && l.Source != "Kommo"));

        var suspeitos = await suspeitosBase
            .OrderByDescending(l => l.OriginalCreatedAt ?? l.CreatedAt)
            .Take(50)
            .Select(l => new
            {
                id = l.Id,
                external_id = l.ExternalId,
                name = l.Name,
                phone = l.Phone,
                source = l.Source,
                status = l.Status,
                created_at = l.CreatedAt,
                original_created_at = l.OriginalCreatedAt,
                why = (l.Status == "deleted" ? "deleted" : null)
                   ?? (l.ExternalId == 0 ? "sem-external-id" : null)
                   ?? (l.Source != null && l.Source != "Kommo" ? $"source={l.Source}" : null)
                   ?? "?",
            })
            .ToListAsync(ct);

        return Ok(new
        {
            unit = new { unit.Id, unit.Name },
            window = new { from = fromUtc, to = endExclUtc },
            summary = new
            {
                total_no_banco = total,
                kommo_validos = fromKommo,
                provavel_diferenca = total - fromKommo,
                deletados = deleted,
                sem_external_id_kommo = withoutKommoExternal,
            },
            tipo_ativos = new
            {
                cadastro = cadastros,
                resgate = resgates,
                outros,
                detalhe_lead_type_bruto = byLeadType,
            },
            // Classificação pela COLUNA LeadType (acima) vs pelo CAMPO CUSTOM "Tipo lead" (abaixo).
            // Compare `tipo_lead_campo.preenchido` com o total e com `tipo_ativos.cadastro`.
            tipo_lead_campo = new
            {
                total_ativos = cadastros + resgates + outros,
                preenchido = tipoLeadPreenchido,
                em_branco = tipoLeadEmBranco,
                valores = tipoLeadValores.OrderByDescending(kv => kv.Value)
                    .Select(kv => new { valor = kv.Key, count = kv.Value }),
                campos_com_tipo_no_nome = camposTipoNoNome.OrderByDescending(kv => kv.Value)
                    .Select(kv => new { field_name = kv.Key, preenchidos = kv.Value }),
            },
            by_source = bySource,
            by_status = byStatus,
            suspeitos_amostra = suspeitos,
            hint = "Compare `kommo_validos` com o número da Kommo. `provavel_diferenca` é o overhead. tipo_ativos mostra a quebra Cadastro × Resgate dos ativos.",
        });
    }
}
