using CrestCreates.RabbitMq.TestContractsA;
using CrestCreates.RabbitMq.TestHandlersA;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace CrestCreates.EventBus.RabbitMQ.Tests.Integration;

/// <summary>
/// Normal-path matrix on the official mainline: the host is built through AddRabbitMqEventBus plus the
/// generated activation extensions, publishers go through the official event bus and every assertion
/// observes handler side effects or broker queue state (never a hand-rolled consumer).
/// </summary>
public class RabbitMqStaticHostTests : RabbitMqIntegrationTestBase
{
    public RabbitMqStaticHostTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task Publishes_and_consumes_typed_payloads_with_nested_values()
    {
        var host = await StartHostAsync();
        var bus = host.Services.GetRequiredService<RabbitMqEventBus>();

        await bus.PublishAsync(new OrderPlacedEvent
        {
            OrderId = "order-1",
            Amount = 19.95m,
            Tags = ["priority", "eu"],
        });

        (await WaitUntilAsync(
            () => Task.FromResult(RabbitMqTestHooks.CallLog.Any(e => e.StartsWith("order:order-1:"))),
            TimeSpan.FromSeconds(10))).Should().BeTrue("the handler must observe the typed payload");

        RabbitMqTestHooks.CallLog.Should().Contain(e => e.StartsWith("audit:order-1:"));
        RabbitMqTestHooks.CallLog.Should().Contain(e => e.StartsWith("order:order-1:"));

        (await GetQueueDepthAsync(VHost, QueueNames.Orders, "messages")).Should().Be(0);
    }

    [Fact]
    public async Task Same_short_name_events_from_two_assemblies_do_not_cross()
    {
        var host = await StartHostAsync();

        // ContractsA.OrderPlacedEvent ("orders.order-placed") vs ContractsB.OrderPlacedEvent
        // ("billing.invoice-issued"): identical short type names must never cross-deliver.
        await host.Services.GetRequiredService<RabbitMqEventBus>().PublishAsync(new OrderPlacedEvent
        {
            OrderId = "a-1",
            Amount = 1m,
        });

        (await WaitUntilAsync(
            () => Task.FromResult(RabbitMqTestHooks.CallLog.Any(e => e.StartsWith("order:a-1:"))),
            TimeSpan.FromSeconds(10))).Should().BeTrue();

        CrestCreates.RabbitMq.TestContractsB.RabbitMqTestHooksB.CallLog.Should().BeEmpty("the ContractsB handler must not receive ContractsA's event");

        await host.Services.GetRequiredService<RabbitMqEventBus>().PublishAsync(
            new CrestCreates.RabbitMq.TestContractsB.OrderPlacedEvent { OrderId = "b-1", Total = 2m });

        (await WaitUntilAsync(
            () => Task.FromResult(CrestCreates.RabbitMq.TestContractsB.RabbitMqTestHooksB.CallLog.Any(e => e.StartsWith("invoice:b-1:"))),
            TimeSpan.FromSeconds(10))).Should().BeTrue();

        RabbitMqTestHooks.CallLog.Should().NotContain(e => e.StartsWith("order:b-1:"));
        RabbitMqTestHooks.CallLog.Should().NotContain(e => e.StartsWith("audit:b-1:"));
    }

    [Fact]
    public async Task Same_queue_handlers_run_sequentially_once_each_in_stable_order()
    {
        var host = await StartHostAsync();

        await host.Services.GetRequiredService<RabbitMqEventBus>().PublishAsync(new OrderPlacedEvent
        {
            OrderId = "seq-1",
            Amount = 3m,
        });

        (await WaitUntilAsync(
            () => Task.FromResult(RabbitMqTestHooks.CallLog.Count(e => e.Contains(":seq-1:")) == 2),
            TimeSpan.FromSeconds(10))).Should().BeTrue();

        var entries = RabbitMqTestHooks.CallLog.Where(e => e.Contains(":seq-1:")).ToList();
        entries.Should().HaveCount(2);
        entries[0].Should().StartWith("audit:", "handlers are ordered by (origin, handler type, method)");
        entries[1].Should().StartWith("order:");
    }

    [Fact]
    public async Task Different_queues_each_receive_their_own_copy()
    {
        var host = await StartHostAsync();

        await host.Services.GetRequiredService<RabbitMqEventBus>().PublishAsync(new StockReservedEvent
        {
            Sku = "sku-1",
            Quantity = 5,
        });

        (await WaitUntilAsync(
            () => Task.FromResult(
                RabbitMqTestHooks.CallLog.Any(e => e.StartsWith("stock:sku-1:")) &&
                RabbitMqTestHooks.CallLog.Any(e => e.StartsWith("mirror:sku-1:"))),
            TimeSpan.FromSeconds(10))).Should().BeTrue();
    }

    [Fact]
    public async Task Each_delivery_executes_handlers_in_its_own_async_scope()
    {
        var host = await StartHostAsync();
        var bus = host.Services.GetRequiredService<RabbitMqEventBus>();

        await bus.PublishAsync(new OrderPlacedEvent { OrderId = "scope-1", Amount = 1m });
        (await WaitUntilAsync(
            () => Task.FromResult(RabbitMqTestHooks.CallLog.Any(e => e.StartsWith("order:scope-1:"))),
            TimeSpan.FromSeconds(10))).Should().BeTrue();

        await bus.PublishAsync(new OrderPlacedEvent { OrderId = "scope-2", Amount = 1m });
        (await WaitUntilAsync(
            () => Task.FromResult(RabbitMqTestHooks.CallLog.Any(e => e.StartsWith("order:scope-2:"))),
            TimeSpan.FromSeconds(10))).Should().BeTrue();

        var probeIds = RabbitMqTestHooks.CallLog
            .Where(e => e.StartsWith("order:scope-"))
            .Select(e => e.Split(':')[2])
            .ToList();

        probeIds.Should().HaveCount(2);
        probeIds.Distinct().Should().HaveCount(2, "every delivery resolves handlers from a fresh DI scope");

        // Handlers within one delivery share the same scope instance.
        var auditId = RabbitMqTestHooks.CallLog.First(e => e.StartsWith("audit:scope-1:")).Split(':')[2];
        var orderId = RabbitMqTestHooks.CallLog.First(e => e.StartsWith("order:scope-1:")).Split(':')[2];
        orderId.Should().Be(auditId);
    }

    [Fact]
    public async Task Explicit_routing_key_and_default_binding_key_are_declared()
    {
        var host = await StartHostAsync();
        var bus = host.Services.GetRequiredService<RabbitMqEventBus>();

        await bus.PublishAsync(new OrderPlacedEvent { OrderId = "route-1", Amount = 1m });
        (await WaitUntilAsync(
            () => Task.FromResult(RabbitMqTestHooks.CallLog.Any(e => e.StartsWith("order:route-1:"))),
            TimeSpan.FromSeconds(10))).Should().BeTrue();

        // Explicitly configured routing key on both sides (ContractsB) still routes.
        await bus.PublishAsync(new CrestCreates.RabbitMq.TestContractsB.OrderPlacedEvent
        {
            OrderId = "route-b-1",
            Total = 7m,
        });
        (await WaitUntilAsync(
            () => Task.FromResult(CrestCreates.RabbitMq.TestContractsB.RabbitMqTestHooksB.CallLog.Any(e => e.StartsWith("invoice:route-b-1:"))),
            TimeSpan.FromSeconds(10))).Should().BeTrue();
    }
}
