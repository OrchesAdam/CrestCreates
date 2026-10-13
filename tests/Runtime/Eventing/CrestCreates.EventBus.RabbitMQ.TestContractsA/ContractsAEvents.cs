using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event.Abstractions;

namespace CrestCreates.RabbitMq.TestContractsA;

[CrestEvent(Name = "orders.order-placed", Version = 1, Scope = EventScope.Integration,
    Importance = EventImportance.Business)]
public sealed class OrderPlacedEvent : IDomainEvent
{
    public string OrderId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public List<string> Tags { get; set; } = new();
    public DateTime OccurredOn { get; set; } = DateTime.UtcNow;
}

[CrestEvent(Name = "inventory.stock-reserved", Version = 1, Scope = EventScope.Integration)]
public sealed class StockReservedEvent : IDomainEvent
{
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateTime OccurredOn { get; set; } = DateTime.UtcNow;
}

/// <summary>Scoped probe proving that every delivery executes handlers inside its own DI scope.</summary>
public sealed class RabbitMqScopedProbe
{
    public Guid Id { get; } = Guid.NewGuid();
}
