using System.Text.Json.Serialization;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using CrestCreates.EventBus.Kafka.Connection;
using CrestCreates.EventBus.Kafka.Options;
using CrestCreates.EventBus.Kafka.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CrestCreates.TestServices;
using Xunit;

namespace CrestCreates.EventBus.Kafka.Tests.Integration;

[Collection("Kafka broker")]
public abstract class KafkaIntegrationTestBase : IAsyncLifetime
{
    private readonly HashSet<string> _ownedTopics = new(StringComparer.Ordinal);
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

        TrackTopic(Options.DefaultTopic);
        TrackTopic(Options.DefaultTopic + Options.DeadLetterTopicSuffix);

        var serializerContext = TestKafkaSerializerContext.Default;
        var services = new ServiceCollection();
        services.AddSingleton<JsonSerializerContext>(serializerContext);
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(Options));
        services.AddSingleton<KafkaProducerPool>();
        services.AddSingleton<KafkaPublisher>();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        ServiceProvider = services.BuildServiceProvider();
        return Task.CompletedTask;
    }

    protected string TrackTopic(string topic)
    {
        _ownedTopics.Add(topic);
        return topic;
    }

    public virtual Task DisposeAsync() => ExternalTestServices.CleanupAsync([
        () => ServiceProvider is null
            ? Task.CompletedTask
            : ServiceProvider.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)),
        DeleteOwnedTopicsAsync
    ]);

    private async Task DeleteOwnedTopicsAsync()
    {
        if (_ownedTopics.Count == 0)
            return;

        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = Options.BootstrapServers }).Build();
        try
        {
            await admin.DeleteTopicsAsync(_ownedTopics, new DeleteTopicsOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(15),
                OperationTimeout = TimeSpan.FromSeconds(10)
            });
        }
        catch (DeleteTopicsException exception) when (exception.Results.All(result =>
            result.Error.Code is ErrorCode.NoError or ErrorCode.UnknownTopicOrPart))
        {
            // A topic registered before a failed publish may never have been created.
        }
    }
}

[CollectionDefinition("Kafka broker", DisableParallelization = true)]
public sealed class KafkaBrokerCollection : ICollectionFixture<KafkaBrokerFixture> { }

public sealed class KafkaBrokerFixture : IAsyncLifetime
{
    public static string BootstrapServers { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        BootstrapServers = ExternalTestServices.RequiredConnectionString("CREST_KAFKA_TEST_BOOTSTRAP", "Kafka 4");
        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = BootstrapServers,
            SocketTimeoutMs = 2000,
            SocketConnectionSetupTimeoutMs = 2000
        }).Build();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (admin.GetMetadata(TimeSpan.FromSeconds(2)).Brokers.Count > 0)
                    return;
                await Task.Delay(500, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException("Kafka did not become ready within 30 seconds.");
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;
}