namespace CrestCreates.Runtime.Delivery.Abstractions.Messages;

public interface IOutboxMessageFactory
{
    OutboxMessage Create(
        string messageId,
        string? tenantId,
        string contractId,
        string payloadTypeId,
        ReadOnlySpan<byte> payload,
        IEnumerable<string>? requiredConsumerIds = null,
        DateTimeOffset? createdAt = null);
}
