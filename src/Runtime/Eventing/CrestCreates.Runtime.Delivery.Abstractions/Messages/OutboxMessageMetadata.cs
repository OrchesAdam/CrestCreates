namespace CrestCreates.Runtime.Delivery.Abstractions.Messages;

public sealed record OutboxMessageMetadata
{
    public required string MessageId { get; init; }
    public string? TenantId { get; init; }
    public required string ContractId { get; init; }
    public required string PayloadTypeId { get; init; }
    public required IReadOnlyList<string> RequiredConsumerIds { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
