using System.Security.Cryptography;
using System.Text.Json;

namespace CrestCreates.Runtime.Delivery.Abstractions.Messages;

public static class OutboxMessageIntegrity
{
    public static byte[] Compute(OutboxMessageMetadata metadata, ReadOnlySpan<byte> payload)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("messageId", metadata.MessageId);
            if (metadata.TenantId is null) writer.WriteNull("tenantId"); else writer.WriteString("tenantId", metadata.TenantId);
            writer.WriteString("contractId", metadata.ContractId);
            writer.WriteString("payloadTypeId", metadata.PayloadTypeId);
            writer.WritePropertyName("requiredConsumerIds");
            writer.WriteStartArray();
            foreach (var id in metadata.RequiredConsumerIds.Order(StringComparer.Ordinal)) writer.WriteStringValue(id);
            writer.WriteEndArray();
            writer.WriteString("createdAt", metadata.CreatedAt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            writer.WritePropertyName("payload");
            writer.WriteBase64StringValue(payload);
            writer.WriteEndObject();
        }
        return SHA256.HashData(stream.ToArray());
    }

    public static bool Matches(OutboxMessage message)
        => CryptographicOperations.FixedTimeEquals(message.Integrity, Compute(message.Metadata, message.Payload));
}
