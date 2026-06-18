namespace CrestCreates.Capability.Abstractions;

public sealed record CapabilityExecutionRecord
{
    public string ExecutionId { get; init; } = string.Empty;
    public string CapabilityId { get; init; } = string.Empty;
    public string CapabilityName { get; init; } = string.Empty;
    public int CapabilityVersion { get; init; }
    public string? TenantId { get; init; }
    public string? UserId { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public InvocationSource Source { get; init; }
    public bool IsSuccess { get; init; }
    public string? ErrorCode { get; init; }
    public TimeSpan Duration { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}
