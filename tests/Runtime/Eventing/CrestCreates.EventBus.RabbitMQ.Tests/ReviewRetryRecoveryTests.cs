using System.Reflection;
using System.Text;
using CrestCreates.EventBus.RabbitMQ.Composition;
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Consuming;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Publishing;
using CrestCreates.EventBus.RabbitMQ.Registration;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RabbitMQ.Client;
using Xunit;

namespace CrestCreates.EventBus.RabbitMQ.Tests;

public sealed class ReviewRetryRecoveryTests
{
    [Fact]
    public async Task Retry_confirm_timeout_must_schedule_recovery_for_original_unacked_delivery()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RabbitMqOptions { RetryCount = 1, RetryDelaySeconds = 0 });
        using var pool = new RabbitMqConnectionPool(options, NullLogger<RabbitMqConnectionPool>.Instance);
        var publishChannel = new Mock<IChannel>();
        publishChannel.SetupGet(x => x.IsOpen).Returns(true);
        publishChannel.Setup(x => x.BasicPublishAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<BasicProperties>(),
            It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask(Task.FromException(new OperationCanceledException("simulated confirm timeout"))));
        var connection = new Mock<IConnection>();
        connection.SetupGet(x => x.IsOpen).Returns(true);
        connection.Setup(x => x.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(publishChannel.Object);
        typeof(RabbitMqConnectionPool).GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(pool, connection.Object);

        var descriptor = new OrderPlacedDescriptorProvider().GetDescriptors().Single();
        var binding = new RabbitMqPublishBinding(descriptor, FreezeTestJsonContext.Default.OrderPlacedPayload, descriptor.Name, "review");
        var identity = new RabbitMqWireIdentity(descriptor.Id, descriptor.Version);
        var handler = new RabbitMqHandlerBinding(typeof(TestHandler), "HandleAsync", "review",
            (_, _, _) => Task.FromException(new InvalidOperationException("business failure")));
        var plan = new RabbitMqQueuePlan("review.queue", 1, [],
            new Dictionary<RabbitMqWireIdentity, IReadOnlyList<RabbitMqHandlerBinding>> { [identity] = [handler] });
        var registry = new ReviewRegistry(binding, plan);
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();
        var publisher = new RabbitMqPublisher(pool, options, NullLogger<RabbitMqPublisher>.Instance);
        using var consumer = new RabbitMqConsumer(provider, new RabbitMqCompositionReadiness(), pool, publisher,
            options, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<RabbitMqConsumer>.Instance);
        var consumerChannel = new Mock<IChannel>();
        consumerChannel.SetupGet(x => x.IsOpen).Returns(true);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(RabbitMqConsumer).GetField("_registry", flags)!.SetValue(consumer, registry);
        typeof(RabbitMqConsumer).GetField("_consumerChannel", flags)!.SetValue(consumer, consumerChannel.Object);
        typeof(RabbitMqConsumer).GetMethod("BeginDelivery", flags)!.Invoke(consumer, null);

        var envelope = "{\"eventType\":\"orders.order-placed\",\"eventId\":\"" + descriptor.Id +
            "\",\"eventVersion\":1,\"payload\":\"{}\",\"headers\":{}}";
        var deliveryType = typeof(RabbitMqConsumer).GetNestedType("DeliveryContext", BindingFlags.NonPublic)!;
        var delivery = Activator.CreateInstance(deliveryType, plan, Encoding.UTF8.GetBytes(envelope), new BasicProperties(), 1UL)!;
        await (Task)typeof(RabbitMqConsumer).GetMethod("ProcessDeliveryAsync", flags)!
            .Invoke(consumer, [delivery, CancellationToken.None])!;

        publishChannel.Invocations.Should().Contain(x => x.Method.Name == "BasicPublishAsync");
        consumerChannel.Invocations.Should().NotContain(x => x.Method.Name == "BasicAckAsync");
        consumerChannel.Invocations.Should().Contain(x =>
            x.Method.Name == "BasicNackAsync" || x.Method.Name == "CloseAsync" || x.Method.Name == "BasicCancelAsync",
            "a failed retry publish must recover the original delivery rather than leave a healthy consumer channel stalled indefinitely");
    }

    private sealed class ReviewRegistry(RabbitMqPublishBinding binding, RabbitMqQueuePlan plan) : IRabbitMqBindingRegistry
    {
        public bool PublisherOnly => false;
        public IReadOnlyList<RabbitMqQueuePlan> QueuePlans => [plan];
        public IReadOnlyCollection<Type> PublishableEventTypes => [binding.Descriptor.PayloadType];
        public bool TryGetPublishBinding(Type type, out RabbitMqPublishBinding? found) { found = binding; return true; }
        public RabbitMqWireResolution ResolveWire(string name, string? id, int? version) => new(binding, null);
    }
}
