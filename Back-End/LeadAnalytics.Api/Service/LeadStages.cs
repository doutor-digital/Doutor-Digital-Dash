namespace LeadAnalytics.Api.Service;

public static class LeadStages
{
    public const string AgendadoSemPagamento = "04_AGENDADO_SEM_PAGAMENTO";
    public const string AgendadoComPagamento = "05_AGENDADO_COM_PAGAMENTO";
    public const string Faltou = "07_FALTOU";
    public const string NaoFechouTratamento = "08_NAO_FECHOU_TRATAMENTO";
    public const string FechouTratamento = "09_FECHOU_TRATAMENTO";
    public const string EmTratamento = "10_EM_TRATAMENTO";

    public const string AttendedCompareceu = "compareceu";
    public const string AttendedFaltou = "faltou";

    public static bool IsScheduled(string? stage) =>
        stage is AgendadoSemPagamento or AgendadoComPagamento;

    public static bool RequiresPriorAttendance(string? stage) =>
        stage is FechouTratamento or NaoFechouTratamento or EmTratamento;

    public static bool HasAppointmentRecord(string? stage) =>
        stage is AgendadoSemPagamento
            or AgendadoComPagamento
            or Faltou
            or NaoFechouTratamento
            or FechouTratamento
            or EmTratamento;
}
