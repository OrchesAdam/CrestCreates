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
