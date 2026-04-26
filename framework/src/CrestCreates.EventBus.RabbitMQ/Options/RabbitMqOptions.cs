namespace CrestCreates.EventBus.RabbitMQ.Options;

public class RabbitMqOptions
{
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public int MaxChannels { get; set; } = 10;
    public int RetryCount { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 5;
    public string DeadLetterExchange { get; set; } = "crestcreates.dlx";
    public string DefaultExchange { get; set; } = "crestcreates.events";
    public int PublisherConfirmTimeoutSeconds { get; set; } = 30;
}
