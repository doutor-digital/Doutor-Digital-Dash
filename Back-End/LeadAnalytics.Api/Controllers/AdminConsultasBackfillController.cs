using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Botão "Atualizar" do card Consultas. Roda <see cref="ConsultasBackfillService"/>
/// pra UMA unidade — popula Lead.AppointmentScheduledAt e Lead.ConsultationValue
/// a partir do CustomFieldsJson que já está no banco, usando o mapeamento de
/// "Data de agendamento" / "Valor da consulta" do Perfil do Lead.
///
/// Não chama a Kommo. Resolve o caso "acabei de mapear o campo e quero ver os leads
/// antigos no card agora". Leads novos já são populados pelo webhook ao vivo.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/admin/consultas-backfill")]
public class AdminConsultasBackfillController(
    ConsultasBackfillService backfill,
    ILogger<AdminConsultasBackfillController> logger) : ControllerBase
{
    [HttpPost("{unitId:int}")]
    public async Task<IActionResult> RunForUnit(int unitId, CancellationToken ct)
    {
        logger.LogInformation("[consultas-backfill] manual unit={Unit}", unitId);
        var r = await backfill.BackfillUnitAsync(unitId, ct);
        return Ok(new
        {
            scanned = r.Scanned,
            appointments_set = r.AppointmentsSet,
            values_set = r.ValuesSet,
            error = r.Error,
        });
    }
}
