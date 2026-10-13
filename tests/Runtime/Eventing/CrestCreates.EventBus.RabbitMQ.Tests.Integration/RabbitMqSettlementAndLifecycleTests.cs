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
    public async Task Real_connection_drop_recovers_topology_and_consumption_without_duplicate_registration()
    {
        var host = await StartHostAsync();

        var bus = host.Services.GetRequiredService<RabbitMqEventBus>();
        await bus.PublishAsync(new OrderPlacedEvent { OrderId = "recover-before", Amount = 1m });
        (await WaitUntilAsync(
            () => Task.FromResult(RabbitMqTestHooks.CallLog.Any(e => e.StartsWith("order:recover-before:"))),
            TimeSpan.FromSeconds(10))).Should().BeTrue();

        // Force-close the broker-side connection (management API); SDK recovery must restore topology
        // and consumers on the same connection.
        await CloseAllConnectionsAsync(VHost);

        var published = false;
        for (var attempt = 0; attempt < 40 && !published; attempt++)
        {
            try
            {
                await bus.PublishAsync(new OrderPlacedEvent { OrderId = "recover-after", Amount = 2m });
                published = true;
            }
            catch (RabbitMqPublishException)
            {
                await Task.Delay(500);
            }
        }

        published.Should().BeTrue("the publisher must recover after the forced disconnect");

        (await WaitUntilAsync(
            () => Task.FromResult(RabbitMqTestHooks.CallLog.Any(e => e.StartsWith("order:recover-after:"))),
            TimeSpan.FromSeconds(30))).Should().BeTrue("consumption must recover after the connection drop");

        RabbitMqTestHooks.CallLog.Count(e => e.StartsWith("order:recover-after:")).Should().Be(1,
            "recovery must not register a duplicate consumer");
    }
}
