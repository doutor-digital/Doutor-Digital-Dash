using System.Text.Json;
using LeadAnalytics.Api.DTOs.SavedFilters;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Filtros dinâmicos globais exibidos no topo do dashboard. Qualquer usuário logado
/// lê e aplica; só analista_ti / super_admin cria, edita e remove.
/// </summary>
[ApiController]
[Authorize]
[Route("api/saved-filters")]
public class SavedFiltersController(
    SavedFilterService service,
    ICurrentUser currentUser) : ControllerBase
{
    private readonly SavedFilterService _service = service;
    private readonly ICurrentUser _currentUser = currentUser;

    private IActionResult? RequireAnalyst() =>
        _currentUser.IsAdminLevel
            ? null
            : StatusCode(403, new { message = "Só o analista pode gerenciar filtros." });

    private static SavedFilterItemDto ToDto(Models.SavedFilter r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        Filter = ParseOrEmpty(r.FilterJson),
        SortOrder = r.SortOrder,
        UpdatedByEmail = r.UpdatedByEmail,
        UpdatedAt = r.UpdatedAt,
    };

    /// <summary>Lista todos os filtros salvos (qualquer usuário logado).</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var rows = await _service.ListAsync(ct);
        return Ok(new { items = rows.Select(ToDto).ToList() });
    }

    /// <summary>Cria um filtro salvo (só analista).</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SavedFilterSaveRequestDto body, CancellationToken ct)
    {
        if (RequireAnalyst() is { } denied) return denied;
        if (Validate(body) is { } bad) return bad;

        var item = new SavedFilterSaveItem(body.Name, RawJson(body.Filter), body.SortOrder);
        var row = await _service.CreateAsync(item, _currentUser.Email, ct);
        return Ok(ToDto(row));
    }

    /// <summary>Atualiza um filtro salvo (só analista).</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SavedFilterSaveRequestDto body, CancellationToken ct)
    {
        if (RequireAnalyst() is { } denied) return denied;
        if (Validate(body) is { } bad) return bad;

        var item = new SavedFilterSaveItem(body.Name, RawJson(body.Filter), body.SortOrder);
        var row = await _service.UpdateAsync(id, item, _currentUser.Email, ct);
        if (row is null) return NotFound(new { message = "Filtro não encontrado." });
        return Ok(ToDto(row));
    }

    /// <summary>Remove um filtro salvo (só analista).</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (RequireAnalyst() is { } denied) return denied;
        var ok = await _service.DeleteAsync(id, ct);
        if (!ok) return NotFound(new { message = "Filtro não encontrado." });
        return Ok(new { message = "Filtro removido." });
    }

    private static IActionResult? Validate(SavedFilterSaveRequestDto body)
    {
        // MVC devolve BadRequest via ControllerBase; usamos um helper estático que
        // apenas indica o erro — o caller retorna.
        if (string.IsNullOrWhiteSpace(body.Name))
            return new BadRequestObjectResult(new { message = "O filtro precisa de um nome." });
        if (body.Name.Trim().Length > 120)
            return new BadRequestObjectResult(new { message = "Nome muito longo (máx. 120)." });
        if (body.Filter.ValueKind != JsonValueKind.Object)
            return new BadRequestObjectResult(new { message = "Filtro inválido." });
        return null;
    }

    private static string RawJson(JsonElement el) =>
        el.ValueKind == JsonValueKind.Undefined ? "{}" : el.GetRawText();

    private static JsonElement ParseOrEmpty(string json)
    {
        try { return JsonDocument.Parse(json).RootElement.Clone(); }
        catch { return JsonDocument.Parse("{}").RootElement.Clone(); }
    }
}
