using System;

namespace CrestCreates.EventBus.RabbitMQ.Exceptions;

public class RabbitMqPublishException : Exception
{
    public string? EventType { get; }
    public string? CorrelationId { get; }

    public RabbitMqPublishException(string message) : base(message) { }

    public RabbitMqPublishException(string message, Exception innerException)
        : base(message, innerException) { }

    public RabbitMqPublishException(string message, string? eventType, string? correlationId, Exception? innerException = null)
        : base(message, innerException)
    {
        EventType = eventType;
        CorrelationId = correlationId;
    }
}
