using LeadAnalytics.Api.DTOs.Filter;
using LeadAnalytics.Api.DTOs.Response;
using LeadAnalytics.Api.Service;
using LeadAnalytics.Api.Service.Filtering;
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
    /// Lista contatos (import_csv + webhook_cloudia + manual) com contagens por origem e por ação.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ContactsListResponseDto), 200)]
    public async Task<IActionResult> List(
        [FromQuery] int clinicId,
        [FromQuery] string origem = "all",
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] string? etapa = null,
        [FromQuery] string? tag = null,
        [FromQuery] bool? blocked = null,
        [FromQuery] bool? hasConsultation = null,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string orderBy = "created_at",
        [FromQuery] string orderDir = "desc")
    {
        if (clinicId <= 0)
            return BadRequest(new { error = "clinicId inválido" });

        var filters = new ContactFiltersDto
        {
            Origem = origem,
            Search = search,
            Status = status,
            Etapa = etapa,
            Tag = tag,
            Blocked = blocked,
            HasConsultation = hasConsultation,
            DateFrom = dateFrom,
            DateTo = dateTo,
        };

        var result = await _contactService.ListAsync(
            clinicId, filters, page, pageSize, orderBy, orderDir, HttpContext.RequestAborted);

        return Ok(result);
    }

    /// <summary>
    /// Detalhe de um contato (importado, manual ou lead webhook).
    /// O id aceita os formatos "c_123" (contato), "l_123" (lead) ou "123" (tenta nos dois).
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
    /// Cria um contato manualmente (origem = "manual").
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ContactDetailDto), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Create([FromBody] ContactCreateDto dto)
    {
        if (dto is null || dto.ClinicId <= 0)
            return BadRequest(new { error = "clinic_id inválido" });

        try
        {
            var created = await _contactService.CreateAsync(dto, HttpContext.RequestAborted);
            return CreatedAtAction(
                nameof(GetById),
                new { id = created.Id, clinicId = dto.ClinicId },
                created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao criar contato manualmente");
            return StatusCode(500, new { error = "falha ao criar contato", message = ex.Message });
        }
    }

    /// <summary>
    /// Atualiza a ação (compareceu, faltou, aguardando) de um contato ou lead.
    /// </summary>
    [HttpPatch("{id}/action")]
    [ProducesResponseType(typeof(ContactDetailDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> SetAction(
        string id,
        [FromQuery] int clinicId,
        [FromBody] ContactActionDto dto)
    {
        if (clinicId <= 0) return BadRequest(new { error = "clinicId inválido" });
        if (dto is null || string.IsNullOrWhiteSpace(dto.Action))
            return BadRequest(new { error = "ação obrigatória (compareceu | faltou | aguardando)" });

        try
        {
            var result = await _contactService.SetActionAsync(
                clinicId, id, dto.Action, dto.ConsultationAt, dto.Observacoes, HttpContext.RequestAborted);

            if (result is null)
                return NotFound(new { error = $"contato '{id}' não encontrado" });

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Busca avançada com DSL de filtros (POST para suportar payload grande).
    /// </summary>
    [HttpPost("search")]
    [ProducesResponseType(typeof(ContactSearchResponseDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(501)]
    public async Task<IActionResult> Search(
        [FromQuery] int clinicId,
        [FromBody] ContactSearchRequestDto req)
    {
        if (clinicId <= 0)
            return BadRequest(new { error = "clinicId inválido" });
        if (req is null)
            return BadRequest(new { error = "payload obrigatório" });
        if (req.PageSize > 200)
            return BadRequest(new { error = "page_size máximo é 200" });

        try
        {
            _logger.LogInformation(
                "🔎 Search contacts tenant={Tenant} filters={Count} origem={Origem} page={Page}",
                clinicId, req.Filters?.Count ?? 0, req.Origem, req.Page);

            var result = await _contactService.SearchAsync(clinicId, req, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (FilterValidationException ex)
        {
            return BadRequest(new { error = ex.Message, field = ex.Field });
        }
        catch (FilterNotImplementedException ex)
        {
            return StatusCode(501, new
            {
                error = ex.Message,
                field = ex.Field,
                hint = "o campo está reconhecido pela whitelist mas ainda não tem coluna de backing no schema atual"
            });
        }
    }

    /// <summary>
    /// Opções dinâmicas para multiselects do painel de filtros.
    /// Chaves suportadas hoje: tags, etapas, conexoes.
    /// </summary>
    [HttpGet("filter-options/{key}")]
    [ProducesResponseType(typeof(FilterOptionsResponseDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> FilterOptions(
        string key,
        [FromQuery] int clinicId,
        [FromQuery] string? search = null,
        [FromQuery] int limit = 50)
    {
        if (clinicId <= 0)
            return BadRequest(new { error = "clinicId inválido" });

        var result = await _contactService.GetFilterOptionsAsync(
            clinicId, key, search, limit, HttpContext.RequestAborted);

        if (result is null)
            return NotFound(new
            {
                error = $"opções '{key}' não suportadas",
                supported = ContactService.SupportedOptionKeys.OrderBy(x => x).ToArray()
            });

        return Ok(result);
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
            "Import solicitado. Tenant={Tenant} File={File} Size={Size}B OnDup={OnDup}",
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
            _logger.LogError(ex, "Falha ao importar CSV");
            return StatusCode(500, new { error = "falha ao processar o arquivo", message = ex.Message });
        }
    }
}
