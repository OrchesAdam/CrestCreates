using Microsoft.Extensions.Logging;

namespace CrestCreates.Logging.Tests;

public sealed class TestLogger<T> : ILogger<T>
{
    private readonly Stack<IReadOnlyDictionary<string, object?>> _scopes = new();

    public List<TestLogEntry> Entries { get; } = new();

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        var scopeValues = state as IEnumerable<KeyValuePair<string, object?>>
            ?? Array.Empty<KeyValuePair<string, object?>>();

        var scope = scopeValues.ToDictionary(item => item.Key, item => item.Value);
        _scopes.Push(scope);
        return new ScopeHandle(_scopes);
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new TestLogEntry(
            logLevel,
            formatter(state, exception),
            exception,
            _scopes.Count > 0 ? new Dictionary<string, object?>(_scopes.Peek()) : new Dictionary<string, object?>()));
    }

    private sealed class ScopeHandle : IDisposable
    {
        private readonly Stack<IReadOnlyDictionary<string, object?>> _scopes;

        public ScopeHandle(Stack<IReadOnlyDictionary<string, object?>> scopes)
        {
            _scopes = scopes;
        }

        public void Dispose()
        {
            if (_scopes.Count > 0)
            {
                _scopes.Pop();
            }
        }
    }
}

public sealed record TestLogEntry(
    LogLevel LogLevel,
    string Message,
    Exception? Exception,
    IReadOnlyDictionary<string, object?> Scope);
