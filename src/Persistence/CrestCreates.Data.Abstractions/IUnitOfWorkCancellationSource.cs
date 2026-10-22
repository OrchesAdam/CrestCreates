using CrestCreates.Core.Abstractions.Threading;

namespace CrestCreates.Data.Abstractions;

/// <summary>
/// Implemented by the kernel scope so the weaving interceptor can attach the caller/execution
/// identities to the original cancellation exception before the ambient frame unwinds. This is an
/// internal bridge between the kernel and framework transports; business code never consumes it.
/// </summary>
public interface IUnitOfWorkCancellationSource
{
    /// <summary>
    /// Snapshot of the current frame's cancellation identities; null when the frame did not start or
    /// its linked execution token is not canceled.
    /// </summary>
    UnitOfWorkCancellationSnapshot? CaptureCancellationSnapshot();
}
