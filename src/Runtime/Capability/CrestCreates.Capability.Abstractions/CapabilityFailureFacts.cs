namespace CrestCreates.Capability.Abstractions;

/// <summary>
/// Transport-neutral failure classification for capability execution. No HTTP status, exception,
/// type or stack trace travels in these facts; a transport adapter maps the kind to its own wire.
/// </summary>
public enum CapabilityFailureKind
{
    InvalidRequest,
    ValidationFailed,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    PreconditionRequired,
    Cancelled,
    ManagedDeadline,
    NonCaller,
    Unknown,
}

public sealed record CapabilityFailureFacts(
    CapabilityFailureKind Kind,
    string ErrorCode,
    string SafeMessage,
    string? SafeDetails = null);

/// <summary>
/// Classifies a handler/business exception into bounded, transport-neutral facts. Implemented by the
/// host's web/transport adapter and consumed by the capability pipeline (Runtime never references
/// the adapter assembly).
/// </summary>
public interface ICapabilityExceptionClassifier
{
    CapabilityFailureFacts Classify(Exception exception);
}
