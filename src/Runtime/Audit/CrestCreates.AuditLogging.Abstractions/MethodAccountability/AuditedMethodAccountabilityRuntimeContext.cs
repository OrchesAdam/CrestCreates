using System.Threading;

namespace CrestCreates.AuditLogging.Abstractions.MethodAccountability;

internal static class AuditedMethodAccountabilityRuntimeContext
{
    private static readonly AsyncLocal<Frame?> CurrentFrame = new();

    internal static IAuditedMethodAccountabilityRuntime? Current => CurrentFrame.Value?.Runtime;

    internal static IDisposable Push(IAuditedMethodAccountabilityRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var frame = new Frame(runtime, CurrentFrame.Value);
        CurrentFrame.Value = frame;
        return new Scope(frame);
    }

    private sealed record Frame(IAuditedMethodAccountabilityRuntime Runtime, Frame? Parent);

    private sealed class Scope(Frame frame) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            if (!ReferenceEquals(CurrentFrame.Value, frame))
                throw new InvalidOperationException("Audited method runtime scopes must be disposed in LIFO order.");
            Interlocked.Exchange(ref _disposed, 1);
            CurrentFrame.Value = frame.Parent;
        }
    }
}
