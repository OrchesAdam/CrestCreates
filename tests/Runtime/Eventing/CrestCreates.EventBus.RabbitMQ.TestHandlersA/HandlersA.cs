using CrestCreates.RabbitMq.TestContractsA;

namespace CrestCreates.RabbitMq.TestHandlersA;

/// <summary>Static test hooks: the integration host sets these before publishing to steer handler behavior.</summary>
public static class RabbitMqTestHooks
{
    private static readonly object Gate = new();
    private static readonly List<string> Entries = new();

    public static Func<OrderPlacedEvent, CancellationToken, Task>? OnOrderPlaced { get; set; }

    public static Func<StockReservedEvent, CancellationToken, Task>? OnStockReserved { get; set; }

    public static IReadOnlyList<string> CallLog
    {
        get
        {
            lock (Gate)
            {
                return Entries.ToArray();
            }
        }
    }

    public static void Record(string entry)
    {
        lock (Gate)
        {
            Entries.Add(entry);
        }
    }

    public static void Reset()
    {
        lock (Gate)
        {
            Entries.Clear();
        }

        OnOrderPlaced = null;
        OnStockReserved = null;
    }
}

public static class QueueNames
{
    public const string Orders = "itest127.a.orders";
    public const string Inventory = "itest127.a.inventory";
}

public sealed class OrderPlacedAuditHandler(RabbitMqScopedProbe probe)
{
    [CrestCreates.Domain.Shared.Attributes.RabbitMqSubscribe("orders.order-placed", Queue = QueueNames.Orders)]
    public Task HandleAsync(OrderPlacedEvent @event, CancellationToken cancellationToken)
    {
        RabbitMqTestHooks.Record($"audit:{@event.OrderId}:{probe.Id}");
        return Task.CompletedTask;
    }
}

public sealed class OrderPlacedHandler(RabbitMqScopedProbe probe)
{
    [CrestCreates.Domain.Shared.Attributes.RabbitMqSubscribe("orders.order-placed", Queue = QueueNames.Orders)]
    public async Task HandleAsync(OrderPlacedEvent @event, CancellationToken cancellationToken)
    {
        RabbitMqTestHooks.Record($"order:{@event.OrderId}:{probe.Id}");
        var hook = RabbitMqTestHooks.OnOrderPlaced;
        if (hook is not null)
        {
            await hook(@event, cancellationToken).ConfigureAwait(false);
        }
    }
}

public sealed class StockReservedHandler(RabbitMqScopedProbe probe)
{
    [CrestCreates.Domain.Shared.Attributes.RabbitMqSubscribe("inventory.stock-reserved", Queue = QueueNames.Orders)]
    public async Task HandleAsync(StockReservedEvent @event, CancellationToken cancellationToken)
    {
        RabbitMqTestHooks.Record($"stock:{@event.Sku}:{probe.Id}");
        var hook = RabbitMqTestHooks.OnStockReserved;
        if (hook is not null)
        {
            await hook(@event, cancellationToken).ConfigureAwait(false);
        }
    }
}

public sealed class StockReservedMirrorHandler(RabbitMqScopedProbe probe)
{
    [CrestCreates.Domain.Shared.Attributes.RabbitMqSubscribe("inventory.stock-reserved", Queue = QueueNames.Inventory)]
    public Task HandleAsync(StockReservedEvent @event, CancellationToken cancellationToken)
    {
        RabbitMqTestHooks.Record($"mirror:{@event.Sku}:{probe.Id}");
        return Task.CompletedTask;
    }
}
