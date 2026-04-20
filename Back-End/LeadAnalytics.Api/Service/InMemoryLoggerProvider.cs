using Microsoft.Extensions.Logging;

namespace LeadAnalytics.Api.Service;

[ProviderAlias("InMemory")]
public sealed class InMemoryLoggerProvider(InMemoryLogStore store, IHttpContextAccessor httpContext) : ILoggerProvider
{
    private readonly InMemoryLogStore _store = store;
    private readonly IHttpContextAccessor _httpContext = httpContext;

    public ILogger CreateLogger(string categoryName)
        => new InMemoryLogger(categoryName, _store, _httpContext);

    public void Dispose() { }
}

internal sealed class InMemoryLogger : ILogger
{
    private readonly string _category;
    private readonly InMemoryLogStore _store;
    private readonly IHttpContextAccessor _httpContext;

    public InMemoryLogger(string category, InMemoryLogStore store, IHttpContextAccessor httpContext)
    {
        _category = category;
        _store = store;
        _httpContext = httpContext;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception) ?? string.Empty;
        var ctx = _httpContext.HttpContext;

        _store.Add(new LogEntry
        {
            Id = _store.NextId(),
            Timestamp = DateTime.UtcNow,
            Level = logLevel.ToString(),
            Category = _category,
            Message = message,
            Exception = exception?.ToString(),
            Path = ctx?.Request.Path.ToString(),
            Method = ctx?.Request.Method,
            TraceId = ctx?.TraceIdentifier,
        });
    }
}
