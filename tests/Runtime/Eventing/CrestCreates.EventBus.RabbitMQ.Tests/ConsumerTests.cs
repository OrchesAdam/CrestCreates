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
    private static RabbitMqConsumer CreateConsumer(
        IRabbitMqBindingRegistry? registry = null,
        RabbitMqConnectionPool? pool = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RabbitMqOptions());
        pool ??= new RabbitMqConnectionPool(options, NullLogger<RabbitMqConnectionPool>.Instance);
        var publisher = new RabbitMqPublisher(pool, options, NullLogger<RabbitMqPublisher>.Instance);
        var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new RabbitMqConsumer(
            registry ?? new EmptyRegistry(),
            new RabbitMqCompositionReadiness(),
            pool,
            publisher,
            options,
            scopeFactory,
            NullLogger<RabbitMqConsumer>.Instance);
    }

    [Fact]
    public void Constructor_accepts_valid_dependencies()
    {
        using var pool = new RabbitMqConnectionPool(
            Microsoft.Extensions.Options.Options.Create(new RabbitMqOptions()),
            NullLogger<RabbitMqConnectionPool>.Instance);

        var consumer = CreateConsumer(pool: pool);
        consumer.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_returns_immediately_for_publisher_only_registry()
    {
        using var pool = new RabbitMqConnectionPool(
            Microsoft.Extensions.Options.Options.Create(new RabbitMqOptions()),
            NullLogger<RabbitMqConnectionPool>.Instance);

        var consumer = CreateConsumer(new EmptyRegistry(publisherOnly: true), pool);
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

        var options = Microsoft.Extensions.Options.Options.Create(new RabbitMqOptions());
        var publisher = new RabbitMqPublisher(pool, options, NullLogger<RabbitMqPublisher>.Instance);
        var readiness = new RabbitMqCompositionReadiness();
        using var scopeProvider = new ServiceCollection().BuildServiceProvider();
        var scopeFactory = scopeProvider.GetRequiredService<IServiceScopeFactory>();
        var consumer = new RabbitMqConsumer(
            new EmptyRegistry(),
            readiness,
            pool,
            publisher,
            options,
            scopeFactory,
            NullLogger<RabbitMqConsumer>.Instance);

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
