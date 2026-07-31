using System.Threading;
using CrestCreates.Accountability.Abstractions.Context;

namespace CrestCreates.Accountability.Context;

public sealed class AuditOperationContextAccessor : IAuditOperationContextAccessor
{
    private readonly AsyncLocal<Frame?> _current = new();

    public AuditOperationContext? Current => _current.Value?.Context;

    public IDisposable Push(AuditOperationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var frame = new Frame(context, _current.Value);
        _current.Value = frame;
        return new Scope(this, frame);
    }

    private sealed record Frame(AuditOperationContext Context, Frame? Parent);

    private sealed class Scope : IDisposable
    {
        private readonly AuditOperationContextAccessor _owner;
        private readonly Frame _frame;
        private int _disposed;

        public Scope(AuditOperationContextAccessor owner, Frame frame)
        {
            _owner = owner;
            _frame = frame;
        }

        public void Dispose()
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            if (!ReferenceEquals(_owner._current.Value, _frame))
                throw new InvalidOperationException("Accountability operation scopes must be disposed in LIFO order.");
            Interlocked.Exchange(ref _disposed, 1);
            _owner._current.Value = _frame.Parent;
        }
    }
}
