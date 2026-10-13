using System;

namespace CrestCreates.EventBus.RabbitMQ.Exceptions;

/// <summary>Distinguished publish failure kinds; never collapsed into a fake success.</summary>
public enum RabbitMqPublishFailureKind
{
    /// <summary>The broker could not route the mandatory publish (PublishReturnException).</summary>
    Unroutable,

    /// <summary>The broker nacked the publish (PublishNackException).</summary>
    Nacked,

    /// <summary>The confirm wait exceeded PublisherConfirmTimeoutSeconds.</summary>
    Timeout,

    /// <summary>The connection or channel was interrupted; the outcome is indeterminate.</summary>
    Interrupted,

    /// <summary>Any other publish failure.</summary>
    Failed,
}

public class RabbitMqPublishException : Exception
{
    public string? EventType { get; }
    public string? CorrelationId { get; }
    public RabbitMqPublishFailureKind FailureKind { get; }

    public RabbitMqPublishException(string message) : base(message) { }

    public RabbitMqPublishException(string message, Exception innerException)
        : base(message, innerException) { }

    public RabbitMqPublishException(
        string message,
        string? eventType,
        string? correlationId,
        Exception? innerException = null,
        RabbitMqPublishFailureKind failureKind = RabbitMqPublishFailureKind.Failed)
        : base(message, innerException)
    {
        EventType = eventType;
        CorrelationId = correlationId;
        FailureKind = failureKind;
    }
}
