using System.Security.Cryptography;
using System.Text.Json;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Runtime.Delivery.Abstractions.Messages;

public static class OutboxMessageIntegrity
{
    public static CanonicalHash Compute(OutboxMessageMetadata metadata, ReadOnlySpan<byte> payload)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("messageId", metadata.MessageId);
            if (metadata.TenantId is null) writer.WriteNull("tenantId"); else writer.WriteString("tenantId", metadata.TenantId);
            writer.WriteString("contractId", metadata.ContractId);
            writer.WriteString("eventName", metadata.EventName);
            writer.WriteNumber("eventVersion", metadata.EventVersion);
            if (metadata.CorrelationId is null) writer.WriteNull("correlationId"); else writer.WriteString("correlationId", metadata.CorrelationId);
            if (metadata.CausationId is null) writer.WriteNull("causationId"); else writer.WriteString("causationId", metadata.CausationId);
            // Runtime providers commonly persist timestamps at microsecond
            // precision (for example PostgreSQL timestamptz). Normalize the
            // signed payload before hashing so a durable round-trip remains
            // verifiable without weakening the rest of the envelope hash.
            var occurredAt = (metadata.OccurredAt == default ? metadata.CreatedAt : metadata.OccurredAt).ToUniversalTime();
            occurredAt = occurredAt.AddTicks(-(occurredAt.Ticks % TimeSpan.TicksPerMicrosecond));
            writer.WriteString("occurredAt", occurredAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            writer.WritePropertyName("requiredConsumerIds");
            writer.WriteStartArray();
            foreach (var id in metadata.RequiredConsumerIds.Order(StringComparer.Ordinal)) writer.WriteStringValue(id);
            writer.WriteEndArray();
            writer.WritePropertyName("payload");
            writer.WriteBase64StringValue(payload);
            writer.WriteEndObject();
        }
        return new CanonicalHash
        {
            Value = Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant(),
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = "RuntimeOutboxMessage",
            Scope = "InternalFull",
            Purpose = "Integrity",
            ContractVersion = "canonical-hash-v1",
            CanonicalShapeVersion = "runtime-outbox-message-v1"
        };
    }

    public static bool Matches(OutboxMessage message)
        => message.Integrity is not null
            && string.Equals(message.Integrity.Value, Compute(message.Metadata, message.Payload).Value, StringComparison.Ordinal)
            && string.Equals(message.Integrity.ArtifactKind, "RuntimeOutboxMessage", StringComparison.Ordinal)
            && string.Equals(message.Integrity.Purpose, "Integrity", StringComparison.Ordinal)
            && string.Equals(message.Integrity.Algorithm, "SHA-256", StringComparison.Ordinal)
            && string.Equals(message.Integrity.AlgorithmVersion, "sha256-canonical-json-v1", StringComparison.Ordinal)
            && string.Equals(message.Integrity.Scope, "InternalFull", StringComparison.Ordinal)
            && string.Equals(message.Integrity.ContractVersion, "canonical-hash-v1", StringComparison.Ordinal)
            && string.Equals(message.Integrity.CanonicalShapeVersion, "runtime-outbox-message-v1", StringComparison.Ordinal);
}
