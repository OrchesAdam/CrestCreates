using System;

namespace CrestCreates.EventBus.Kafka.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class KafkaSubscribeAttribute : Attribute
{
    public string Topic { get; }
    public string? ConsumerGroup { get; set; }
    public int MaxPollRecords { get; set; } = 500;

    public KafkaSubscribeAttribute(string topic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        Topic = topic;
    }
}
