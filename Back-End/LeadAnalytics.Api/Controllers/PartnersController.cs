using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Partners;
using LeadAnalytics.Api.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Controllers;

/// <summary>
/// Painel de Parceiros: visão consolidada de TODAS as unidades parceiras lado a lado.
///
/// É uma visão cruzada (ignora o tenant da sessão), por isso é restrita a nível
/// administrativo — um usuário de unidade não deve enxergar os números dos outros
/// parceiros.
/// </summary>
[ApiController]
[Authorize]
[Route("partners")]
public class PartnersController(AppDbContext db, ICurrentUser currentUser) : ControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly ICurrentUser _currentUser = currentUser;

    /// <summary>
    /// Lista os parceiros com cadastro, estado da integração Kommo e números agregados.
    /// As métricas saem de UMA query agrupada por TenantId (não é N+1 por unidade).
    /// </summary>
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken ct)
    {
        if (!_currentUser.IsAdminLevel)
            return Forbid();

        var now = DateTime.UtcNow;
        var d30 = now.AddDays(-30);
        var d7 = now.AddDays(-7);

        var units = await _db.Units
            .AsNoTracking()
            .OrderBy(u => u.Name)
            .ToListAsync(ct);

        // Agregado único por tenant — evita uma query por parceiro.
        var stats = await _db.Leads
            .AsNoTracking()
            .GroupBy(l => l.TenantId)
            .Select(g => new
            {
                TenantId = g.Key,
                Total = g.Count(),
                L30 = g.Count(x => x.CreatedAt >= d30),
                L7 = g.Count(x => x.CreatedAt >= d7),
                Agendados = g.Count(x =>
                    x.CurrentStage == LeadStages.AgendadoSemPagamento ||
                    x.CurrentStage == LeadStages.AgendadoComPagamento),
                Fechados = g.Count(x =>
                    x.CurrentStage == LeadStages.FechouTratamento ||
                    x.CurrentStage == LeadStages.EmTratamento),
                Faturamento = g.Sum(x =>
                    (x.CurrentStage == LeadStages.FechouTratamento ||
                     x.CurrentStage == LeadStages.EmTratamento)
                        ? (x.Price ?? 0m) : 0m),
                LastLeadAt = g.Max(x => (DateTime?)x.CreatedAt),
            })
            .ToListAsync(ct);

        var byTenant = stats.ToDictionary(s => s.TenantId);

        var result = units.Select(u =>
        {
            byTenant.TryGetValue(u.ClinicId, out var s);

            var last = s?.LastLeadAt;
            int? daysSince = last is null
                ? null
                : (int)Math.Floor((now - last.Value).TotalDays);

            return new PartnerOverviewDto
            {
                Id = u.Id,
                ClinicId = u.ClinicId,
                Name = u.Name,
                Slug = u.Slug,
                Segment = u.Segment,
                City = u.City,
                State = u.State,
                PhotoUrl = u.PhotoUrl,
                ResponsibleName = u.ResponsibleName,
                IsActive = u.IsActive,

                KommoSubdomain = u.KommoSubdomain,
                HasKommoToken = !string.IsNullOrWhiteSpace(u.KommoAccessToken),
                HasStageMap = !string.IsNullOrWhiteSpace(u.KommoStageMapJson)
                              && u.KommoStageMapJson != "{}",

                TotalLeads = s?.Total ?? 0,
                Leads30d = s?.L30 ?? 0,
                Leads7d = s?.L7 ?? 0,
                Agendados = s?.Agendados ?? 0,
                Fechados = s?.Fechados ?? 0,
                Faturamento = s?.Faturamento ?? 0m,
                LastLeadAt = last,
                DaysSinceLastLead = daysSince,
            };
        })
        .OrderByDescending(p => p.Leads30d)
        .ThenBy(p => p.Name)
        .ToList();

        return Ok(result);
    }

    /// <summary>
    /// Vitrine PÚBLICA de parceiros (sem login) — alimenta o painel externo em
    /// <c>parceiros.doutordigitalconsultoria.com</c>, usado para apresentar a base
    /// de parceiros a novos clientes.
    ///
    /// Expõe de propósito APENAS dados de identificação (nome, cidade, segmento,
    /// logo) e números GLOBAIS agregados. Faturamento e volume de leads por parceiro
    /// são confidenciais de cada cliente e nunca saem por aqui.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("public")]
    public async Task<IActionResult> PublicShowcase(CancellationToken ct)
    {
        var units = await _db.Units
            .AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.Name)
            .Select(u => new
            {
                name = u.Name,
                city = u.City,
                state = u.State,
                segment = u.Segment,
                logo = u.PhotoUrl,
            })
            .ToListAsync(ct);

        // Número global — prova social, não identifica nenhum parceiro.
        var totalLeads = await _db.Leads.AsNoTracking().CountAsync(ct);

        return Ok(new
        {
            partners = units,
            totals = new
            {
                partners = units.Count,
                states = units
                    .Where(u => !string.IsNullOrWhiteSpace(u.state))
                    .Select(u => u.state!)
                    .Distinct()
                    .Count(),
                leads = totalLeads,
            },
        });
    }
}
