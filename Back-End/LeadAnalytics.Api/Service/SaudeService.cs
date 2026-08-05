using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Saude;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Quão fresco está o dado de cada fonte.
///
/// A MEDIDA É O DADO QUE CHEGOU, NÃO O JOB QUE RODOU
/// -------------------------------------------------
/// Olhar "o job rodou?" não teria pego a falha de julho: o agendamento do n8n rodava a cada
/// 30 minutos, certinho, e as 35 execuções falhavam com 401 na chamada à nossa API. Do lado
/// do agendador estava tudo verde.
///
/// Por isso a medida aqui é o lead mais recente que EXISTE no banco. Se ninguém entrou nas
/// últimas horas, ou a clínica parou de receber lead, ou a ponte quebrou — os dois merecem
/// aparecer na tela.
///
/// OS LIMITES SÃO POR FONTE, E DEPROPÓSITO GENEROSOS
/// -------------------------------------------------
/// Kommo: 3 horas. O sync incremental é de 30 min, mas clínica tem madrugada e domingo;
/// alarme que dispara toda noite é alarme que ninguém olha.
/// Franquia: 24 horas, porque a captura da agenda é diária.
/// </summary>
public class SaudeService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    private const int LimiteKommoMin = 180;
    private const int LimiteFranquiaMin = 1440;

    public async Task<SaudeDto> GetAsync(int tenantId, int? unitId, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var fontes = new List<FonteSaudeDto>();

        // ── Kommo: o lead mais recente que chegou ────────────────────────────
        var ultimoLead = await _db.Leads.AsNoTracking()
            .Where(l => l.TenantId == tenantId && (!unitId.HasValue || l.UnitId == unitId.Value))
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => (DateTime?)l.CreatedAt)
            .FirstOrDefaultAsync(ct);

        fontes.Add(Montar("kommo", "Kommo", ultimoLead, agora, LimiteKommoMin,
            atrasado: "Nenhum lead novo há mais de 3 horas. Pode ser o sync parado — "
                    + "confira as execuções do n8n.",
            nunca: "Nenhum lead sincronizado ainda."));

        // ── Franquia: o snapshot mais recente da agenda ──────────────────────
        var ultimaAgenda = await _db.SpineScheduleSnapshots.AsNoTracking()
            .Where(s => !unitId.HasValue || s.UnitId == unitId.Value)
            .OrderByDescending(s => s.CapturedAt)
            .Select(s => (DateTime?)s.CapturedAt)
            .FirstOrDefaultAsync(ct);

        fontes.Add(Montar("franquia", "Franquia", ultimaAgenda, agora, LimiteFranquiaMin,
            atrasado: "A agenda da franquia não é capturada há mais de um dia.",
            nunca: "Sem token da franquia nesta unidade."));

        // ── Meta Ads: existe conta conectada? ────────────────────────────────
        var conta = await _db.AdAccounts.AsNoTracking()
            .Where(a => a.ClinicId == tenantId && (!unitId.HasValue || a.UnitId == unitId.Value))
            .OrderByDescending(a => a.LastSyncAt)
            .Select(a => new { a.Status, a.LastSyncAt })
            .FirstOrDefaultAsync(ct);

        if (conta is null)
        {
            fontes.Add(new FonteSaudeDto
            {
                Id = "ads",
                Nome = "Meta Ads",
                Status = "desconectado",
                LimiteMinutos = LimiteFranquiaMin,
                Detalhe = "Sem conta conectada — custo por lead e nome de anúncio ficam de fora.",
            });
        }
        else
        {
            fontes.Add(Montar("ads", "Meta Ads", conta.LastSyncAt, agora, LimiteFranquiaMin,
                atrasado: "Os dados de anúncio não são atualizados há mais de um dia.",
                nunca: "Conta conectada, mas nunca sincronizada."));
        }

        return new SaudeDto
        {
            TemAlerta = fontes.Any(f => f.Status != "ok"),
            Fontes = fontes,
        };
    }

    private static FonteSaudeDto Montar(
        string id, string nome, DateTime? quando, DateTime agora, int limite,
        string atrasado, string nunca)
    {
        if (quando is null)
            return new FonteSaudeDto
            {
                Id = id, Nome = nome, Status = "desconectado",
                LimiteMinutos = limite, Detalhe = nunca,
            };

        var minutos = (int)Math.Max(0, (agora - quando.Value).TotalMinutes);
        var ok = minutos <= limite;

        return new FonteSaudeDto
        {
            Id = id,
            Nome = nome,
            Status = ok ? "ok" : "atrasado",
            AtualizadoEm = quando,
            MinutosAtras = minutos,
            LimiteMinutos = limite,
            Detalhe = ok ? null : atrasado,
        };
    }
}
