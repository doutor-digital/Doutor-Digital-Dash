using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Spine;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service.Spine;

/// <summary>
/// Comparativo da rede: roda o card de avaliações para cada unidade que já
/// conectou o Doutor Hérnia e devolve um ranking. É o recurso que se vende ao
/// franqueador master — ele monitora todas as unidades numa tela.
///
/// As unidades são consultadas EM PARALELO, com isolamento de falha: se uma tem
/// token revogado, ela entra no resultado com o campo Erro preenchido em vez de
/// derrubar o comparativo inteiro.
/// </summary>
public class SpineRedeService(
    AppDbContext db,
    SpineTokenStore tokens,
    SpineAvaliacoesService avaliacoes,
    ILogger<SpineRedeService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly SpineTokenStore _tokens = tokens;
    private readonly SpineAvaliacoesService _avaliacoes = avaliacoes;
    private readonly ILogger<SpineRedeService> _logger = logger;

    /// <param name="tenantId">null = super admin, vê todas as unidades ativas.</param>
    public async Task<SpineRedeDto> ComparativoAsync(
        int? tenantId, DateOnly de, DateOnly ate, CancellationToken ct = default)
    {
        var q = _db.Units.AsNoTracking().Where(u => u.IsActive);
        if (tenantId.HasValue) q = q.Where(u => u.ClinicId == tenantId.Value);
        var unidades = await q.Select(u => new { u.Id, u.Name }).OrderBy(u => u.Name).ToListAsync(ct);

        var comToken = new List<(int Id, string Nome)>();
        var semToken = new List<SpineRedeSemTokenDto>();
        foreach (var u in unidades)
        {
            var token = await _tokens.GetTokenAsync(u.Id, ct);
            if (string.IsNullOrWhiteSpace(token)) semToken.Add(new SpineRedeSemTokenDto(u.Id, u.Name));
            else comToken.Add((u.Id, u.Name));
        }

        // Consulta as unidades conectadas em paralelo (uma chamada ao Spine cada).
        var linhas = await Task.WhenAll(comToken.Select(async u =>
        {
            try
            {
                var dto = await _avaliacoes.GetAsync(u.Id, de, ate, ct);
                if (dto is null)
                    return new SpineRedeUnidadeDto(u.Id, u.Nome, 0, 0, 0, 0, 0, 0, "sem token");
                int Sit(int idStatus) => dto.PorSituacao.FirstOrDefault(s => s.IdStatus == idStatus)?.Total ?? 0;
                return new SpineRedeUnidadeDto(
                    u.Id, u.Nome, dto.Total, dto.Realizadas,
                    Sit(SpineApiClient.ScheduleStatus.NaoCompareceu),
                    Sit(SpineApiClient.ScheduleStatus.Desmarcado),
                    dto.TaxaComparecimento, dto.PacientesDistintos, null);
            }
            catch (SpineApiException ex)
            {
                _logger.LogWarning(ex, "Comparativo: unidade {UnitId} falhou", u.Id);
                return new SpineRedeUnidadeDto(u.Id, u.Nome, 0, 0, 0, 0, 0, 0, ex.Motivo);
            }
        }));

        var ok = linhas.Where(l => l.Erro is null).ToList();
        var totAg = ok.Sum(l => l.Agendadas);
        var totComp = ok.Sum(l => l.Compareceram);
        var totais = new SpineRedeTotaisDto(
            ok.Count, totAg, totComp,
            totAg == 0 ? 0 : Math.Round((double)totComp / totAg * 100, 1));

        // Ranking: melhor comparecimento primeiro; sem token/erro vão pro fim.
        var ordenadas = linhas
            .OrderByDescending(l => l.Erro is null)
            .ThenByDescending(l => l.TaxaComparecimento)
            .ToList();

        return new SpineRedeDto(de, ate, ordenadas, semToken, totais);
    }
}
