using System.Net.Sockets;
using System.Text.Json.Serialization;
using CrestCreates.EventBus.Kafka.Connection;
using CrestCreates.EventBus.Kafka.Options;
using CrestCreates.EventBus.Kafka.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.EventBus.Kafka.Tests.Integration;

public abstract class KafkaIntegrationTestBase : IAsyncLifetime
{
    protected ServiceProvider ServiceProvider { get; private set; } = null!;
    protected KafkaOptions Options { get; private set; } = new();

    public virtual async Task InitializeAsync()
    {
        if (!await IsKafkaAvailable())
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Kafka is not available. Start it with: docker compose -f infra/docker-compose.eventbus.yml up -d");
        }

        Options = new KafkaOptions
        {
            BootstrapServers = "localhost:9092",
            RetryCount = 2,
            RetryDelaySeconds = 1,
            DeadLetterTopicSuffix = ".test.dlq",
            DefaultTopic = $"crestcreates.test.events.{Guid.NewGuid():N}",
            ConsumerGroupId = $"test-group-{Guid.NewGuid():N}",
            EnableAutoCommit = false,
            ProducerPoolSize = 2
        };

        var serializerContext = new TestKafkaSerializerContext();
        var services = new ServiceCollection();
        services.AddSingleton(serializerContext);
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(Options));
        services.AddSingleton<KafkaProducerPool>();
        services.AddSingleton<KafkaPublisher>();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        ServiceProvider = services.BuildServiceProvider();
    }

    public virtual async Task DisposeAsync()
    {
        if (ServiceProvider is not null)
        {
            await ServiceProvider.DisposeAsync();
        }
    }

    private static async Task<bool> IsKafkaAvailable()
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("localhost", 9092);
            return true;
        }
        catch
        {
            return false;
        }
    }
}