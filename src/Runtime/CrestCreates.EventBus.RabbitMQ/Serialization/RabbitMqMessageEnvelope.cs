using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CrestCreates.EventBus.RabbitMQ.Serialization;

public class RabbitMqMessageEnvelope
{
    public string EventType { get; set; } = string.Empty;
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
