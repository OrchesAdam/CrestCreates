using System;

namespace CrestCreates.EventBus.Kafka.Exceptions;

public class KafkaPublishException : Exception
{
    public string? Topic { get; }
    public int? Partition { get; }
    public long? Offset { get; }

    public KafkaPublishException(string message) : base(message) { }

    public KafkaPublishException(string message, Exception innerException)
        : base(message, innerException) { }

    public KafkaPublishException(string message, string? topic, int? partition = null, long? offset = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Topic = topic;
        Partition = partition;
        Offset = offset;
    }
}
