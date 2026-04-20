using System.Collections.Concurrent;

namespace LeadAnalytics.Api.Service;

public class LogEntry
{
    public long Id { get; init; }
    public DateTime Timestamp { get; init; }
    public string Level { get; init; } = null!;
    public string Category { get; init; } = null!;
    public string Message { get; init; } = null!;
    public string? Exception { get; init; }
    public string? Path { get; init; }
    public string? Method { get; init; }
    public string? TraceId { get; init; }
}

/// <summary>
/// Armazena os últimos N logs em memória (ring buffer) para consumo
/// pelo painel de diagnóstico. NÃO é persistente — some a cada restart.
/// </summary>
public class InMemoryLogStore
{
    private readonly int _capacity;
    private readonly LinkedList<LogEntry> _items = new();
    private readonly ReaderWriterLockSlim _lock = new();
    private long _nextId;

    public InMemoryLogStore(int capacity = 5000)
    {
        _capacity = capacity;
    }

    public void Add(LogEntry entry)
    {
        _lock.EnterWriteLock();
        try
        {
            _items.AddFirst(entry);
            while (_items.Count > _capacity) _items.RemoveLast();
        }
        finally { _lock.ExitWriteLock(); }
    }

    public long NextId() => Interlocked.Increment(ref _nextId);

    public IReadOnlyList<LogEntry> Query(
        string? level,
        string? search,
        DateTime? since,
        int limit)
    {
        _lock.EnterReadLock();
        try
        {
            IEnumerable<LogEntry> q = _items;

            if (!string.IsNullOrWhiteSpace(level))
            {
                var levels = level
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                q = q.Where(e => levels.Contains(e.Level));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                q = q.Where(e =>
                    e.Message.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    e.Category.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    (e.Exception?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (e.Path?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            if (since.HasValue)
                q = q.Where(e => e.Timestamp >= since.Value);

            if (limit > 0) q = q.Take(limit);

            return q.ToList();
        }
        finally { _lock.ExitReadLock(); }
    }

    public IReadOnlyDictionary<string, int> Stats(DateTime? since)
    {
        _lock.EnterReadLock();
        try
        {
            var q = since.HasValue ? _items.Where(e => e.Timestamp >= since.Value) : _items;
            return q.GroupBy(e => e.Level)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        }
        finally { _lock.ExitReadLock(); }
    }

    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try { return _items.Count; }
            finally { _lock.ExitReadLock(); }
        }
    }

    public void Clear()
    {
        _lock.EnterWriteLock();
        try { _items.Clear(); }
        finally { _lock.ExitWriteLock(); }
    }
}
