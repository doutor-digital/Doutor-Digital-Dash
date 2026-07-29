namespace LeadAnalytics.Api.Service;

public static class LeadStages
{
    public const string AgendadoSemPagamento = "04_AGENDADO_SEM_PAGAMENTO";
    public const string AgendadoComPagamento = "05_AGENDADO_COM_PAGAMENTO";
    public const string Faltou = "07_FALTOU";
    public const string NaoFechouTratamento = "08_NAO_FECHOU_TRATAMENTO";
    public const string FechouTratamento = "09_FECHOU_TRATAMENTO";
    public const string EmTratamento = "10_EM_TRATAMENTO";

    // Etapas do novo funil COMERCIAL/TRATAMENTO (Kommo 2026). Coexistem com as
    // legadas acima — a resolução é por NOME da etapa (CanonicalStages.Resolve),
    // então unidades ainda no funil antigo (04-10) continuam iguais. Apenas a ITZ,
    // já no funil novo, passa a gravar estes códigos em Lead.CurrentStage.
    public const string Qualificacao        = "QUALIFICACAO";         // COMERCIAL · Em Qualificação
    public const string Compareceu          = "COMPARECEU";           // COMERCIAL · Compareceu (veio à consulta)
    public const string Negociacao          = "NEGOCIACAO";           // COMERCIAL · Em Negociação
    public const string Perdido             = "PERDIDO";              // COMERCIAL · Perdido (status 143)
    public const string Alta                = "ALTA";                 // TRATAMENTO · Alta (status 142)
    public const string TratamentoCancelado = "TRATAMENTO_CANCELADO"; // TRATAMENTO · Cancelado (status 143)

    public const string AttendedCompareceu = "compareceu";
    public const string AttendedFaltou = "faltou";

    public static bool IsScheduled(string? stage) =>
        stage is AgendadoSemPagamento or AgendadoComPagamento;

    public static bool RequiresPriorAttendance(string? stage) =>
        stage is FechouTratamento or NaoFechouTratamento or EmTratamento
            or Compareceu or Negociacao or Alta;

    public static bool HasAppointmentRecord(string? stage) =>
        stage is AgendadoSemPagamento
            or AgendadoComPagamento
            or Faltou
            or NaoFechouTratamento
            or FechouTratamento
            or EmTratamento
            or Compareceu
            or Negociacao
            or Alta;
}
