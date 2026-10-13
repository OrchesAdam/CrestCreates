using CrestCreates.EventBus.RabbitMQ.Composition;
using CrestCreates.EventBus.RabbitMQ.Consuming;
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Publishing;
using CrestCreates.EventBus.RabbitMQ.Registration;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrestCreates.EventBus.RabbitMQ.Tests;

public class ConsumerTests
{
    private static ServiceProvider BuildProvider(IRabbitMqBindingRegistry registry)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(registry);
        return services.BuildServiceProvider();
    }

    private RabbitMqConsumer CreateConsumer(RabbitMqConnectionPool pool, ServiceProvider provider, RabbitMqCompositionReadiness readiness)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RabbitMqOptions());
        var publisher = new RabbitMqPublisher(pool, options, NullLogger<RabbitMqPublisher>.Instance);
        return new RabbitMqConsumer(
            provider,
            readiness,
            pool,
            publisher,
            options,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RabbitMqConsumer>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_exits_cleanly_for_publisher_only_registry()
    {
        using var pool = new RabbitMqConnectionPool(
            Microsoft.Extensions.Options.Options.Create(new RabbitMqOptions()),
            NullLogger<RabbitMqConnectionPool>.Instance);
        using var provider = BuildProvider(new EmptyRegistry(publisherOnly: true));
        var readiness = new RabbitMqCompositionReadiness();
        var consumer = CreateConsumer(pool, provider, readiness);

        readiness.SignalReady();
        await consumer.StartAsync(CancellationToken.None);

        await FluentActions.Awaiting(() => consumer.StopAsync(CancellationToken.None))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_observes_a_faulted_composition_gate_and_exits_without_broker_io()
    {
        // The deterministic startup-failure contract lives on the composition hosted service (host
        // StartAsync throws); the consumer only observes the faulted gate and must exit without any
        // broker I/O or hanging shutdown.
        using var pool = new RabbitMqConnectionPool(
            Microsoft.Extensions.Options.Options.Create(new RabbitMqOptions()),
            NullLogger<RabbitMqConnectionPool>.Instance);
        using var provider = BuildProvider(new EmptyRegistry());
        var readiness = new RabbitMqCompositionReadiness();
        var consumer = CreateConsumer(pool, provider, readiness);

        readiness.Fail(new InvalidOperationException("freeze failed"));
        await consumer.StartAsync(CancellationToken.None);

        await FluentActions.Awaiting(() => consumer.StopAsync(CancellationToken.None))
            .Should().NotThrowAsync();
    }

    private sealed class EmptyRegistry : IRabbitMqBindingRegistry
    {
        public EmptyRegistry(bool publisherOnly = false) => PublisherOnly = publisherOnly;

        public bool PublisherOnly { get; }

        public IReadOnlyList<RabbitMqQueuePlan> QueuePlans => [];

        public IReadOnlyCollection<Type> PublishableEventTypes => [];

        public bool TryGetPublishBinding(Type eventType, out RabbitMqPublishBinding? binding)
        {
            binding = null;
            return false;
        }

        public RabbitMqWireResolution ResolveWire(string eventType, string? eventId, int? eventVersion)
            => new(null, "empty registry");
    }
}
