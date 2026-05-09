using LeadAnalytics.Api.Data;
using LeadAnalytics.Api.DTOs.Sdr;
using LeadAnalytics.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadAnalytics.Api.Service;

// ───────────────────────────────────────────────────────────────────────────
// Consultas
// ───────────────────────────────────────────────────────────────────────────

public class SdrConsultaService(AppDbContext db, SdrAuditLogService audit, ILogger<SdrConsultaService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly SdrAuditLogService _audit = audit;
    private readonly ILogger<SdrConsultaService> _logger = logger;

    public async Task<List<SdrConsultaResponseDto>> ListAsync(int tenantId, int? leadId = null, CancellationToken ct = default)
    {
        var q = _db.SdrConsultas.AsNoTracking()
            .Include(c => c.Recebimentos)
            .Where(c => c.TenantId == tenantId);
        if (leadId.HasValue) q = q.Where(c => c.SdrLeadId == leadId);
        var rows = await q.OrderByDescending(c => c.DataConsulta).ToListAsync(ct);
        return rows.Select(MapToDto).ToList();
    }

    public async Task<SdrConsultaResponseDto> CreateAsync(int tenantId, SdrConsultaCreateDto dto, CancellationToken ct = default)
    {
        var lead = await _db.SdrLeads.FirstOrDefaultAsync(
            l => l.Id == dto.SdrLeadId && l.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"lead {dto.SdrLeadId} não encontrado");

        if (dto.Recebimentos is { Count: > 2 })
            throw new ArgumentException("Consulta aceita no máximo 2 recebimentos");

        var now = DateTime.UtcNow;
        var consulta = new SdrConsulta
        {
            TenantId = tenantId,
            SdrLeadId = lead.Id,
            DataConsulta = SdrUtil.ToUtc(dto.DataConsulta) ?? now,
            ValorConsulta = dto.ValorConsulta,
            Pago = dto.Pago,
            Status = dto.Status,
            TipoTratamentoIndicado = dto.TipoTratamentoIndicado,
            ValorTratamento = dto.ValorTratamento,
            FechouTratamento = dto.FechouTratamento,
            MotivoNaoFechamento = dto.MotivoNaoFechamento,
            Observacao = dto.Observacao,
            CreatedAt = now,
            UpdatedAt = now,
        };

        if (dto.Recebimentos is { Count: > 0 })
        {
            int ordem = 1;
            foreach (var r in dto.Recebimentos)
                consulta.Recebimentos.Add(new SdrRecebimento
                {
                    TenantId = tenantId,
                    Ordem = r.Ordem > 0 ? r.Ordem : ordem++,
                    Valor = r.Valor,
                    FormaPagamento = r.FormaPagamento.Trim(),
                    DataRecebimento = SdrUtil.ToUtc(r.DataRecebimento) ?? now,
                    Notes = r.Notes,
                    CreatedAt = now,
                });
        }

        _db.SdrConsultas.Add(consulta);
        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(tenantId, "sdr_consulta.created", "SdrConsulta", consulta.Id,
            $"Registrou consulta de {lead.Nome} ({consulta.DataConsulta:dd/MM/yyyy})",
            after: MapToDto(consulta), ct: ct);

        return MapToDto(consulta);
    }

    public async Task<bool> DeleteAsync(int tenantId, int id, CancellationToken ct = default)
    {
        var c = await _db.SdrConsultas.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        if (c is null) return false;
        _db.SdrConsultas.Remove(c);
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(tenantId, "sdr_consulta.deleted", "SdrConsulta", id, $"Removeu consulta #{id}", ct: ct);
        return true;
    }

    private static SdrConsultaResponseDto MapToDto(SdrConsulta c)
    {
        var rec = c.Recebimentos.OrderBy(r => r.Ordem).Select(MapRec).ToList();
        var totalRec = rec.Sum(r => r.Valor);
        return new SdrConsultaResponseDto
        {
            Id = c.Id,
            SdrLeadId = c.SdrLeadId,
            DataConsulta = c.DataConsulta,
            ValorConsulta = c.ValorConsulta,
            Pago = c.Pago,
            Status = c.Status,
            TipoTratamentoIndicado = c.TipoTratamentoIndicado,
            ValorTratamento = c.ValorTratamento,
            FechouTratamento = c.FechouTratamento,
            MotivoNaoFechamento = c.MotivoNaoFechamento,
            Observacao = c.Observacao,
            Recebimentos = rec,
            TotalRecebido = totalRec,
            FaltaReceber = Math.Max(0, c.ValorConsulta - totalRec),
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
        };
    }

    public static SdrRecebimentoResponseDto MapRec(SdrRecebimento r) => new()
    {
        Id = r.Id,
        SdrConsultaId = r.SdrConsultaId,
        SdrTratamentoId = r.SdrTratamentoId,
        Ordem = r.Ordem,
        Valor = r.Valor,
        FormaPagamento = r.FormaPagamento,
        DataRecebimento = r.DataRecebimento,
        Notes = r.Notes,
        CreatedAt = r.CreatedAt,
    };
}

