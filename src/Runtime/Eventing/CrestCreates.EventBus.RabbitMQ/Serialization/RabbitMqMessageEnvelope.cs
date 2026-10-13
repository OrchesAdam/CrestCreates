using System.Text.Json.Serialization;

namespace CrestCreates.EventBus.RabbitMQ.Serialization;

/// <summary>
/// Wire envelope. The 2026-10 static-binding revision is additive: <see cref="EventId"/> and
/// <see cref="EventVersion"/> are written on new-format messages and absent on legacy messages that
/// resolve exclusively through the explicit static alias table.
/// </summary>
public class RabbitMqMessageEnvelope
{
    public string EventType { get; set; } = string.Empty;

    public string? EventId { get; set; }

    public int? EventVersion { get; set; }

    public string Payload { get; set; } = string.Empty;

    public Dictionary<string, string?> Headers { get; set; } = new();

    public DateTime Timestamp { get; set; }

    public RabbitMqMessageEnvelope() { }

    public RabbitMqMessageEnvelope(string eventType, string payload, Dictionary<string, string?>? headers = null)
    {
        EventType = eventType;
        Payload = payload;
        Headers = headers ?? new Dictionary<string, string?>();
        Timestamp = DateTime.UtcNow;
    }
}

[JsonSerializable(typeof(RabbitMqMessageEnvelope))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class RabbitMqMessageEnvelopeContext : JsonSerializerContext
{
}
