using CrestCreates.EventBus.Kafka.Options;
using Xunit;

namespace CrestCreates.EventBus.Kafka.Tests;

public class ConsumerTests
{
    [Fact]
    public void Consumer_WithValidOptions_CanBeConfigured()
    {
        // Arrange
        var options = new KafkaOptions
        {
            BootstrapServers = "localhost:9092",
            ConsumerGroupId = "test-consumers",
            EnableAutoCommit = false
        };

        // Act & Assert
        Assert.NotNull(options);
        Assert.Equal("test-consumers", options.ConsumerGroupId);
        Assert.False(options.EnableAutoCommit);
    }
}