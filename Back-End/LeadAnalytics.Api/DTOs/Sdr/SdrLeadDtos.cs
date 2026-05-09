namespace LeadAnalytics.Api.DTOs.Sdr;

public class SdrSyncRequestDto
{
    /// <summary>Unidade alvo. Obrigatório para operadores não super-admin.</summary>
    public int? UnitId { get; set; }

    /// <summary>Início da janela (UTC). Default: now - 30 dias.</summary>
    public DateTime? From { get; set; }

    /// <summary>Fim da janela (UTC). Default: now.</summary>
    public DateTime? To { get; set; }

    /// <summary>
    /// Preset de turno: "morning" (08:00-12:00), "overnight" (20:00-07:50),
    /// "custom" (usa TimeStart/TimeEnd) ou null/empty (sem filtro de horário).
    /// </summary>
    public string? Shift { get; set; }

    /// <summary>Início da janela de horário "HH:mm" (Brasília). Usado quando Shift = "custom".</summary>
    public string? TimeStart { get; set; }

    /// <summary>Fim da janela de horário "HH:mm" (Brasília). Se &lt; TimeStart, atravessa meia-noite.</summary>
    public string? TimeEnd { get; set; }

    /// <summary>Limite de leads na resposta (1..2000). Default: 500.</summary>
    public int? Limit { get; set; }
}

public class SdrLeadResponseDto
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int? ExternalId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Telefone { get; set; } = string.Empty;
    public string Tipo { get; set; } = "Cadastro";
    public string Origem { get; set; } = string.Empty;
    public string? TipoResgate { get; set; }
    public bool Interacao { get; set; }
    public bool AgendouConsulta { get; set; }
    public string? DataAgendamento { get; set; }
    public string? MotivoNaoAgendamento { get; set; }
    public string NomeResponsavel { get; set; } = string.Empty;
    public string? Login { get; set; }
    public string? Observacao { get; set; }
    public string? Situacao { get; set; }
    public string? Clinica { get; set; }
    public string DataOrigem { get; set; } = string.Empty;
    public string? DataModificacao { get; set; }
    public string Source { get; set; } = "cloudia";
    public string Status { get; set; } = "pendente_revisao";
    public string? ReviewedAt { get; set; }
    public int? ReviewedByUserId { get; set; }
    public string? ReviewedByName { get; set; }
    public string? RejectionReason { get; set; }
    public List<string> CloudiaFields { get; set; } = new();
    public string? CloudiaReceivedAt { get; set; }
    public string? CloudiaWebhookEvent { get; set; }
    public int? UnitId { get; set; }
    public int? AttendantId { get; set; }
    public int? ImportBatchId { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}

public class SdrSyncSummaryDto
{
    public int Created { get; set; }
    public int Skipped { get; set; }
    public int Updated { get; set; }
    public int Failed { get; set; }
    public List<SdrLeadResponseDto> Items { get; set; } = new();

    /// <summary>Janela usada na consulta (eco para o front confirmar).</summary>
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int? UnitId { get; set; }
    public string? ShiftStart { get; set; }
    public string? ShiftEnd { get; set; }
}
