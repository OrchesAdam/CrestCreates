using CrestCreates.EventBus.RabbitMQ.Connection;
using RabbitMQ.Client;
using CrestCreates.EventBus.RabbitMQ.Exceptions;
using CrestCreates.RabbitMq.TestContractsA;
using CrestCreates.RabbitMq.TestHandlersA;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace CrestCreates.EventBus.RabbitMQ.Tests.Integration;

/// <summary>
/// Settlement and lifecycle boundaries: typed publish failure kinds, MaxChannels=1 redelivery without
/// self-lock, shutdown leaving in-flight deliveries unacknowledged (redelivered after restart), and
/// consumption recovery after a real connection drop caused through the management API.
/// </summary>
public class RabbitMqSettlementAndLifecycleTests : RabbitMqIntegrationTestBase
{
    public RabbitMqSettlementAndLifecycleTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task Publish_with_no_bound_queue_reports_typed_unroutable_failure()
    {
        // Publisher-only host: nothing consumes orders.order-placed in this vhost, so the mandatory
        // publish is returned by the broker; the failure kind must be distinguishable.
        var host = await StartHostAsync(
            builder => builder.PublisherOnly().AddPublishedEvent<OrderPlacedEvent>(),
            handlersA: false,
            handlersB: false,
            contractB: false,
            waitForQueues: []);

        var bus = host.Services.GetRequiredService<RabbitMqEventBus>();
        var failure = await FluentActions.Awaiting(() =>
                bus.PublishAsync(new OrderPlacedEvent { OrderId = "unroutable-1", Amount = 1m }))
            .Should().ThrowAsync<RabbitMqPublishException>();

        failure.Which.FailureKind.Should().Be(RabbitMqPublishFailureKind.Unroutable);
    }

    [Fact]
    public async Task Max_channels_one_completes_consume_and_redelivery_without_self_lock()
    {
        var host = await StartHostAsync(options: o =>
        {
            o.MaxChannels = 1;
            o.RetryCount = 1;
            o.RetryDelaySeconds = 0;
        });

        var attempts = 0;
        RabbitMqTestHooks.OnOrderPlaced = (_, _) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                throw new InvalidOperationException("first attempt fails");
            }

