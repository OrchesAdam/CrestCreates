using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event.Abstractions;
using ErpManagement.Application.Contracts.Events;

namespace ErpManagement.Application.Contracts.Integration;

[CrestEvent(Id = ErpIntegrationWire.MesWorkOrderReleasedId, Name = ErpIntegrationWire.MesWorkOrderReleased,
    Version = ErpIntegrationWire.Version, Scope = EventScope.Integration, RequiresIdempotency = true)]
public sealed record MesWorkOrderReleased(
    Guid WorkOrderId, Guid MaterialId, decimal Quantity, Guid TenantId, DateTimeOffset ReleasedAt) : IErpIntegrationEvent
{
    public string MessageId { get; init; } = string.Empty;
    string? IErpIntegrationEvent.TenantKey => TenantId == Guid.Empty ? null : TenantId.ToString();
    DateTime IDomainEvent.OccurredOn => ReleasedAt.UtcDateTime;
}

[CrestEvent(Id = ErpIntegrationWire.MesWorkOrderCompletedId, Name = ErpIntegrationWire.MesWorkOrderCompleted,
    Version = ErpIntegrationWire.Version, Scope = EventScope.Integration, RequiresIdempotency = true)]
public sealed record MesWorkOrderCompleted(
    Guid WorkOrderId, decimal Quantity, DateTimeOffset CompletedAt, Guid TenantId) : IErpIntegrationEvent
{
    public string MessageId { get; init; } = string.Empty;
    string? IErpIntegrationEvent.TenantKey => TenantId == Guid.Empty ? null : TenantId.ToString();
    DateTime IDomainEvent.OccurredOn => CompletedAt.UtcDateTime;
}

[CrestEvent(Id = ErpIntegrationWire.CrmCustomerChangedId, Name = ErpIntegrationWire.CrmCustomerChanged,
    Version = ErpIntegrationWire.Version, Scope = EventScope.Integration, RequiresIdempotency = true)]
public sealed record CrmCustomerChanged(Guid CustomerId, string Code, string Name, Guid TenantId) : IErpIntegrationEvent
{
    public string MessageId { get; init; } = string.Empty;
    public DateTimeOffset ChangedAt { get; init; }
    string? IErpIntegrationEvent.TenantKey => TenantId == Guid.Empty ? null : TenantId.ToString();
    DateTime IDomainEvent.OccurredOn => ChangedAt.UtcDateTime;
}
