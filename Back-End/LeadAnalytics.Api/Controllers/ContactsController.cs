using LeadAnalytics.Api.DTOs.Response;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

[ApiController]
[Route("contacts")]
public class ContactsController(
    ContactService contactService,
    ContactImportService importService,
    ILogger<ContactsController> logger) : ControllerBase
{
    private readonly ContactService _contactService = contactService;
    private readonly ContactImportService _importService = importService;
    private readonly ILogger<ContactsController> _logger = logger;

    private const long MaxFileSize = 50 * 1024 * 1024; // 50 MB

    /// <summary>
    /// Lista contatos (import_csv + webhook_cloudia) com contagens por origem.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ContactsListResponseDto), 200)]
    public async Task<IActionResult> List(
        [FromQuery] int clinicId,
        [FromQuery] string origem = "all",
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string orderBy = "created_at",
        [FromQuery] string orderDir = "desc")
    {
        if (clinicId <= 0)
            return BadRequest(new { error = "clinicId inválido" });

        var result = await _contactService.ListAsync(
            clinicId, origem, search, page, pageSize, orderBy, orderDir, HttpContext.RequestAborted);

        return Ok(result);
    }

    /// <summary>
    /// Detalhe de um contato (importado ou lead webhook).
    /// O id aceita os formatos "c_123" (importado), "l_123" (lead webhook) ou "123" (tenta nos dois).
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ContactDetailDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(string id, [FromQuery] int clinicId)
    {
        if (clinicId <= 0)
            return BadRequest(new { error = "clinicId inválido" });

        var detail = await _contactService.GetByIdAsync(clinicId, id, HttpContext.RequestAborted);
        if (detail is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Contato não encontrado",
                Status = 404,
                Detail = $"Nenhum contato encontrado com id '{id}' para a clínica {clinicId}"
            });
        }

        return Ok(detail);
    }

    /// <summary>
    /// Importa contatos de um arquivo CSV (multipart/form-data).
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(MaxFileSize)]
    [ProducesResponseType(typeof(ContactImportResultDto), 200)]
    public async Task<IActionResult> Import(
        [FromForm] IFormFile file,
        [FromForm] int clinicId,
        [FromForm] string onDuplicate = "skip")
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "arquivo ausente ou vazio" });

        if (file.Length > MaxFileSize)
            return BadRequest(new { error = "arquivo maior que 50 MB" });

        if (clinicId <= 0)
            return BadRequest(new { error = "clinicId inválido" });

        var allowed = new[] { "skip", "update", "fail" };
        if (!allowed.Contains(onDuplicate))
            onDuplicate = "skip";

        _logger.LogInformation(
            "📥 Import solicitado. Tenant={Tenant} File={File} Size={Size}B OnDup={OnDup}",
            clinicId, file.FileName, file.Length, onDuplicate);

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _importService.ImportCsvAsync(
                clinicId, file.FileName, stream, onDuplicate,
                uploadedByUserId: null,
                ct: HttpContext.RequestAborted);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Falha ao importar CSV");
            return StatusCode(500, new { error = "falha ao processar o arquivo", message = ex.Message });
        }
    }
}
