using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace CrestCreates.Core.Abstractions.Threading;

/// <summary>
/// One frame of the managed unit-of-work chain. A Required scope joins the owner and records the
/// owner execution token as its caller; classification therefore follows the chain back to the root
/// caller instead of treating an inner frame's caller as the HTTP caller.
/// </summary>
public sealed record UnitOfWorkCancellationFrame(
    CancellationToken CallerToken,
    CancellationToken ExecutionToken,
    bool CallerTokenWasCancelled,
    bool DeadlineExpired,
    IReadOnlyList<CancellationToken> DerivedTokens,
    int DepthFromRoot);

/// <summary>
/// Identities and source facts captured by the unit-of-work kernel when a frame observes
/// cancellation. It is attached to the original <see cref="OperationCanceledException"/> (via
/// <see cref="ExceptionDataKey"/>) before the ambient frame unwinds; transports classify the source
/// from the typed chain without wrapping the business exception.
/// </summary>
public sealed record UnitOfWorkCancellationSnapshot(IReadOnlyList<UnitOfWorkCancellationFrame> Frames)
{
    /// <summary>The <see cref="Exception.Data"/> key carrying this snapshot.</summary>
    public const string ExceptionDataKey = "CrestCreates.UnitOfWork.CancellationSnapshot";

    /// <summary>Innermost frame view (the frame that most operations throw with).</summary>
    public CancellationToken CallerToken => Frames[0].CallerToken;

    /// <summary>Innermost execution token view.</summary>
    public CancellationToken ExecutionToken => Frames[0].ExecutionToken;

    /// <summary>Innermost caller cancellation view.</summary>
    public bool CallerTokenWasCancelled => Frames[0].CallerTokenWasCancelled;

    /// <summary>True when any frame's managed deadline expired.</summary>
    public bool DeadlineExpired => Frames.Any(frame => frame.DeadlineExpired);

    /// <summary>The outermost (root) caller identity — the transport caller for a request chain.</summary>
    public CancellationToken RootCallerToken => RootFrame.CallerToken;

    /// <summary>True when the root caller identity is canceled.</summary>
    public bool RootCallerWasCancelled => RootFrame.CallerTokenWasCancelled;

    private UnitOfWorkCancellationFrame RootFrame => Frames.OrderBy(frame => frame.DepthFromRoot).First();

    /// <summary>True when any frame's managed deadline expired.</summary>
    public bool AnyDeadlineExpired => Frames.Any(frame => frame.DeadlineExpired);

    /// <summary>
    /// True when the token is a frame execution token or an operation-derived token recorded by the
    /// official composition helper for one of those frames.
    /// </summary>
    public bool ContainsToken(CancellationToken token)
        => Frames.Any(frame =>
            (frame.ExecutionToken.CanBeCanceled && frame.ExecutionToken == token)
            || frame.DerivedTokens.Contains(token));

    /// <summary>
    /// Merges an outer frame chain onto an already attached inner chain. Frames are unique by
    /// execution token and keep inner-to-outer order, so <see cref="RootCallerToken"/> remains the
    /// outermost frame's caller.
    /// </summary>
    public UnitOfWorkCancellationSnapshot Merge(UnitOfWorkCancellationSnapshot outer)
    {
        var frames = new List<UnitOfWorkCancellationFrame>(Frames);
        foreach (var frame in outer.Frames)
        {
            if (!frames.Any(existing => existing.ExecutionToken == frame.ExecutionToken))
            {
                frames.Add(frame);
            }
        }

        // Order by the real execution depth, never by attach order: an outer frame must not become
        // the "root" merely because it was merged last (three-level Required).
        return new UnitOfWorkCancellationSnapshot(
            frames.OrderByDescending(frame => frame.DepthFromRoot).ToArray());
    }

    /// <summary>
    /// Attaches (or merges) the snapshot on a cancellation exception without replacing the original
    /// exception. Non-cancellation exceptions are ignored.
    /// </summary>
    public static void Attach(Exception exception, UnitOfWorkCancellationSnapshot? snapshot)
    {
        if (exception is not OperationCanceledException || snapshot is null)
        {
            return;
        }

        exception.Data[ExceptionDataKey] = exception.Data[ExceptionDataKey] is UnitOfWorkCancellationSnapshot existing
            ? existing.Merge(snapshot)
            : snapshot;
    }
}
