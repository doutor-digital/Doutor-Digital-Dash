using System.Text.Json;
using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Kpi;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Configurações Técnicas do dashboard: mapeia cada KPI da tela principal para a sua
/// fonte de dados (etapa da Kommo, campo customizado, filtro combinado). Restrito a
/// analista_ti / super_admin — é a área "que só o analista vê".
/// </summary>
[ApiController]
[Authorize]
[Route("api/config/kpis")]
public class KpiConfigController(
    KpiConfigService kpiService,
    TenantUnitGuard tenantGuard,
    ICurrentUser currentUser,
    AppDbContext db) : ControllerBase
{
    private readonly KpiConfigService _kpiService = kpiService;
    private readonly TenantUnitGuard _tenantGuard = tenantGuard;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly AppDbContext _db = db;

    private IActionResult? RequireAnalyst() =>
        _currentUser.IsAdminLevel
            ? null
            : StatusCode(403, new { message = "Acesso restrito ao analista de TI." });

    /// <summary>Catálogo dos KPIs que podem ser mapeados (chave + rótulo amigável).</summary>
    [HttpGet("catalog")]
    public IActionResult GetCatalog()
    {
        if (RequireAnalyst() is { } denied) return denied;
        var items = KpiCatalog.Items
            .Select(i => new KpiCatalogItemDto { Key = i.Key, Label = i.Label, Description = i.Description })
            .ToList();
        return Ok(new { items, source_types = KpiSourceTypes.All });
    }

    /// <summary>Mapeamentos salvos de uma unidade.</summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int unitId, CancellationToken ct)
    {
        if (RequireAnalyst() is { } denied) return denied;
        if (await _tenantGuard.EnsureUnitBelongsToTenantAsync(unitId, ct) is { } guard) return guard;

        var rows = await _kpiService.GetForUnitAsync(unitId, ct);
        var items = rows.Select(r => new KpiConfigItemDto
        {
            KpiKey = r.KpiKey,
            SourceType = r.SourceType,
            Config = ParseOrEmpty(r.ConfigJson),
            IsCustom = r.IsCustom,
            DisplayName = r.DisplayName,
            AccentColor = r.AccentColor,
            DisplayType = string.IsNullOrWhiteSpace(r.DisplayType) ? "number" : r.DisplayType,
            SortOrder = r.SortOrder,
            UpdatedByEmail = r.UpdatedByEmail,
            UpdatedAt = r.UpdatedAt,
        }).ToList();

        return Ok(new { items });
    }

    /// <summary>Salva (upsert) os mapeamentos de uma unidade.</summary>
    [HttpPut]
    public async Task<IActionResult> Save(
        [FromQuery] int unitId,
        [FromBody] KpiConfigSaveRequestDto body,
        CancellationToken ct)
    {
        if (RequireAnalyst() is { } denied) return denied;
        if (await _tenantGuard.EnsureUnitBelongsToTenantAsync(unitId, ct) is { } guard) return guard;

        var unit = await _db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit is null) return NotFound(new { message = "Unidade não encontrada." });

        var prepared = new List<KpiSaveItem>();
        foreach (var item in body.Items ?? new())
        {
            // KPI custom usa chave gerada (não está no catálogo); o fixo precisa ser válido.
            if (!item.IsCustom && !KpiCatalog.IsValidKey(item.KpiKey))
                return BadRequest(new { message = $"KPI inválido: {item.KpiKey}" });
            if (item.IsCustom && string.IsNullOrWhiteSpace(item.KpiKey))
                return BadRequest(new { message = "KPI custom precisa de uma chave." });
            if (item.IsCustom && string.IsNullOrWhiteSpace(item.DisplayName))
                return BadRequest(new { message = "KPI custom precisa de um nome." });
            if (!KpiSourceTypes.IsValid(item.SourceType))
                return BadRequest(new { message = $"Tipo de fonte inválido: {item.SourceType}" });

            var displayType = string.IsNullOrWhiteSpace(item.DisplayType) ? "number" : item.DisplayType;
            // O gráfico de origens precisa de um campo customizado pra distribuir os valores.
            if (displayType == "source_chart"
                && !(item.Config.ValueKind == JsonValueKind.Object
                     && item.Config.TryGetProperty("fieldId", out _)))
                return BadRequest(new { message = "Gráfico de origens precisa de um campo customizado (fieldId)." });

            var configJson = item.Config.ValueKind == JsonValueKind.Undefined
                ? "{}"
                : item.Config.GetRawText();
            prepared.Add(new KpiSaveItem(
                item.KpiKey, item.SourceType, configJson,
                item.IsCustom, item.DisplayName, item.AccentColor, displayType, item.SortOrder));
        }

        await _kpiService.SaveAsync(unitId, unit.ClinicId, prepared, _currentUser.Email, ct);
        return Ok(new { message = "Configurações salvas.", count = prepared.Count });
    }

    /// <summary>Lê o mapeamento de campos do Perfil do Lead (nascimento/agendamento/doutor + breakdowns).</summary>
    [HttpGet("lead-profile")]
    public async Task<IActionResult> GetLeadProfileConfig([FromQuery] int unitId, CancellationToken ct)
    {
        if (RequireAnalyst() is { } denied) return denied;
        if (await _tenantGuard.EnsureUnitBelongsToTenantAsync(unitId, ct) is { } guard) return guard;

        var f = await _kpiService.GetLeadProfileConfigAsync(unitId, ct);
        return Ok(new LeadProfileConfigDto
        {
            BirthdateFieldId = f.BirthdateFieldId,
            AppointmentFieldId = f.AppointmentFieldId,
            DoctorFieldId = f.DoctorFieldId,
            OrigemFieldId = f.OrigemFieldId,
            MotivoNaoAgendamentoFieldId = f.MotivoNaoAgendamentoFieldId,
            FisioterapeutaFieldId = f.FisioterapeutaFieldId,
            ValorTratamentoFieldId = f.ValorTratamentoFieldId,
            ValorConsultaFieldId = f.ValorConsultaFieldId,
            TratamentoFechadoFieldId = f.TratamentoFechadoFieldId,
            QualificacaoFieldId = f.QualificacaoFieldId,
            TipoFieldId = f.TipoFieldId,
        });
    }

    /// <summary>Salva o mapeamento de campos do Perfil do Lead.</summary>
    [HttpPut("lead-profile")]
    public async Task<IActionResult> SaveLeadProfileConfig(
        [FromQuery] int unitId, [FromBody] LeadProfileConfigDto body, CancellationToken ct)
    {
        if (RequireAnalyst() is { } denied) return denied;
        if (await _tenantGuard.EnsureUnitBelongsToTenantAsync(unitId, ct) is { } guard) return guard;

        var unit = await _db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit is null) return NotFound(new { message = "Unidade não encontrada." });

        await _kpiService.SaveLeadProfileConfigAsync(
            unitId, unit.ClinicId,
            new KpiConfigService.LeadProfileFields
            {
                BirthdateFieldId = body.BirthdateFieldId,
                AppointmentFieldId = body.AppointmentFieldId,
                DoctorFieldId = body.DoctorFieldId,
                OrigemFieldId = body.OrigemFieldId,
                MotivoNaoAgendamentoFieldId = body.MotivoNaoAgendamentoFieldId,
                FisioterapeutaFieldId = body.FisioterapeutaFieldId,
                ValorTratamentoFieldId = body.ValorTratamentoFieldId,
                ValorConsultaFieldId = body.ValorConsultaFieldId,
                TratamentoFechadoFieldId = body.TratamentoFechadoFieldId,
                QualificacaoFieldId = body.QualificacaoFieldId,
                TipoFieldId = body.TipoFieldId,
            },
            _currentUser.Email, ct);
        return Ok(new { message = "Configuração salva." });
    }

    /// <summary>Remove um KPI (custom) de uma unidade.</summary>
    [HttpDelete("{kpiKey}")]
    public async Task<IActionResult> Delete(
        [FromQuery] int unitId,
        string kpiKey,
        CancellationToken ct)
    {
        if (RequireAnalyst() is { } denied) return denied;
        if (await _tenantGuard.EnsureUnitBelongsToTenantAsync(unitId, ct) is { } guard) return guard;

        var removed = await _kpiService.DeleteAsync(unitId, kpiKey, ct);
        return removed
            ? Ok(new { message = "KPI removido." })
            : NotFound(new { message = "KPI não encontrado." });
    }

    /// <summary>Calcula o número de um KPI ao vivo, para pré-visualizar antes de salvar.</summary>
    [HttpPost("preview")]
    public async Task<IActionResult> Preview(
        [FromQuery] int unitId,
        [FromBody] KpiPreviewRequestDto body,
        CancellationToken ct)
    {
        if (RequireAnalyst() is { } denied) return denied;
        if (await _tenantGuard.EnsureUnitBelongsToTenantAsync(unitId, ct) is { } guard) return guard;

        if (!KpiSourceTypes.IsValid(body.SourceType))
            return BadRequest(new { message = $"Tipo de fonte inválido: {body.SourceType}" });

        var unit = await _db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit is null) return NotFound(new { message = "Unidade não encontrada." });

        var to = body.DateTo ?? DateTime.UtcNow;
        var from = body.DateFrom ?? to.AddDays(-30);
        if (to < from) return BadRequest(new { error = "date_to deve ser >= date_from" });

        var (value, sample, note) = await _kpiService.ComputeAsync(
            unit.ClinicId, unitId, body.SourceType, body.Config, from, to, ct: ct);

        return Ok(new KpiPreviewResponseDto { Value = value, SampleSize = sample, Note = note });
    }

    private static JsonElement ParseOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) json = "{}";
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch (JsonException) { return JsonSerializer.Deserialize<JsonElement>("{}"); }
    }
}
