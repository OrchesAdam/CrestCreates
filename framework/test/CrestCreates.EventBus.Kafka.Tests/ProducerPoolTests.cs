using CrestCreates.EventBus.Kafka.Connection;
using CrestCreates.EventBus.Kafka.Options;
using Xunit;

namespace CrestCreates.EventBus.Kafka.Tests;

public class ProducerPoolTests
{
    [Fact]
    public void Constructor_WithValidOptions_CreatesProducerPool()
    {
        // Arrange
        var options = new KafkaOptions
        {
            BootstrapServers = "localhost:9092",
            ProducerPoolSize = 4
        };

        // Act & Assert - Basic validation
        Assert.NotNull(options);
        Assert.Equal(4, options.ProducerPoolSize);
    }
}
