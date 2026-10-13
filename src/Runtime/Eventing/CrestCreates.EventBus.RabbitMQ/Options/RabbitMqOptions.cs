namespace CrestCreates.EventBus.RabbitMQ.Options;

public class RabbitMqOptions
{
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";

    /// <summary>Publisher channel pool size. The dedicated consumer channel is not counted against it.</summary>
    public int MaxChannels { get; set; } = 10;

    public int RetryCount { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 5;

    public string DeadLetterExchange { get; set; } = "crestcreates.dlx";
    public string DefaultExchange { get; set; } = "crestcreates.events";
    public int PublisherConfirmTimeoutSeconds { get; set; } = 30;

    /// <summary>Prefetch applied to a queue plan when the subscription does not declare one.</summary>
    public int DefaultPrefetchCount { get; set; } = 10;

    /// <summary>
    /// Explicit consumer dispatch concurrency for the connection dispatcher (never left to the SDK
    /// default). Delivery callbacks only copy the body and hand off; handler execution concurrency is
    /// bounded by the per-queue prefetch.
    /// </summary>
    public int ConsumerDispatchConcurrency { get; set; } = 1;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(HostName))
            throw new ArgumentException("RabbitMQ HostName must be non-empty.", nameof(HostName));
        if (Port is < 1 or > 65535)
            throw new ArgumentException($"RabbitMQ Port {Port} is outside 1..65535.", nameof(Port));
        if (string.IsNullOrWhiteSpace(VirtualHost))
            throw new ArgumentException("RabbitMQ VirtualHost must be non-empty.", nameof(VirtualHost));
        if (MaxChannels <= 0)
            throw new ArgumentException($"RabbitMQ MaxChannels {MaxChannels} must be greater than 0.", nameof(MaxChannels));
        if (RetryCount < 0)
            throw new ArgumentException($"RabbitMQ RetryCount {RetryCount} must be >= 0.", nameof(RetryCount));
        if (RetryDelaySeconds < 0)
            throw new ArgumentException($"RabbitMQ RetryDelaySeconds {RetryDelaySeconds} must be >= 0.", nameof(RetryDelaySeconds));
        if (PublisherConfirmTimeoutSeconds <= 0)
            throw new ArgumentException(
                $"RabbitMQ PublisherConfirmTimeoutSeconds {PublisherConfirmTimeoutSeconds} must be greater than 0.",
                nameof(PublisherConfirmTimeoutSeconds));
        if (DefaultPrefetchCount is < 1 or > ushort.MaxValue)
            throw new ArgumentException(
                $"RabbitMQ DefaultPrefetchCount {DefaultPrefetchCount} is outside 1..{ushort.MaxValue}.",
                nameof(DefaultPrefetchCount));
        if (ConsumerDispatchConcurrency is < 1 or > ushort.MaxValue)
            throw new ArgumentException(
                $"RabbitMQ ConsumerDispatchConcurrency {ConsumerDispatchConcurrency} must be within 1..{ushort.MaxValue}.",
                nameof(ConsumerDispatchConcurrency));
        if (string.IsNullOrWhiteSpace(DeadLetterExchange))
            throw new ArgumentException("RabbitMQ DeadLetterExchange must be non-empty.", nameof(DeadLetterExchange));
        if (string.IsNullOrWhiteSpace(DefaultExchange))
            throw new ArgumentException("RabbitMQ DefaultExchange must be non-empty.", nameof(DefaultExchange));
    }
}
