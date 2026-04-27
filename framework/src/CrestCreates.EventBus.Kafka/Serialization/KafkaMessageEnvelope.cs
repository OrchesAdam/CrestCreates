using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CrestCreates.EventBus.Kafka.Serialization;

public class KafkaMessageEnvelope
{
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public Dictionary<string, string?> Headers { get; set; } = new();
    public DateTime Timestamp { get; set; }
    public string? CorrelationId { get; set; }
    public int RetryCount { get; set; }

    public KafkaMessageEnvelope() { }

    public KafkaMessageEnvelope(string eventType, string payload, Dictionary<string, string?>? headers = null)
    {
        EventType = eventType;
        Payload = payload;
        Headers = headers ?? new Dictionary<string, string?>();
        Timestamp = DateTime.UtcNow;
        CorrelationId = Guid.NewGuid().ToString();
        RetryCount = 0;
    }
}

[JsonSerializable(typeof(KafkaMessageEnvelope))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class KafkaMessageEnvelopeContext : JsonSerializerContext
{
}
