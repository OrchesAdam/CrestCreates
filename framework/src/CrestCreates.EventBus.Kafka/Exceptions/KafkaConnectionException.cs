using System;

namespace CrestCreates.EventBus.Kafka.Exceptions;

public class KafkaConnectionException : Exception
{
    public string? BootstrapServers { get; }

    public KafkaConnectionException(string message) : base(message) { }

    public KafkaConnectionException(string message, Exception innerException)
        : base(message, innerException) { }

    public KafkaConnectionException(string message, string? bootstrapServers, Exception? innerException = null)
        : base(message, innerException)
    {
        BootstrapServers = bootstrapServers;
    }
}