// ───────────────────────────────────────────────────────────────────────────
// Tratamentos
// ───────────────────────────────────────────────────────────────────────────

public class SdrTratamentoService(AppDbContext db, SdrAuditLogService audit, ILogger<SdrTratamentoService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly SdrAuditLogService _audit = audit;
    private readonly ILogger<SdrTratamentoService> _logger = logger;

    public async Task<List<SdrTratamentoResponseDto>> ListAsync(int tenantId, int? leadId = null, CancellationToken ct = default)
    {
        var q = _db.SdrTratamentos.AsNoTracking()
            .Include(t => t.Recebimentos)
            .Where(t => t.TenantId == tenantId);
        if (leadId.HasValue) q = q.Where(t => t.SdrLeadId == leadId);
        var rows = await q.OrderByDescending(t => t.CreatedAt).ToListAsync(ct);
        return rows.Select(MapToDto).ToList();
    }

    public async Task<SdrTratamentoResponseDto> CreateAsync(int tenantId, SdrTratamentoCreateDto dto, CancellationToken ct = default)
    {
        var consulta = await _db.SdrConsultas.FirstOrDefaultAsync(
            c => c.Id == dto.SdrConsultaId && c.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"consulta {dto.SdrConsultaId} não encontrada");

        if (dto.Recebimentos is { Count: > 4 })
            throw new ArgumentException("Tratamento aceita no máximo 4 recebimentos");

        var now = DateTime.UtcNow;
        var trat = new SdrTratamento
        {
            TenantId = tenantId,
            SdrConsultaId = consulta.Id,
            SdrLeadId = consulta.SdrLeadId,
            Valor = dto.Valor,
            Status = dto.Status,
            Tipo = dto.Tipo,
            Descricao = dto.Descricao,
            Observacao = dto.Observacao,
            Situacao = dto.Situacao,
            CreatedAt = now,
            UpdatedAt = now,
        };

        if (dto.Recebimentos is { Count: > 0 })
        {
            int ordem = 1;
            foreach (var r in dto.Recebimentos)
                trat.Recebimentos.Add(new SdrRecebimento
                {
                    TenantId = tenantId,
                    Ordem = r.Ordem > 0 ? r.Ordem : ordem++,
                    Valor = r.Valor,
                    FormaPagamento = r.FormaPagamento.Trim(),
                    DataRecebimento = SdrUtil.ToUtc(r.DataRecebimento) ?? now,
                    Notes = r.Notes,
                    CreatedAt = now,
                });
        }

        _db.SdrTratamentos.Add(trat);
        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(tenantId, "sdr_tratamento.created", "SdrTratamento", trat.Id,
            $"Registrou tratamento de R$ {trat.Valor:N2}", after: MapToDto(trat), ct: ct);

        return MapToDto(trat);
    }

    public async Task<bool> DeleteAsync(int tenantId, int id, CancellationToken ct = default)
    {
        var t = await _db.SdrTratamentos.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        if (t is null) return false;
        _db.SdrTratamentos.Remove(t);
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(tenantId, "sdr_tratamento.deleted", "SdrTratamento", id, $"Removeu tratamento #{id}", ct: ct);
        return true;
    }

    private static SdrTratamentoResponseDto MapToDto(SdrTratamento t)
    {
        var rec = t.Recebimentos.OrderBy(r => r.Ordem).Select(SdrConsultaService.MapRec).ToList();
        var totalRec = rec.Sum(r => r.Valor);
        return new SdrTratamentoResponseDto
        {
            Id = t.Id,
            SdrConsultaId = t.SdrConsultaId,
            SdrLeadId = t.SdrLeadId,
            Valor = t.Valor,
            Status = t.Status,
            Tipo = t.Tipo,
            Descricao = t.Descricao,
            Observacao = t.Observacao,
            Situacao = t.Situacao,
            Recebimentos = rec,
            TotalRecebido = totalRec,
            FaltaReceber = Math.Max(0, t.Valor - totalRec),
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt,
        };
    }
}

