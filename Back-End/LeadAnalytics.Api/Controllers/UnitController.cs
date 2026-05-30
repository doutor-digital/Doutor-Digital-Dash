using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Units;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// CRUD das unidades (clínicas/filiais). Cada unidade é um tenant isolado e tem uma
/// URL de webhook própria da Kommo (<c>/webhooks/kommo/{slug}</c>), retornada em
/// <see cref="UnitDto.WebhookUrl"/> para o usuário colar nas configurações da Kommo.
/// </summary>
[ApiController]
[Authorize]
[Route("units")]
public class UnitController(
    UnitService unitService,
    IConfiguration configuration,
    IWebHostEnvironment env,
    AppDbContext db,
    KommoSyncService kommoSync) : ControllerBase
{
    private readonly UnitService _unitService = unitService;
    private readonly IConfiguration _configuration = configuration;
    private readonly IWebHostEnvironment _env = env;
    private readonly AppDbContext _db = db;
    private readonly KommoSyncService _kommoSync = kommoSync;

    private static readonly string[] AllowedPhotoExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
    private const long MaxPhotoBytes = 5 * 1024 * 1024; // 5 MB

    /// <summary>
    /// Base pública usada para montar a URL do webhook (config <c>Webhook:PublicBaseUrl</c>
    /// ou host atual). Força <c>https://</c> quando o host não é localhost porque a Kommo
    /// não consegue entregar via <c>http://</c> em hosts atrás de proxy com 301 redirect
    /// (Railway, Cloudflare, etc.) — o POST body se perde no redirect.
    /// </summary>
    private string BaseUrl()
    {
        var configured = _configuration["Webhook:PublicBaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.TrimEnd('/');

        var host = Request.Host.Host;
        var isLocal = string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                   || host.StartsWith("127.")
                   || host == "::1";
        var scheme = isLocal ? Request.Scheme : "https";
        return $"{scheme}://{Request.Host}";
    }

    /// <summary>Lista todas as unidades (com URL do webhook e contagem de leads).</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _unitService.ListDtosAsync(BaseUrl(), ct));

    /// <summary>Detalhe de uma unidade pelo Id.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var unit = await _unitService.GetDtoByIdAsync(id, BaseUrl(), ct);
        return unit is null ? NotFound() : Ok(unit);
    }

    /// <summary>Cria uma nova unidade (botão "+"). Retorna a unidade com a URL do webhook gerada.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUnitDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var created = await _unitService.CreateAsync(dto, BaseUrl(), ct);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>Atualiza dados/cadastro/configurações de uma unidade.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUnitDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var updated = await _unitService.UpdateAsync(id, dto, BaseUrl(), ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>Remove uma unidade (apenas se não houver leads vinculados).</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        try
        {
            var removed = await _unitService.DeleteAsync(id, ct);
            return removed ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Upload da foto/logo da unidade. Aceita multipart/form-data com o campo "file"
    /// (jpg/png/webp, max 5MB). Retorna { url } absoluto para colar em PhotoUrl
    /// no POST/PUT /units. A foto é hospedada na própria API em /uploads/units/.
    /// </summary>
    [HttpPost("upload-photo")]
    [RequestSizeLimit(MaxPhotoBytes)]
    public async Task<IActionResult> UploadPhoto(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Envie um arquivo no campo 'file'." });

        if (file.Length > MaxPhotoBytes)
            return BadRequest(new { message = "Foto excede 5 MB." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || !AllowedPhotoExtensions.Contains(ext))
            return BadRequest(new { message = "Formato inválido. Use jpg, png ou webp." });

        var webRoot = string.IsNullOrEmpty(_env.WebRootPath)
            ? Path.Combine(_env.ContentRootPath, "wwwroot")
            : _env.WebRootPath;

        var folder = Path.Combine(webRoot, "uploads", "units");
        Directory.CreateDirectory(folder);

        var fileName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(folder, fileName);

        await using (var stream = System.IO.File.Create(fullPath))
            await file.CopyToAsync(stream, ct);

        var url = $"{BaseUrl()}/uploads/units/{fileName}";
        return Ok(new { url });
    }

    /// <summary>
    /// Puxa leads/contatos existentes da Kommo (paginação 250/página, cap default
    /// 5000) e ingere pelo mesmo pipeline do webhook — idempotente. Usa o token
    /// do body OU o salvo na unidade.
    /// </summary>
    [HttpPost("{id:int}/sync-from-kommo")]
    [ProducesResponseType(typeof(KommoSyncResponseDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> SyncFromKommo(
        int id, [FromBody] KommoSyncRequestDto body, CancellationToken ct)
    {
        var unit = await _db.Units.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (unit is null) return NotFound();

        var token = !string.IsNullOrWhiteSpace(body.AccessToken)
            ? body.AccessToken.Trim()
            : unit.KommoAccessToken;

        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { message = "Informe um AccessToken da Kommo (ou salve um na unidade)." });

        if (string.IsNullOrWhiteSpace(unit.KommoSubdomain))
            return BadRequest(new { message = "A unidade precisa ter um KommoSubdomain configurado (ex.: minhaclinica.kommo.com)." });

        // Salva o token na unidade se o caller pediu (default true) e veio body.AccessToken
        if (body.PersistToken && !string.IsNullOrWhiteSpace(body.AccessToken) && unit.KommoAccessToken != token)
        {
            unit.KommoAccessToken = token;
            unit.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        var max = body.MaxLeads.HasValue ? Math.Clamp(body.MaxLeads.Value, 1, 20000) : 5000;

        // Desacopla o cancelamento do request HTTP — se o cliente do front
        // desconectar (timeout 30s do axios), o sync continua até 10min.
        // Importante porque sync de 5k leads paga + busca contatos pode
        // levar 1-3min e queremos rodar até o fim mesmo offline.
        using var syncCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var syncCt = syncCts.Token;

        try
        {
            var result = await _kommoSync.SyncAsync(unit, token, max, syncCt);
            return Ok(new KommoSyncResponseDto
            {
                Success = string.IsNullOrEmpty(result.Error),
                Error = result.Error,
                PagesFetched = result.PagesFetched,
                LeadsFetched = result.LeadsFetched,
                ContactsFetched = result.ContactsFetched,
                LeadsPersisted = result.LeadsPersisted,
                DurationMs = result.DurationMs,
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return BadRequest(new KommoSyncResponseDto { Success = false, Error = ex.Message });
        }
    }

    [HttpGet("quantity-leads")]
    public async Task<IActionResult> GetQuantityLeadsUnit(int clinicId)
    {
        var units = await _unitService.GetQuantityLeadsUnit(clinicId);
        if (units is null)
            return NotFound();
        return Ok(units);
    }

    // ─── Compatibilidade com o comportamento anterior (por ClinicId) ──────

    [HttpGet("by-clinic/{clinicId:int}")]
    public async Task<IActionResult> GetByClinicId(int clinicId)
    {
        var unit = await _unitService.GetOrCreateAsync(clinicId);
        return unit is null ? NotFound() : Ok(unit);
    }

    [HttpPut("by-clinic/{clinicId:int}")]
    public async Task<IActionResult> RenameByClinic(int clinicId, [FromBody] string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Nome inválido" });

        var unit = await _unitService.RenameAsync(clinicId, name);
        return Ok(unit);
    }
}
