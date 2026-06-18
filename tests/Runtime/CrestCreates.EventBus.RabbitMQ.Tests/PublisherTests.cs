using CrestCreates.EventBus.RabbitMQ.Options;
using Xunit;

namespace CrestCreates.EventBus.RabbitMQ.Tests;

public class PublisherTests
{
    [Fact]
    public void RabbitMqOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new RabbitMqOptions();

        // Assert
        Assert.Equal("localhost", options.HostName);
        Assert.Equal(5672, options.Port);
        Assert.Equal("guest", options.UserName);
        Assert.Equal("guest", options.Password);
        Assert.Equal("/", options.VirtualHost);
        Assert.Equal(10, options.MaxChannels);
        Assert.Equal(3, options.RetryCount);
        Assert.Equal(5, options.RetryDelaySeconds);
        Assert.Equal("crestcreates.dlx", options.DeadLetterExchange);
        Assert.Equal("crestcreates.events", options.DefaultExchange);
        Assert.Equal(30, options.PublisherConfirmTimeoutSeconds);
    }

    [Fact]
    public void RabbitMqOptions_CanBeCustomized()
    {
        // Arrange & Act
        var options = new RabbitMqOptions
        {
            HostName = "rabbitmq.example.com",
            Port = 5671,
            UserName = "admin",
            Password = "secret",
            VirtualHost = "production",
            MaxChannels = 20,
            DefaultExchange = "custom.exchange",
            PublisherConfirmTimeoutSeconds = 60
        };

        // Assert
        Assert.Equal("rabbitmq.example.com", options.HostName);
        Assert.Equal(5671, options.Port);
        Assert.Equal("admin", options.UserName);
        Assert.Equal("secret", options.Password);
        Assert.Equal("production", options.VirtualHost);
        Assert.Equal(20, options.MaxChannels);
        Assert.Equal("custom.exchange", options.DefaultExchange);
        Assert.Equal(60, options.PublisherConfirmTimeoutSeconds);
    }

    [Fact]
    public void Publisher_RequiresRealRabbitMQForFunctionalTests()
    {
        // This test documents that functional tests require a real RabbitMQ instance
        // The RabbitMqConnectionPool is sealed by design for proper resource management
        // Integration tests should be run against a real RabbitMQ broker
        // Unit tests focus on configuration validation and compile-time correctness

        var options = new RabbitMqOptions
        {
            DefaultExchange = "test.exchange",
            PublisherConfirmTimeoutSeconds = 5
        };

        Assert.NotNull(options);
        Assert.Equal("test.exchange", options.DefaultExchange);
    }
}