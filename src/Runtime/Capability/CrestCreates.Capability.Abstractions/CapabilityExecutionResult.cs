namespace CrestCreates.Capability.Abstractions;

public sealed class CapabilityExecutionResult
{
    public CapabilityExecutionStatus Status { get; init; }
    public object? Output { get; init; }
    public TimeSpan Duration { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? AuditRecordId { get; init; }
    public IReadOnlyList<string> EmittedEventIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<CapabilityExecutionIssue> Issues { get; init; } = Array.Empty<CapabilityExecutionIssue>();

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
        IReadOnlyList<CapabilityExecutionIssue>? issues = null)
        => new()
        {
            Status = CapabilityExecutionStatus.Failed,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Duration = duration,
            Issues = issues ?? Array.Empty<CapabilityExecutionIssue>()
        };

    public static CapabilityExecutionResult Timeout(TimeSpan duration)
        => new()
        {
            Status = CapabilityExecutionStatus.TimedOut,
            Duration = duration
        };
}
