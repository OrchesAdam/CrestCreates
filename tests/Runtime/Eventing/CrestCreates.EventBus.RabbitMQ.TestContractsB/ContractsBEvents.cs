using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event.Abstractions;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.RabbitMq.TestContractsB;

// Same short type name as ContractsA's OrderPlacedEvent, different namespace and different canonical
// identity: the wire must never confuse the two.
[CrestEvent(Name = "billing.invoice-issued", Version = 1, Scope = EventScope.Integration)]
public sealed class OrderPlacedEvent : IDomainEvent
{
    public string OrderId { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTime OccurredOn { get; set; } = DateTime.UtcNow;
}

public static class RabbitMqTestHooksB
{
    private static readonly object Gate = new();
    private static readonly List<string> Entries = new();

    public static Func<OrderPlacedEvent, CancellationToken, Task>? OnInvoiceIssued { get; set; }

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

        OnInvoiceIssued = null;
    }
}

public sealed class InvoiceIssuedHandler
{
    public const string Queue = "itest127.b.invoices";
    public const string RoutingKey = "billing.invoice_issued_legacy";

    [RabbitMqSubscribe("billing.invoice-issued", Queue = Queue, RoutingKey = RoutingKey)]
    public async Task HandleAsync(OrderPlacedEvent @event, CancellationToken cancellationToken)
    {
        RabbitMqTestHooksB.Record($"invoice:{@event.OrderId}:{@event.Total}");
        var hook = RabbitMqTestHooksB.OnInvoiceIssued;
        if (hook is not null)
        {
            await hook(@event, cancellationToken).ConfigureAwait(false);
        }
    }
}
