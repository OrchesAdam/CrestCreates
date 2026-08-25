using System.Text.Json.Serialization.Metadata;

namespace CrestCreates.Runtime.Delivery.Abstractions.Messages;

public interface IOutboxMessageFactory
{
    OutboxMessage Create<TPayload>(OutboxMessageMetadata metadata, TPayload payload, JsonTypeInfo<TPayload> jsonTypeInfo);

    OutboxMessage Create(
        string messageId,
        string? tenantId,
        string contractId,
        string payloadTypeId,
        ReadOnlySpan<byte> payload,
        IEnumerable<string>? requiredConsumerIds = null,
        DateTimeOffset? createdAt = null);
}