            return Task.CompletedTask;
        };

        await host.Services.GetRequiredService<RabbitMqEventBus>().PublishAsync(
            new OrderPlacedEvent { OrderId = "single-channel-1", Amount = 1m });

        (await WaitUntilAsync(
            () => Task.FromResult(Volatile.Read(ref attempts) == 2),
            TimeSpan.FromSeconds(15))).Should().BeTrue(
            "the dedicated consumer channel must not consume the publisher pool permit");
    }

    [Fact]
    public async Task Shutdown_leaves_in_flight_deliveries_unacknowledged_and_restart_consumes_them()
    {
        var first = await StartHostAsync(options: o => o.RetryCount = 0);

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RabbitMqTestHooks.OnOrderPlaced = async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        };

        await first.Services.GetRequiredService<RabbitMqEventBus>().PublishAsync(
            new OrderPlacedEvent { OrderId = "inflight-1", Amount = 1m });

        (await Task.WhenAny(entered.Task, Task.Delay(TimeSpan.FromSeconds(10))) == entered.Task)
            .Should().BeTrue("the handler must be running when the host stops");

        await first.StopAsync(CancellationToken.None);

        // The unfinished delivery was never acked; the broker requeues it after the channel closes.
        (await WaitUntilAsync(
            async () => await GetQueueDepthAsync(VHost, QueueNames.Orders, "messages_ready") == 1,
            TimeSpan.FromSeconds(15))).Should().BeTrue("the unfinished delivery must stay on the queue");

        RabbitMqTestHooks.OnOrderPlaced = null;
        var second = await StartHostAsync(options: o => o.RetryCount = 0);

        (await WaitUntilAsync(
            () => Task.FromResult(RabbitMqTestHooks.CallLog.Count(e => e.StartsWith("order:inflight-1:")) >= 2),
            TimeSpan.FromSeconds(15))).Should().BeTrue("the restarted consumer must process the requeued delivery");

        (await WaitUntilAsync(
            async () => await GetQueueDepthAsync(VHost, QueueNames.Orders, "messages") == 0,
            TimeSpan.FromSeconds(10))).Should().BeTrue();
    }

    [Fact]
    public async Task Terminal_consumer_channel_loss_fails_the_host_instead_of_staying_falsely_ready()
    {
        var host = await StartHostAsync();

        var consumer = host.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<CrestCreates.EventBus.RabbitMQ.Consuming.RabbitMqConsumer>()
            .Single();
        var channelField = typeof(CrestCreates.EventBus.RabbitMQ.Consuming.RabbitMqConsumer)
            .GetField("_consumerChannel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        global::RabbitMQ.Client.IChannel? channel = null;
        (await WaitUntilAsync(
            () => Task.FromResult((channel = (global::RabbitMQ.Client.IChannel?)channelField.GetValue(consumer)) is not null),
            TimeSpan.FromSeconds(10))).Should().BeTrue("the dedicated consumer channel must exist");

        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>()
            .ApplicationStopping.Register(() => stopping.TrySetResult());

        // Closing the channel in place (connection stays healthy) is terminal: the supervisor must
        // fail the host instead of idling forever with no consumer.
        await channel!.CloseAsync(CancellationToken.None);

        (await Task.WhenAny(stopping.Task, Task.Delay(TimeSpan.FromSeconds(15))) == stopping.Task)
            .Should().BeTrue("a permanently lost consumer channel must stop the host, not stay falsely ready");
    }

    [Fact]
    public async Task Connection_drop_between_targeted_close_and_recovery_window_preserves_the_consumer()
    {
        var host = await StartHostAsync();

        var bus = host.Services.GetRequiredService<RabbitMqEventBus>();
        await bus.PublishAsync(new OrderPlacedEvent { OrderId = "recover-before", Amount = 1m });
        (await WaitUntilAsync(
            () => Task.FromResult(RabbitMqTestHooks.CallLog.Any(e => e.StartsWith("order:recover-before:"))),
            TimeSpan.FromSeconds(10))).Should().BeTrue();

        // The target connection must actually exist in management before the close, otherwise a
        // single empty listing would fake a "disconnect" that never happened.
        (await WaitUntilAsync(
            async () => (await ManagementJsonAsync(HttpMethod.Get, "connections"))
                .EnumerateArray().Any(c => c.TryGetProperty("vhost", out var v) && v.GetString() == VHost),
            TimeSpan.FromSeconds(10))).Should().BeTrue("the target connection must appear in management before it is closed");

        var pool = host.Services.GetRequiredService<RabbitMqConnectionPool>();
        var connectionField = typeof(RabbitMqConnectionPool)
            .GetField("_connection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var originalConnection = connectionField.GetValue(pool)!;

        await CloseAllConnectionsAsync(VHost);
        (await WaitUntilAsync(
            () => Task.FromResult(!((global::RabbitMQ.Client.IConnection)originalConnection).IsOpen),
            TimeSpan.FromSeconds(5))).Should().BeTrue("a real drop of the target connection is required");

        // Publish immediately inside the SDK recovery window: the pool must wait for the SDK to
        // recover the SAME connection instead of disposing it (which would strand the consumer).
        await bus.PublishAsync(new OrderPlacedEvent { OrderId = "recover-during-window", Amount = 2m });

        ReferenceEquals(originalConnection, connectionField.GetValue(pool)).Should().BeTrue(
            "the connection that carries the consumer must be retained across the recovery window");

        (await WaitUntilAsync(
            () => Task.FromResult(RabbitMqTestHooks.CallLog.Any(e => e.StartsWith("order:recover-during-window:"))),
            TimeSpan.FromSeconds(20))).Should().BeTrue("consumption must survive a publish inside the recovery window");

        RabbitMqTestHooks.CallLog.Count(e => e.StartsWith("order:recover-during-window:")).Should().Be(1,
            "recovery must not register a duplicate consumer");

        // Full recovery cycle: later publishes and consumption keep working on the same connection.
        await bus.PublishAsync(new OrderPlacedEvent { OrderId = "recover-after", Amount = 3m });
        (await WaitUntilAsync(
            () => Task.FromResult(RabbitMqTestHooks.CallLog.Any(e => e.StartsWith("order:recover-after:"))),
            TimeSpan.FromSeconds(20))).Should().BeTrue("consumption must recover after the connection drop");
    }
}
