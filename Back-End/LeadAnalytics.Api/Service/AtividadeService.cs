using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Saude;
using LeadAnalytics.Api.Models;
using LeadAnalytics.Api.Service.Spine;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// O que aconteceu no CRM, na ordem em que aconteceu.
///
/// POR QUE UM LOG, SE JÁ EXISTEM OS NÚMEROS
/// ----------------------------------------
/// Todo card desta página é uma contagem, e contagem esconde a história: "22 leads" não diz
/// que 14 chegaram entre 10h e 11h e depois o dia morreu. O log é a prova bruta por trás de
/// cada número — e é a única parte do dashboard que alguém consegue deixar aberta numa tela
/// da clínica o dia inteiro.
///
/// FICA NO FIM DA PÁGINA, DE PROPÓSITO
/// -----------------------------------
/// No topo ele competiria com as filas, e perderia: fila pede ação, log só informa. No fim,
/// depois dos números, ele é o que sustenta o que veio antes.
///
/// DE ONDE VÊM AS LINHAS
/// ---------------------
/// Não existe tabela de "eventos do dashboard" e não vale criar uma: tudo o que o log mostra
/// já está gravado em coluna com data. Quatro fontes, todas reais:
///
/// • lead    — <see cref="Lead.CreatedAt"/>, o lead entrando
/// • etapa   — <see cref="LeadStageHistory"/>, a mudança de etapa (só linhas com data
///             confiável: webhook ou API de eventos; legado tem a data do último sync)
/// • agenda  — <see cref="Lead.AppointmentScheduledAtFilledAt"/>, quando a SDR preencheu a data
/// • campo   — <see cref="Lead.QualificationFilledAt"/>, quando a qualificação foi preenchida
/// </summary>
public class AtividadeService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    /// <summary>Teto de linhas devolvidas — a tela mostra uma janela, não um arquivo.</summary>
    private const int TetoLinhas = 60;

    /// <summary>Janela do log. Além disso vira histórico, e histórico é outra tela.</summary>
    private static readonly TimeSpan Janela = TimeSpan.FromHours(24);

    public async Task<AtividadeDto> GetAsync(
        int tenantId, int? unitId, int limite, CancellationToken ct = default)
    {
        limite = Math.Clamp(limite, 5, TetoLinhas);
        var desde = DateTime.UtcNow - Janela;

        var escopo = _db.Leads.AsNoTracking()
            .Where(l => l.TenantId == tenantId && (!unitId.HasValue || l.UnitId == unitId.Value));

        // ── Leads que entraram ───────────────────────────────────────────────
        var entradas = await escopo
            .Where(l => l.CreatedAt >= desde)
            .OrderByDescending(l => l.CreatedAt)
            .Take(limite)
            .Select(l => new { l.Id, l.Name, l.Source, l.CreatedAt })
            .ToListAsync(ct);

        var linhas = entradas.Select(l => new AtividadeLinhaDto
        {
            LeadId = l.Id,
            Quando = l.CreatedAt,
            Tipo = "lead",
            Tom = "ok",
            Texto = $"{PrimeiroNome(l.Name)} entrou{Via(l.Source)}",
        }).ToList();

        // ── Mudanças de etapa (só com data confiável) ────────────────────────
        var etapas = await _db.LeadStageHistories.AsNoTracking()
            .Where(h => h.ChangedAt >= desde
                        && h.EntrySource != LeadStageHistory.SourceLegacy
                        && h.Lead.TenantId == tenantId
                        && (!unitId.HasValue || h.Lead.UnitId == unitId.Value))
            .OrderByDescending(h => h.ChangedAt)
            .Take(limite)
            .Select(h => new { h.LeadId, h.Lead.Name, h.StageLabel, h.ChangedAt })
            .ToListAsync(ct);

        linhas.AddRange(etapas.Select(h => new AtividadeLinhaDto
        {
            LeadId = h.LeadId,
            Quando = h.ChangedAt,
            Tipo = "etapa",
            Tom = TomDaEtapa(h.StageLabel),
            Texto = $"{PrimeiroNome(h.Name)} → {h.StageLabel}",
        }));

        // ── Data de consulta preenchida ──────────────────────────────────────
        var agendas = await escopo
            .Where(l => l.AppointmentScheduledAtFilledAt >= desde)
            .OrderByDescending(l => l.AppointmentScheduledAtFilledAt)
            .Take(limite)
            .Select(l => new { l.Id, l.Name, l.AppointmentScheduledAt, l.AppointmentScheduledAtFilledAt })
            .ToListAsync(ct);

        linhas.AddRange(agendas.Select(l => new AtividadeLinhaDto
        {
            LeadId = l.Id,
            Quando = l.AppointmentScheduledAtFilledAt!.Value,
            Tipo = "agenda",
            Tom = "ok",
            Texto = l.AppointmentScheduledAt is DateTime d
                ? $"{PrimeiroNome(l.Name)} marcado para {Local(d):dd/MM 'às' HH:mm}"
                : $"{PrimeiroNome(l.Name)} teve a data de consulta preenchida",
        }));

        // ── Qualificação preenchida ──────────────────────────────────────────
        var quals = await escopo
            .Where(l => l.QualificationFilledAt >= desde)
            .OrderByDescending(l => l.QualificationFilledAt)
            .Take(limite)
            .Select(l => new { l.Id, l.Name, l.Qualification, l.QualificationFilledAt })
            .ToListAsync(ct);

        linhas.AddRange(quals.Select(l => new AtividadeLinhaDto
        {
            LeadId = l.Id,
            Quando = l.QualificationFilledAt!.Value,
            Tipo = "campo",
            Tom = "neutro",
            Texto = string.IsNullOrWhiteSpace(l.Qualification)
                ? $"{PrimeiroNome(l.Name)} — qualificação preenchida"
                : $"{PrimeiroNome(l.Name)} qualificado como {l.Qualification}",
        }));

        var ordenadas = linhas
            .OrderByDescending(l => l.Quando)
            .Take(limite)
            .ToList();

        // Resumo da última hora: o log conta a história, o resumo dá o tamanho dela.
        var umaHora = DateTime.UtcNow.AddHours(-1);
        return new AtividadeDto
        {
            Linhas = ordenadas,
            NaUltimaHora = linhas.Count(l => l.Quando >= umaHora),
            EntraramNaUltimaHora = linhas.Count(l => l.Tipo == "lead" && l.Quando >= umaHora),
            // Quando não houve nada em 24 h, a tela precisa dizer isso — um log vazio
            // é indistinguível de um log quebrado.
            MaisRecente = ordenadas.FirstOrDefault()?.Quando,
        };
    }

    private static DateTime Local(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), SpineApiClient.BrTz);

    /// <summary>Só o primeiro nome: a linha do log tem largura de terminal, não de tabela.</summary>
    private static string PrimeiroNome(string? nome)
    {
        var n = (nome ?? string.Empty).Trim();
        if (n.Length == 0) return "Sem nome";
        var esp = n.IndexOf(' ');
        return esp > 0 ? n[..esp] : n;
    }

    private static string Via(string? origem) =>
        string.IsNullOrWhiteSpace(origem) || origem == "DESCONHECIDO" ? "" : $" por {origem}";

    /// <summary>
    /// A cor da linha diz o que aconteceu sem ninguém ler: ganho é verde, perdido é vermelho,
    /// o resto é o azul de "andou".
    /// </summary>
    private static string TomDaEtapa(string? etapa)
    {
        var e = (etapa ?? string.Empty).ToLowerInvariant();
        if (e.Contains("perdid") || e.Contains("descart")) return "ruim";
        if (e.Contains("ganho") || e.Contains("fechad") || e.Contains("tratament")) return "ok";
        if (e.Contains("agendad")) return "atencao";
        return "neutro";
    }
}
