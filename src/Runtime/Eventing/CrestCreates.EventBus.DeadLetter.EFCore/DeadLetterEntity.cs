namespace CrestCreates.EventBus.DeadLetter.EFCore;

public sealed class DeadLetterEntity
{
    public string MessageId { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public int EventVersion { get; set; }
    public string? EventDescriptorId { get; set; }
    public string? CorrelationId { get; set; }
    public string? TenantId { get; set; }
    public string Scope { get; set; } = "Local";
    public string PayloadTypeFullName { get; set; } = string.Empty;
    public byte[] Payload { get; set; } = Array.Empty<byte>();
    public string ErrorMessage { get; set; } = string.Empty;
    public string? ExceptionType { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime FailedAt { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; }
    public string Status { get; set; } = "Pending";

    /// <summary>Local replay lookup key ("assembly:metadataTypeName"); null on pre-migration records.</summary>
    public string? LocalReplayKey { get; set; }

    /// <summary>Context snapshot format marker (additive nullable column); null on pre-migration records.</summary>
    public int? LocalContextVersion { get; set; }
}
