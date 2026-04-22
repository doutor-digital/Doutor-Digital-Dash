
using LeadAnalytics.Api.DTOs.Response;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

[ApiController]
[Route("api/relatorios")]
public class RelatorioController(IRelatorioService relatorioService) : ControllerBase
{
    /// <summary>
    /// Gera o relatório mensal de leads em PDF para uma clínica.
    /// </summary>
    /// <param name="clinicId">Identificador da clínica (tenant).</param>
    /// <param name="mes">Mês do relatório (1–12).</param>
    /// <param name="ano">Ano do relatório (ex.: 2025).</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpGet("mensal")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterRelatorioMensal(
        [FromQuery] int clinicId,
        [FromQuery] int mes,
        [FromQuery] int ano,
        CancellationToken ct)
    {
        if (clinicId <= 0)
            return BadRequest("clinicId inválido.");

        if (mes < 1 || mes > 12)
            return BadRequest("Mês deve estar entre 1 e 12.");

        if (ano < 2000 || ano > DateTime.UtcNow.Year + 1)
            return BadRequest("Ano inválido.");

        var pdf = await relatorioService.GerarRelatorioMensalAsync(clinicId, mes, ano, ct);

        if (pdf.Length == 0)
            return NotFound("Nenhum dado encontrado para o período informado.");

        var nomeArquivo = $"relatorio_{ano}_{mes:D2}_clinica_{clinicId}.pdf";
        return File(pdf, "application/pdf", nomeArquivo);
    }

    /// <summary>
    /// Retorna o resumo mensal agregado em JSON, utilizado para montar a prévia
    /// e a mensagem de WhatsApp no front-end.
    /// </summary>
    [HttpGet("mensal-resumo")]
    [ProducesResponseType(typeof(RelatorioMensalDadosDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterResumoMensal(
        [FromQuery] int clinicId,
        [FromQuery] int mes,
        [FromQuery] int ano,
        CancellationToken ct)
    {
        if (clinicId <= 0)
            return BadRequest("clinicId inválido.");

        if (mes < 1 || mes > 12)
            return BadRequest("Mês deve estar entre 1 e 12.");

        if (ano < 2000 || ano > DateTime.UtcNow.Year + 1)
            return BadRequest("Ano inválido.");

        var resumo = await relatorioService.ObterResumoMensalAsync(clinicId, mes, ano, ct);

        if (resumo is null)
            return NotFound("Nenhum dado encontrado para o período informado.");

        return Ok(resumo);
    }
}
