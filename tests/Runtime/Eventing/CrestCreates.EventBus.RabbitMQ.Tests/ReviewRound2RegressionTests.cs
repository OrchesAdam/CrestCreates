using System.Reflection;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.RabbitMQ.Composition;
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Consuming;
using CrestCreates.EventBus.RabbitMQ.Extensions;
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

public sealed class ReviewRound2RegressionTests
{
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void Different_codec_factories_with_same_origin_must_not_bypass_type_validation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventDescriptorProvider, OrderPlacedDescriptorProvider>();
        services.AddSingleton(new EventJsonCodecRegistration {
            Origin = "Same.Owner", PayloadType = typeof(OrderPlacedPayload),
            CodecFactory = () => FreezeTestJsonContext.Default.OrderPlacedPayload });
        services.AddSingleton(new EventJsonCodecRegistration {
            Origin = "Same.Owner", PayloadType = typeof(OrderPlacedPayload),
            CodecFactory = () => FreezeTestJsonContext.Default.InvoiceIssuedPayload });
        services.AddRabbitMqEventBus(builder: b => b.PublisherOnly().RequirePublishableEvent<OrderPlacedPayload>());
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<EventRegistry>().Build(provider.GetServices<IEventDescriptorProvider>());
        FluentActions.Invoking(() => provider.GetRequiredService<IRabbitMqBindingRegistry>())
            .Should().Throw<RabbitMqCompositionException>("same origin does not prove two different factories describe an identical contract");
    }

    [Fact]
    public async Task Cancellation_during_recovery_wait_must_preserve_OperationCanceledException()
    {
        using var pool = new RabbitMqConnectionPool(
            Microsoft.Extensions.Options.Options.Create(new RabbitMqOptions()), NullLogger<RabbitMqConnectionPool>.Instance);
        var connection = new Mock<IConnection>();
        connection.SetupGet(x => x.IsOpen).Returns(false);
        typeof(RabbitMqConnectionPool).GetField("_connection", PrivateInstance)!.SetValue(pool, connection.Object);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await FluentActions.Awaiting(() => pool.GetChannelAsync(cancel.Token))
            .Should().ThrowAsync<OperationCanceledException>("caller cancellation must not become a connection failure");
    }

    [Fact]
    public async Task Supervisor_must_allow_channel_recovery_after_connection_reopens()
    {
        var connectionOpen = false;
        var channelOpen = false;
        var options = Microsoft.Extensions.Options.Options.Create(new RabbitMqOptions());
        using var pool = new RabbitMqConnectionPool(options, NullLogger<RabbitMqConnectionPool>.Instance);
        var connection = new Mock<IConnection>();
        connection.SetupGet(x => x.IsOpen).Returns(() => Volatile.Read(ref connectionOpen));
        typeof(RabbitMqConnectionPool).GetField("_connection", PrivateInstance)!.SetValue(pool, connection.Object);
        var channel = new Mock<IChannel>();
        channel.SetupGet(x => x.IsOpen).Returns(() => Volatile.Read(ref channelOpen));
        using var provider = new ServiceCollection().BuildServiceProvider();
        var publisher = new RabbitMqPublisher(pool, options, NullLogger<RabbitMqPublisher>.Instance);
        using var consumer = new RabbitMqConsumer(provider, new RabbitMqCompositionReadiness(), pool, publisher, options,
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<RabbitMqConsumer>.Instance);
        typeof(RabbitMqConsumer).GetField("_consumerChannel", PrivateInstance)!.SetValue(consumer, channel.Object);
        using var cancel = new CancellationTokenSource();
        var supervision = (Task)typeof(RabbitMqConsumer).GetMethod("SuperviseConsumerChannelAsync", PrivateInstance)!
            .Invoke(consumer, [cancel.Token])!;
        try
        {
            // At t=2s the supervisor observes a real outage. The SDK then reopens the connection,
            // and completes topology/channel recovery later, well inside the declared 30s grace.
            await Task.Delay(2500);
            Volatile.Write(ref connectionOpen, true);
            await Task.Delay(2000);
            Volatile.Write(ref channelOpen, true);
            supervision.IsFaulted.Should().BeFalse("connection.IsOpen is true before SDK topology/channel recovery completes");
        }
        finally
        {
            cancel.Cancel();
            try { await supervision; } catch { }
        }
    }
}