// ───────────────────────────────────────────────────────────────────────────
// Tarefas
// ───────────────────────────────────────────────────────────────────────────

public class SdrTarefaService(AppDbContext db, SdrAuditLogService audit, ILogger<SdrTarefaService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly SdrAuditLogService _audit = audit;
    private readonly ILogger<SdrTarefaService> _logger = logger;

    public async Task<List<SdrTarefaResponseDto>> ListAsync(int tenantId, string? status = null, CancellationToken ct = default)
    {
        var q = _db.SdrTarefas.AsNoTracking().Where(t => t.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(t => t.Status == status);
        var rows = await q.OrderBy(t => t.DataVencimento).ToListAsync(ct);
        return rows.Select(MapToDto).ToList();
    }

    public async Task<SdrTarefaResponseDto> CreateAsync(int tenantId, SdrTarefaCreateDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Nome)) throw new ArgumentException("Nome da tarefa obrigatório");
        var now = DateTime.UtcNow;
        var t = new SdrTarefa
        {
            TenantId = tenantId,
            Nome = dto.Nome.Trim(),
            Descricao = dto.Descricao,
            DataVencimento = SdrUtil.ToUtc(dto.DataVencimento) ?? now,
            Prioridade = dto.Prioridade,
            Status = dto.Status,
            Observacao = dto.Observacao,
            ResponsavelLogin = dto.ResponsavelLogin,
            SdrLeadId = dto.SdrLeadId,
            CreatedAt = now,
            UpdatedAt = now,
            ConcludedAt = dto.Status == "concluida" ? now : null,
        };
        _db.SdrTarefas.Add(t);
        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(tenantId, "sdr_tarefa.created", "SdrTarefa", t.Id, $"Criou tarefa: {t.Nome}", after: MapToDto(t), ct: ct);
        return MapToDto(t);
    }

    public async Task<SdrTarefaResponseDto?> UpdateAsync(int tenantId, int id, SdrTarefaUpdateDto dto, CancellationToken ct = default)
    {
        var t = await _db.SdrTarefas.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        if (t is null) return null;
        var before = MapToDto(t);

        var prevStatus = t.Status;
        t.Nome = dto.Nome.Trim();
        t.Descricao = dto.Descricao;
        t.DataVencimento = SdrUtil.ToUtc(dto.DataVencimento) ?? t.DataVencimento;
        t.Prioridade = dto.Prioridade;
        t.Status = dto.Status;
        t.Observacao = dto.Observacao;
        t.ResponsavelLogin = dto.ResponsavelLogin;
        t.SdrLeadId = dto.SdrLeadId;
        t.UpdatedAt = DateTime.UtcNow;
        if (prevStatus != "concluida" && t.Status == "concluida") t.ConcludedAt = t.UpdatedAt;
        if (prevStatus == "concluida" && t.Status != "concluida") t.ConcludedAt = null;

        await _db.SaveChangesAsync(ct);
        var after = MapToDto(t);
        await _audit.RecordAsync(tenantId, "sdr_tarefa.updated", "SdrTarefa", t.Id, $"Editou tarefa: {t.Nome}", before: before, after: after, ct: ct);
        return after;
    }

    public async Task<bool> DeleteAsync(int tenantId, int id, CancellationToken ct = default)
    {
        var t = await _db.SdrTarefas.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        if (t is null) return false;
        _db.SdrTarefas.Remove(t);
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(tenantId, "sdr_tarefa.deleted", "SdrTarefa", id, $"Removeu tarefa #{id}", ct: ct);
        return true;
    }

    private static SdrTarefaResponseDto MapToDto(SdrTarefa t) => new()
    {
        Id = t.Id,
        Nome = t.Nome,
        Descricao = t.Descricao,
        DataVencimento = t.DataVencimento,
        Prioridade = t.Prioridade,
        Status = t.Status,
        Observacao = t.Observacao,
        ResponsavelLogin = t.ResponsavelLogin,
        SdrLeadId = t.SdrLeadId,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt,
        ConcludedAt = t.ConcludedAt,
    };
}

