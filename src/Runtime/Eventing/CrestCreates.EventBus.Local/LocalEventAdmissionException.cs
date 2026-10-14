using System;

namespace CrestCreates.EventBus.Local;

/// <summary>
/// Deterministic admission rejection (governance, policy or explicit check). Admission rejections
/// happen before any handler runs and never create a dead letter.
/// </summary>
public sealed class LocalEventAdmissionException : InvalidOperationException
{
    public LocalEventAdmissionException(string problemCode, string message)
        : base(message)
    {
        ProblemCode = problemCode;
    }

    public string ProblemCode { get; }
}

/// <summary>
/// The ambient execution context cannot be captured into a consistent snapshot (non-restorable or
/// contradictory audit/tenant facts). Capturing must fail rather than persist a fabricated fact.
/// </summary>
public sealed class LocalEventContextCaptureException : InvalidOperationException
{
    public LocalEventContextCaptureException(string problemCode, string message)
        : base(message)
    {
        ProblemCode = problemCode;
    }

    public string ProblemCode { get; }
}

/// <summary>Persisted context cannot be restored under the current capability/policy set.</summary>
internal sealed class LocalEventContextRestoreException : InvalidOperationException
{
    public LocalEventContextRestoreException(string problemCode, string message)
        : base(message)
    {
        ProblemCode = problemCode;
    }

    public string ProblemCode { get; }
}

/// <summary>
/// Ambient activation partially entered frames and then failed, and unwinding the entered frames
/// failed as well. Both facts travel together so callers can report Release.Failed instead of
/// claiming a clean release. Caller cancellation keeps its own exception identity: the cleanup
/// fact is attached to the rethrown OperationCanceledException instead of this type.
/// </summary>
internal sealed class LocalEventContextActivationException : InvalidOperationException
{
    public LocalEventContextActivationException(Exception activationFailure, Exception cleanupFailure)
        : base(
            $"context activation failed ({activationFailure.Message}) and restoring the already-entered " +
            "frames also failed; both facts are reported.",
            activationFailure)
    {
        CleanupFailure = cleanupFailure;
    }

    public Exception CleanupFailure { get; }
}
