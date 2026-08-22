using CrestCreates.Runtime.Delivery.Abstractions.Contracts;
using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace CrestCreates.Runtime.Delivery.Message;

public sealed class DefaultOutboxMessageFactory : IOutboxMessageFactory
{
    public OutboxMessage Create<TPayload>(OutboxMessageMetadata metadata, TPayload payload, JsonTypeInfo<TPayload> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        ValidateIdentifier(metadata.MessageId, nameof(metadata.MessageId));
        ValidateIdentifier(metadata.ContractId, nameof(metadata.ContractId));
        ValidateIdentifier(metadata.EventName, nameof(metadata.EventName));
        if (metadata.EventVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(metadata.EventVersion));
        if (metadata.RequiredConsumerIds.Count > OutboxContractLimits.MaxRequiredConsumerCount)
            throw new ArgumentOutOfRangeException(nameof(metadata.RequiredConsumerIds));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, jsonTypeInfo);
        if (bytes.Length is < OutboxContractLimits.MinPayloadBytes or > OutboxContractLimits.MaxPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(payload));
        return CreateCore(metadata, bytes);
    }

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

        var timestamp = createdAt ?? DateTimeOffset.UtcNow;
        var metadata = new OutboxMessageMetadata
        {
            MessageId = messageId,
            TenantId = string.IsNullOrWhiteSpace(tenantId) ? null : tenantId,
            ContractId = contractId,
            PayloadTypeId = payloadTypeId,
            RequiredConsumerIds = consumers,
            CreatedAt = timestamp,
            EventName = contractId,
            EventVersion = 1,
            OccurredAt = timestamp
        };
        var detachedPayload = payload.ToArray();
        return CreateCore(metadata, detachedPayload);
    }

    private static OutboxMessage CreateCore(OutboxMessageMetadata metadata, byte[] detachedPayload)
    {
        var createdAt = metadata.CreatedAt == default ? DateTimeOffset.UtcNow : metadata.CreatedAt;
        var normalized = metadata with
        {
            CreatedAt = createdAt,
            EventName = string.IsNullOrWhiteSpace(metadata.EventName) ? metadata.ContractId : metadata.EventName,
            EventVersion = metadata.EventVersion <= 0 ? 1 : metadata.EventVersion,
            OccurredAt = metadata.OccurredAt == default ? createdAt : metadata.OccurredAt,
            RequiredConsumerIds = metadata.RequiredConsumerIds.Order(StringComparer.Ordinal).Distinct(StringComparer.Ordinal).ToArray()
        };
        return new OutboxMessage
        {
            Metadata = normalized,
            Payload = detachedPayload,
            Integrity = OutboxMessageIntegrity.Compute(normalized, detachedPayload)
        };
    }

    private static string ValidateIdentifier(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > OutboxContractLimits.MaxSemanticIdentifierLength)
            throw new ArgumentException($"{name} must be non-blank and at most {OutboxContractLimits.MaxSemanticIdentifierLength} UTF-16 characters.", name);
        return value;
    }
}
