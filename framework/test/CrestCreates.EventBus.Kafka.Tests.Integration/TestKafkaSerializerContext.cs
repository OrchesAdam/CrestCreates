using System.Text.Json.Serialization;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Kafka.Serialization;

namespace CrestCreates.EventBus.Kafka.Tests.Integration;

[JsonSerializable(typeof(TestKafkaEvent))]
[JsonSerializable(typeof(KafkaMultiTestEvent))]
[JsonSerializable(typeof(KafkaRetryTestEvent))]
[JsonSerializable(typeof(KafkaDLQTestEvent))]
[JsonSerializable(typeof(KafkaMessageEnvelope))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class TestKafkaSerializerContext : JsonSerializerContext
{
}

public sealed class TestKafkaEvent : DomainEvent
{
    public string Message { get; set; } = string.Empty;
}

public sealed class KafkaMultiTestEvent : DomainEvent
{
    public string Message { get; set; } = string.Empty;
}

public sealed class KafkaRetryTestEvent : DomainEvent
{
    public string Message { get; set; } = string.Empty;
}

public sealed class KafkaDLQTestEvent : DomainEvent
{
    public string Message { get; set; } = string.Empty;
}
