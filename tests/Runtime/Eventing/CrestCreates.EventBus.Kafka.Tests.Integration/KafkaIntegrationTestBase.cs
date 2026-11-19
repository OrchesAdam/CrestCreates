using System.Text.Json.Serialization;
using CrestCreates.EventBus.Kafka.Connection;
using CrestCreates.EventBus.Kafka.Options;
using CrestCreates.EventBus.Kafka.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.EventBus.Kafka.Tests.Integration;

[Collection("Kafka broker")]
public abstract class KafkaIntegrationTestBase : IAsyncLifetime
{
    protected ServiceProvider ServiceProvider { get; private set; } = null!;
    protected KafkaOptions Options { get; private set; } = new();

    public virtual Task InitializeAsync()
    {
        Options = new KafkaOptions
        {
            BootstrapServers = KafkaBrokerFixture.BootstrapServers,
            RetryCount = 2,
            RetryDelaySeconds = 1,
            DeadLetterTopicSuffix = ".test.dlq",
            DefaultTopic = $"crestcreates.test.events.{Guid.NewGuid():N}",
            ConsumerGroupId = $"test-group-{Guid.NewGuid():N}",
            EnableAutoCommit = false,
            ProducerPoolSize = 2
        };

        var serializerContext = TestKafkaSerializerContext.Default;
        var services = new ServiceCollection();
        services.AddSingleton<System.Text.Json.Serialization.JsonSerializerContext>(serializerContext);
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(Options));
        services.AddSingleton<KafkaProducerPool>();
        services.AddSingleton<KafkaPublisher>();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        ServiceProvider = services.BuildServiceProvider();
        return Task.CompletedTask;
    }

    public virtual async Task DisposeAsync()
    {
        if (ServiceProvider is not null)
        {
            await ServiceProvider.DisposeAsync();
        }
    }

}

[CollectionDefinition("Kafka broker", DisableParallelization = true)]
public sealed class KafkaBrokerCollection : ICollectionFixture<KafkaBrokerFixture> { }

public sealed class KafkaBrokerFixture : IAsyncLifetime
{
    public static string BootstrapServers { get; private set; } = null!;

    public Task InitializeAsync()
    {
        BootstrapServers = CrestCreates.TestServices.ExternalTestServices.RequiredConnectionString(
            "CREST_KAFKA_TEST_BOOTSTRAP", "Kafka 4");
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}