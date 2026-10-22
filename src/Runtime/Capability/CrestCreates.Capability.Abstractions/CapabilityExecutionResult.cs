namespace CrestCreates.Capability.Abstractions;

public sealed class CapabilityExecutionResult
{
    public CapabilityExecutionStatus Status { get; init; }
    public object? Output { get; init; }
    public TimeSpan Duration { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? AuditRecordId { get; internal set; }
    public IReadOnlyList<string> EmittedEventIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<CapabilityExecutionIssue> Issues { get; init; } = Array.Empty<CapabilityExecutionIssue>();

    /// <summary>
    /// Bounded, transport-neutral failure facts for a failed/cancelled execution. Null on legacy
    /// results that only carry an error code; transports must then fall back to their code mapping.
    /// </summary>
    public CapabilityFailureFacts? FailureFacts { get; init; }

    public bool IsSuccess => Status == CapabilityExecutionStatus.Succeeded;

    public static CapabilityExecutionResult Success(object? output, TimeSpan duration, string? auditRecordId = null, IReadOnlyList<string>? emittedEventIds = null)
        => new()
        {
            Status = CapabilityExecutionStatus.Succeeded,
            Output = output,
            Duration = duration,
            AuditRecordId = auditRecordId,
            EmittedEventIds = emittedEventIds ?? Array.Empty<string>()
        };

    public static CapabilityExecutionResult Failure(
        string errorCode,
        string errorMessage,
        TimeSpan duration,
        IReadOnlyList<CapabilityExecutionIssue>? issues = null,
        string? auditRecordId = null,
        CapabilityFailureFacts? failureFacts = null)
        => new()
        {
            Status = CapabilityExecutionStatus.Failed,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Duration = duration,
            Issues = issues ?? Array.Empty<CapabilityExecutionIssue>(),
            AuditRecordId = auditRecordId,
            FailureFacts = failureFacts
        };

    public static CapabilityExecutionResult Cancelled(
        TimeSpan duration,
        CapabilityFailureFacts? failureFacts = null,
        string? auditRecordId = null)
        => new()
        {
            Status = CapabilityExecutionStatus.Cancelled,
            Duration = duration,
            AuditRecordId = auditRecordId,
            FailureFacts = failureFacts
        };

    public static CapabilityExecutionResult Timeout(TimeSpan duration, string? auditRecordId = null)
        => new()
        {
            Status = CapabilityExecutionStatus.TimedOut,
            Duration = duration,
            AuditRecordId = auditRecordId
        };
}
