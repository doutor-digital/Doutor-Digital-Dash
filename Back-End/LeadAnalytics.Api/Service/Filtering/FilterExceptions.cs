namespace LeadAnalytics.Api.Service.Filtering;

public class FilterValidationException : Exception
{
    public string? Field { get; }
    public FilterValidationException(string message, string? field = null) : base(message) { Field = field; }
}

public class FilterNotImplementedException : Exception
{
    public string Field { get; }
    public FilterNotImplementedException(string field)
        : base($"filtro '{field}' reconhecido mas não implementado no schema atual")
    {
        Field = field;
    }
}
