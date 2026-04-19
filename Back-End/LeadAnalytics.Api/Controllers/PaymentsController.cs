using LeadAnalytics.Api.DTOs.Response;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

[ApiController]
[Route("payments")]
public class PaymentsController(
    PaymentService paymentService,
    ILogger<PaymentsController> logger) : ControllerBase
{
    private readonly PaymentService _paymentService = paymentService;
    private readonly ILogger<PaymentsController> _logger = logger;

    /// <summary>
    /// Lista os tratamentos disponíveis para seleção (catálogo).
    /// </summary>
    [HttpGet("treatments")]
    [ProducesResponseType(typeof(IEnumerable<TreatmentOptionDto>), 200)]
    public IActionResult GetTreatments() => Ok(TreatmentCatalog.Options);

    /// <summary>
    /// Registra o pagamento de um lead (tratamento, forma, entrada, parcelas).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(PaymentResponseDto), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Create([FromBody] PaymentCreateDto dto)
    {
        if (dto is null) return BadRequest(new { error = "payload obrigatório" });

        try
        {
            var created = await _paymentService.CreateAsync(dto, HttpContext.RequestAborted);
            return CreatedAtAction(nameof(ListByLead),
                new { leadId = created.LeadId, clinicId = dto.ClinicId },
                created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao registrar pagamento");
            return StatusCode(500, new { error = "falha ao registrar pagamento", message = ex.Message });
        }
    }

    /// <summary>
    /// Lista pagamentos de um lead.
    /// </summary>
    [HttpGet("lead/{leadId:int}")]
    [ProducesResponseType(typeof(IEnumerable<PaymentResponseDto>), 200)]
    public async Task<IActionResult> ListByLead(int leadId, [FromQuery] int clinicId)
    {
        if (clinicId <= 0) return BadRequest(new { error = "clinicId inválido" });
        if (leadId <= 0) return BadRequest(new { error = "leadId inválido" });

        var list = await _paymentService.ListByLeadAsync(clinicId, leadId, HttpContext.RequestAborted);
        return Ok(list);
    }

    /// <summary>
    /// Lista pagamentos de uma clínica com filtros de período, tratamento e método.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<PaymentResponseDto>), 200)]
    public async Task<IActionResult> List(
        [FromQuery] int clinicId,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] string? treatment = null,
        [FromQuery] string? method = null)
    {
        if (clinicId <= 0) return BadRequest(new { error = "clinicId inválido" });

        var list = await _paymentService.ListByClinicAsync(
            clinicId, dateFrom, dateTo, treatment, method, HttpContext.RequestAborted);
        return Ok(list);
    }

    /// <summary>
    /// Retorna o faturamento total por unidade com base nos pagamentos registrados.
    /// </summary>
    [HttpGet("revenue/by-unit")]
    [ProducesResponseType(typeof(RevenueSummaryDto), 200)]
    public async Task<IActionResult> RevenueByUnit(
        [FromQuery] int? clinicId = null,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null)
    {
        var summary = await _paymentService.GetRevenueByUnitAsync(
            clinicId, dateFrom, dateTo, HttpContext.RequestAborted);
        return Ok(summary);
    }

    /// <summary>
    /// Remove um pagamento registrado.
    /// </summary>
    [HttpDelete("{paymentId:int}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(int paymentId, [FromQuery] int clinicId)
    {
        if (clinicId <= 0) return BadRequest(new { error = "clinicId inválido" });

        var ok = await _paymentService.DeleteAsync(clinicId, paymentId, HttpContext.RequestAborted);
        if (!ok) return NotFound(new { error = $"pagamento '{paymentId}' não encontrado" });
        return NoContent();
    }
}
