namespace LeadAnalytics.Api.DTOs.Juridico;

/// <summary>
/// Payload do dashboard do segmento jurídico (ex.: Advocacia Magalhães). Reúne os 7 grupos
/// de métricas. Renderizado pelos MESMOS componentes de card/gráfico do dashboard de saúde —
/// muda só o conteúdo. Campos vêm de etapas dos pipelines, de <c>AgentConversation</c>,
/// de <c>LeadStageHistory</c> e dos custom fields mapeados em <see cref="Service.Juridico.JuridicoFieldMap"/>.
/// </summary>
public sealed class JuridicoDashboardDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int TotalLeads { get; set; }

    /// <summary>Quais papéis de campo ainda não foram mapeados nas Configurações Técnicas (métricas afetadas vêm zeradas).</summary>
    public List<string> CamposNaoMapeados { get; set; } = [];

    public List<AreaCasoDto> AreaCaso { get; set; } = [];
    public List<SecretariaDto> Secretarias { get; set; } = [];
    public IaQualidadeDto Ia { get; set; } = new();
    public SlaJuridicoDto Sla { get; set; } = new();
    public ConversaoJuridicaDto Conversao { get; set; } = new();
    public QualificacaoDto Qualificacao { get; set; } = new();
    public RoiJuridicoDto Roi { get; set; } = new();
}

/// <summary>Distribuição de leads por área/tipo de caso.</summary>
public sealed class AreaCasoDto
{
    public string Area { get; set; } = "Não informado";
    public int Leads { get; set; }
    public double Pct { get; set; }
    /// <summary>Mix de criativos dentro da área (criativo → nº de leads).</summary>
    public List<LabeledCountDto> PorCriativo { get; set; } = [];
}

/// <summary>Qualidade de vendas de uma secretária/atendente.</summary>
public sealed class SecretariaDto
{
    public string Nome { get; set; } = "Não atribuído";
    public int Leads { get; set; }
    public int Agendados { get; set; }
    public double TaxaAgendamento { get; set; }
    public int Compareceram { get; set; }
    public int NoShow { get; set; }
    public double TaxaNoShow { get; set; }
    public int Contratos { get; set; }
    public double TaxaFechamento { get; set; }
    public int Perdas { get; set; }
    /// <summary>Principal motivo de perda dessa secretária (onde ela mais perde).</summary>
    public string? PrincipalMotivoPerda { get; set; }
}

/// <summary>Qualidade da I.A. — qualificação, agendamento, handoff e contribuição p/ contrato.</summary>
public sealed class IaQualidadeDto
{
    public int LeadsAtendidos { get; set; }
    public int Qualificados { get; set; }
    public double TaxaQualificacao { get; set; }
    public int AgendadosPelaIa { get; set; }
    public double TaxaIaAgendamento { get; set; }
    public int Handoffs { get; set; }
    public double TaxaHandoff { get; set; }
    /// <summary>Contratos cujos leads passaram pela I.A. (contribuição da IA p/ receita).</summary>
    public int ContribuiContratos { get; set; }
    /// <summary>Onde a IA mais perde (etapa/motivo predominante após handoff/desqualificação).</summary>
    public string? PrincipalPerda { get; set; }
}

/// <summary>SLA de 1ª resposta — IA × humano e por grupo.</summary>
public sealed class SlaJuridicoDto
{
    public double? MediaMinutos { get; set; }
    public double? MedianaMinutos { get; set; }
    public double? P90Minutos { get; set; }
    public int LeadsComResposta { get; set; }
    public double? IaMediaMinutos { get; set; }
    public double? HumanoMediaMinutos { get; set; }
    public List<GrupoSlaDto> PorGrupo { get; set; } = [];
}

public sealed class GrupoSlaDto
{
    public string Grupo { get; set; } = "Não informado";
    public double? MediaMinutos { get; set; }
    public int Leads { get; set; }
}

/// <summary>Funil de conversão dos 2 pipelines: lead → qualificado → agendado → compareceu → contrato.</summary>
public sealed class ConversaoJuridicaDto
{
    public int Lead { get; set; }
    public int Qualificado { get; set; }
    public int Agendado { get; set; }
    public int Compareceu { get; set; }
    public int Contrato { get; set; }

    public double TaxaQualificacao { get; set; }
    public double TaxaAgendamento { get; set; }
    public double TaxaComparecimento { get; set; }
    public double TaxaFechamento { get; set; }
    public double TaxaGeral { get; set; }

    /// <summary>Etapa onde cai a maior fração de leads (o gargalo).</summary>
    public string? Gargalo { get; set; }
}

/// <summary>Qualificado × Desqualificado e motivos de desqualificação por criativo.</summary>
public sealed class QualificacaoDto
{
    public int Qualificados { get; set; }
    public int Desqualificados { get; set; }
    public double TaxaQualificacao { get; set; }
    public List<LabeledCountDto> MotivosDesqualificacao { get; set; } = [];
    public List<CriativoQualificacaoDto> PorCriativo { get; set; } = [];
}

public sealed class CriativoQualificacaoDto
{
    public string Criativo { get; set; } = "Não informado";
    public int Leads { get; set; }
    public int Qualificados { get; set; }
    public int Desqualificados { get; set; }
    public double TaxaQualificacao { get; set; }
}

/// <summary>ROI por criativo e valor por área (valor estimado + honorário de êxito ÷ investimento).</summary>
public sealed class RoiJuridicoDto
{
    public List<RoiCriativoDto> PorCriativo { get; set; } = [];
    public List<ValorAreaDto> PorArea { get; set; } = [];
    public decimal ValorEstimadoTotal { get; set; }
    public decimal HonorarioExitoTotal { get; set; }
    public decimal InvestimentoTotal { get; set; }
    public double? RoiGeral { get; set; }
}

public sealed class RoiCriativoDto
{
    public string Criativo { get; set; } = "Não informado";
    public int Leads { get; set; }
    public int Contratos { get; set; }
    public decimal ValorEstimado { get; set; }
    public decimal HonorarioExito { get; set; }
    public decimal Investimento { get; set; }
    /// <summary>(honorário de êxito) / investimento. Null quando não há investimento conhecido.</summary>
    public double? Roi { get; set; }
    public decimal? CustoPorLead { get; set; }
}

public sealed class ValorAreaDto
{
    public string Area { get; set; } = "Não informado";
    public decimal ValorEstimado { get; set; }
    public decimal HonorarioExito { get; set; }
    public int Contratos { get; set; }
}

public sealed class LabeledCountDto
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
}
