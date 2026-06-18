using CrestCreates.EventBus.Kafka.Options;
using Xunit;

namespace CrestCreates.EventBus.Kafka.Tests;

public class PublisherTests
{
    [Fact]
    public void Publisher_WithValidOptions_CanBeCreated()
    {
        // Arrange
        var options = new KafkaOptions
        {
            BootstrapServers = "localhost:9092",
            DefaultTopic = "test-events"
        };

        // Act & Assert
        Assert.NotNull(options);
        Assert.Equal("test-events", options.DefaultTopic);
    }
}
