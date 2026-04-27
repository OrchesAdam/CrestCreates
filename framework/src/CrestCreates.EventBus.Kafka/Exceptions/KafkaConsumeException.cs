using System;

namespace CrestCreates.EventBus.Kafka.Exceptions;

public class KafkaConsumeException : Exception
{
    public string? Topic { get; }
    public int? Partition { get; }
    public long? Offset { get; }
    public int RetryCount { get; }

    public KafkaConsumeException(string message) : base(message) { }

    public KafkaConsumeException(string message, Exception innerException)
        : base(message, innerException) { }

    public KafkaConsumeException(string message, string? topic, int? partition, long? offset, int retryCount = 0, Exception? innerException = null)
        : base(message, innerException)
    {
        Topic = topic;
        Partition = partition;
        Offset = offset;
        RetryCount = retryCount;
    }
}