// ───────────────────────────────────────────────────────────────────────────
// Agenda
// ───────────────────────────────────────────────────────────────────────────

public class SdrAgendaService(AppDbContext db, SdrAuditLogService audit, ILogger<SdrAgendaService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly SdrAuditLogService _audit = audit;

    public async Task<List<SdrAgendaResponseDto>> ListAsync(int tenantId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        var q = _db.SdrAgendaEventos.AsNoTracking().Where(e => e.TenantId == tenantId);
        if (from.HasValue) q = q.Where(e => e.Data >= from);
        if (to.HasValue) q = q.Where(e => e.Data <= to);
        var rows = await q.OrderBy(e => e.Data).ThenBy(e => e.HoraInicio).ToListAsync(ct);
        return rows.Select(MapToDto).ToList();
    }

    public async Task<SdrAgendaResponseDto> CreateAsync(int tenantId, SdrAgendaCreateDto dto, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var e = new SdrAgendaEvento
        {
            TenantId = tenantId,
            Data = SdrUtil.ToUtc(dto.Data) ?? now,
            HoraInicio = dto.HoraInicio,
            HoraFim = dto.HoraFim,
            Nome = dto.Nome.Trim(),
            Descricao = dto.Descricao,
            Status = dto.Status,
            Observacao = dto.Observacao,
            ResponsavelLogin = dto.ResponsavelLogin,
            SdrLeadId = dto.SdrLeadId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.SdrAgendaEventos.Add(e);
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(tenantId, "sdr_agenda.created", "SdrAgendaEvento", e.Id, $"Agendou {e.Nome} em {e.Data:dd/MM} {e.HoraInicio}", after: MapToDto(e), ct: ct);
        return MapToDto(e);
    }

    public async Task<SdrAgendaResponseDto?> UpdateAsync(int tenantId, int id, SdrAgendaUpdateDto dto, CancellationToken ct = default)
    {
        var e = await _db.SdrAgendaEventos.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        if (e is null) return null;
        var before = MapToDto(e);
        e.Data = SdrUtil.ToUtc(dto.Data) ?? e.Data;
        e.HoraInicio = dto.HoraInicio;
        e.HoraFim = dto.HoraFim;
        e.Nome = dto.Nome.Trim();
        e.Descricao = dto.Descricao;
        e.Status = dto.Status;
        e.Observacao = dto.Observacao;
        e.ResponsavelLogin = dto.ResponsavelLogin;
        e.SdrLeadId = dto.SdrLeadId;
        e.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        var after = MapToDto(e);
        await _audit.RecordAsync(tenantId, "sdr_agenda.updated", "SdrAgendaEvento", e.Id, $"Editou evento #{e.Id}", before: before, after: after, ct: ct);
        return after;
    }

    public async Task<bool> DeleteAsync(int tenantId, int id, CancellationToken ct = default)
    {
        var e = await _db.SdrAgendaEventos.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        if (e is null) return false;
        _db.SdrAgendaEventos.Remove(e);
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(tenantId, "sdr_agenda.deleted", "SdrAgendaEvento", id, $"Removeu evento #{id}", ct: ct);
        return true;
    }

    private static SdrAgendaResponseDto MapToDto(SdrAgendaEvento e) => new()
    {
        Id = e.Id,
        Data = e.Data,
        HoraInicio = e.HoraInicio,
        HoraFim = e.HoraFim,
        Nome = e.Nome,
        Descricao = e.Descricao,
        Status = e.Status,
        Observacao = e.Observacao,
        ResponsavelLogin = e.ResponsavelLogin,
        SdrLeadId = e.SdrLeadId,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };
}

// ───────────────────────────────────────────────────────────────────────────
// Metas
// ───────────────────────────────────────────────────────────────────────────

public class SdrMetaService(AppDbContext db, SdrAuditLogService audit, ILogger<SdrMetaService> logger)
{
    private readonly AppDbContext _db = db;
    private readonly SdrAuditLogService _audit = audit;

    public async Task<List<SdrMetaResponseDto>> ListAsync(int tenantId, string? mes = null, CancellationToken ct = default)
    {
        var q = _db.SdrMetas.AsNoTracking().Where(m => m.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(mes)) q = q.Where(m => m.Mes == mes);
        var rows = await q.OrderByDescending(m => m.Mes).ThenBy(m => m.Secretaria).ToListAsync(ct);
        return rows.Select(MapToDto).ToList();
    }

    public async Task<SdrMetaResponseDto> UpsertAsync(int tenantId, SdrMetaUpsertDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Mes) || dto.Mes.Length != 7)
            throw new ArgumentException("Mes deve estar no formato YYYY-MM");

        var existing = await _db.SdrMetas.FirstOrDefaultAsync(
            m => m.TenantId == tenantId && m.Mes == dto.Mes && m.Login == dto.Login, ct);

        var now = DateTime.UtcNow;
        if (existing is null)
        {
            var meta = new SdrMeta
            {
                TenantId = tenantId,
                Mes = dto.Mes,
                Unidade = dto.Unidade,
                Login = dto.Login,
                Secretaria = dto.Secretaria,
                MetaValor = dto.MetaValor,
                RealCadastro = dto.RealCadastro,
                RealResgate = dto.RealResgate,
                QtdTotal = dto.QtdTotal,
                CreatedAt = now,
                UpdatedAt = now,
            };
            _db.SdrMetas.Add(meta);
            await _db.SaveChangesAsync(ct);
            await _audit.RecordAsync(tenantId, "sdr_meta.created", "SdrMeta", meta.Id, $"Definiu meta de {meta.Secretaria} ({meta.Mes})", after: MapToDto(meta), ct: ct);
            return MapToDto(meta);
        }

        var before = MapToDto(existing);
        existing.Unidade = dto.Unidade;
        existing.Secretaria = dto.Secretaria;
        existing.MetaValor = dto.MetaValor;
        existing.RealCadastro = dto.RealCadastro;
        existing.RealResgate = dto.RealResgate;
        existing.QtdTotal = dto.QtdTotal;
        existing.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);
        var after = MapToDto(existing);
        await _audit.RecordAsync(tenantId, "sdr_meta.updated", "SdrMeta", existing.Id, $"Atualizou meta de {existing.Secretaria}", before: before, after: after, ct: ct);
        return after;
    }

    public async Task<bool> DeleteAsync(int tenantId, int id, CancellationToken ct = default)
    {
        var m = await _db.SdrMetas.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        if (m is null) return false;
        _db.SdrMetas.Remove(m);
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync(tenantId, "sdr_meta.deleted", "SdrMeta", id, $"Removeu meta #{id}", ct: ct);
        return true;
    }

    private static SdrMetaResponseDto MapToDto(SdrMeta m) => new()
    {
        Id = m.Id,
        Mes = m.Mes,
        Unidade = m.Unidade,
        Login = m.Login,
        Secretaria = m.Secretaria,
        MetaValor = m.MetaValor,
        RealCadastro = m.RealCadastro,
        RealResgate = m.RealResgate,
        QtdTotal = m.QtdTotal,
        CreatedAt = m.CreatedAt,
        UpdatedAt = m.UpdatedAt,
    };
}

// ───────────────────────────────────────────────────────────────────────────
// Helper compartilhado
// ───────────────────────────────────────────────────────────────────────────

internal static class SdrUtil
{
    public static DateTime? ToUtc(DateTime? d) =>
        d?.Kind switch
        {
            DateTimeKind.Utc => d,
            DateTimeKind.Local => d.Value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(d.Value, DateTimeKind.Utc),
            _ => d,
        };
}
