namespace CrestCreates.Runtime.Delivery.Abstractions.Messages;

public sealed record OutboxMessageMetadata
{
    public required string MessageId { get; init; }
    public string? TenantId { get; init; }
    public required string ContractId { get; init; }
    public string PayloadTypeId { get; init; } = string.Empty;
    public string EventName { get; init; } = string.Empty;
    public int EventVersion { get; init; } = 1;
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public required IReadOnlyList<string> RequiredConsumerIds { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
