using System.Threading;

namespace CrestCreates.Core.Abstractions.Threading;

/// <summary>
/// Identities and source facts captured by the unit-of-work kernel when a frame observes
/// cancellation. The kernel attaches this snapshot to the original
/// <see cref="System.OperationCanceledException"/> (via <see cref="ExceptionDataKey"/>) before the
/// ambient frame unwinds, so transports can classify the source without wrapping or replacing the
/// business exception and without reading ambient state that no longer exists.
/// </summary>
public sealed record UnitOfWorkCancellationSnapshot(
    CancellationToken CallerToken,
    CancellationToken ExecutionToken,
    bool CallerTokenWasCancelled,
    bool DeadlineExpired)
{
    /// <summary>The <see cref="System.Exception.Data"/> key carrying this snapshot.</summary>
    public const string ExceptionDataKey = "CrestCreates.UnitOfWork.CancellationSnapshot";
}
