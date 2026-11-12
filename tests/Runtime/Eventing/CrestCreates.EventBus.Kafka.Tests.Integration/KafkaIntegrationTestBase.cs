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
    private DotNet.Testcontainers.Containers.IContainer _container = null!;

    public async Task InitializeAsync()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        BootstrapServers = $"localhost:{port}";
        _container = new DotNet.Testcontainers.Builders.ContainerBuilder("apache/kafka:4.0.0")
            .WithPortBinding(port, 9092)
            .WithEnvironment("KAFKA_NODE_ID", "1")
            .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
            .WithEnvironment("KAFKA_LISTENERS", "PLAINTEXT://:9092,CONTROLLER://:9093")
            .WithEnvironment("KAFKA_ADVERTISED_LISTENERS", $"PLAINTEXT://localhost:{port}")
            .WithEnvironment("KAFKA_LISTENER_SECURITY_PROTOCOL_MAP", "CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT")
            .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
            .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", "1@localhost:9093")
            .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_MIN_ISR", "1")
            .WithEnvironment("KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS", "0")
            .WithEnvironment("KAFKA_NUM_PARTITIONS", "3")
            .WithWaitStrategy(DotNet.Testcontainers.Builders.Wait.ForUnixContainer().UntilMessageIsLogged("Kafka Server started"))
            .Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container != null) await _container.DisposeAsync();
    }
}