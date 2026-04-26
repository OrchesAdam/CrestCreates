using System;

namespace CrestCreates.EventBus.RabbitMQ.Exceptions;

public class RabbitMqConnectionException : Exception
{
    public string? HostName { get; }

    public RabbitMqConnectionException(string message) : base(message) { }

    public RabbitMqConnectionException(string message, Exception innerException)
        : base(message, innerException) { }

    public RabbitMqConnectionException(string message, string? hostName, Exception? innerException = null)
        : base(message, innerException)
    {
        HostName = hostName;
    }
}
