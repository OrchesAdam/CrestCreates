using CrestCreates.Runtime.Delivery.Abstractions.Contracts;
using CrestCreates.Runtime.Delivery.Abstractions.Messages;

namespace CrestCreates.Runtime.Delivery.Message;

public sealed class DefaultOutboxMessageFactory : IOutboxMessageFactory
{
    public OutboxMessage Create(
        string messageId,
        string? tenantId,
        string contractId,
        string payloadTypeId,
        ReadOnlySpan<byte> payload,
        IEnumerable<string>? requiredConsumerIds = null,
        DateTimeOffset? createdAt = null)
    {
        ValidateIdentifier(messageId, nameof(messageId));
        ValidateIdentifier(contractId, nameof(contractId));
        ValidateIdentifier(payloadTypeId, nameof(payloadTypeId));
        if (payload.Length is < OutboxContractLimits.MinPayloadBytes or > OutboxContractLimits.MaxPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(payload), "Outbox payload size is outside the supported bounds.");

        var consumers = (requiredConsumerIds ?? Array.Empty<string>())
            .Select(value => ValidateIdentifier(value, "required consumer id"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (consumers.Length > OutboxContractLimits.MaxRequiredConsumerCount)
            throw new ArgumentOutOfRangeException(nameof(requiredConsumerIds));

        var metadata = new OutboxMessageMetadata
        {
            MessageId = messageId,
            TenantId = string.IsNullOrWhiteSpace(tenantId) ? null : tenantId,
            ContractId = contractId,
            PayloadTypeId = payloadTypeId,
            RequiredConsumerIds = consumers,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow
        };
        var detachedPayload = payload.ToArray();
        return new OutboxMessage
        {
            Metadata = metadata,
            Payload = detachedPayload,
            Integrity = OutboxMessageIntegrity.Compute(metadata, detachedPayload)
        };
    }

    private static string ValidateIdentifier(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > OutboxContractLimits.MaxSemanticIdentifierLength)
            throw new ArgumentException($"{name} must be non-blank and at most {OutboxContractLimits.MaxSemanticIdentifierLength} UTF-16 characters.", name);
        return value;
    }
}
