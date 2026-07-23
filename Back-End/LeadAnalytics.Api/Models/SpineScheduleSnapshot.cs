namespace LeadAnalytics.Api.Models;

/// <summary>
/// Cópia (snapshot) de um horário da agenda do Doutor Hérnia, preservada no nosso
/// banco. Existe porque a API do Spine não guarda histórico e só deixa consultar
/// 100 dias — passado isso, o dado some de lá. O n8n captura a agenda todo dia e
/// faz upsert por (UnitId, IdSchedule): o status muda ao longo do tempo (AGENDADO
/// → ATENDIDO/DESMARCADO), e a última captura vence.
///
/// Não tem vínculo com Lead/Kommo de propósito — isto é o fato clínico cru, cuja
/// chave é o IdSchedule do próprio Spine. O cruzamento com o comercial é feito à
/// parte (por telefone/nome), nunca aqui.
/// </summary>
public class SpineScheduleSnapshot
{
    public int Id { get; set; }

    /// <summary>Unidade (tenant/clínica) dona da agenda.</summary>
    public int UnitId { get; set; }
    public Unit? Unit { get; set; }

    /// <summary>Id do agendamento no Doutor Hérnia — metade da chave de upsert.</summary>
    public long IdSchedule { get; set; }

    /// <summary>Id do tratamento no Spine, quando houver (avaliação vem sem).</summary>
    public long? IdTreatment { get; set; }

    /// <summary>Data/hora do atendimento, em UTC (como o Spine devolve).</summary>
    public DateTime DateAttendanceUtc { get; set; }

    /// <summary>Dia local (America/Sao_Paulo) do atendimento — é por ele que se agrupa.</summary>
    public DateOnly DiaLocal { get; set; }

    /// <summary>1=Avaliação, 2=Sessão, 3=Retorno… (a categoria com que foi capturado).</summary>
    public int IdCategory { get; set; }
    public string? Categoria { get; set; }

    public string? Paciente { get; set; }
    public string? Profissional { get; set; }

    /// <summary>Situação da agenda: 37 Agendado, 42 Atendido, 40 Não compareceu, 57 Desmarcado…</summary>
    public int IdStatus { get; set; }
    public string? StatusName { get; set; }

    /// <summary>Quando o Spine registrou a última mudança nesse horário.</summary>
    public DateTime? ModifiedAtSpine { get; set; }
    public string? ModifiedBySpine { get; set; }

    /// <summary>Quando nós capturamos — mostra o quão fresco está o snapshot.</summary>
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
}
