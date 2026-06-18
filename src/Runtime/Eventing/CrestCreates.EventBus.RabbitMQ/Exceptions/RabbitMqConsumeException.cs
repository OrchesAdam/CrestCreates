using System;

namespace CrestCreates.EventBus.RabbitMQ.Exceptions;

public class RabbitMqConsumeException : Exception
{
    public string? EventType { get; }
    public string? CorrelationId { get; }
    public int RetryCount { get; }

    public RabbitMqConsumeException(string message) : base(message) { }

    public RabbitMqConsumeException(string message, Exception innerException)
        : base(message, innerException) { }

    public RabbitMqConsumeException(string message, string? eventType, string? correlationId, int retryCount = 0, Exception? innerException = null)
        : base(message, innerException)
    {
        EventType = eventType;
        CorrelationId = correlationId;
        RetryCount = retryCount;
    }
}
