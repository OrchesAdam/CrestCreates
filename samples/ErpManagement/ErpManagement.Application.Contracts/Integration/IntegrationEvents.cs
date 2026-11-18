using CrestCreates.EventBus.Abstractions;

namespace ErpManagement.Application.Contracts.Integration;

public sealed record MesWorkOrderReleased(
    Guid WorkOrderId, Guid MaterialId, decimal Quantity, Guid TenantId, DateTimeOffset ReleasedAt) : ILocalEvent;

public sealed record MesWorkOrderCompleted(
    Guid WorkOrderId, decimal Quantity, DateTimeOffset CompletedAt, Guid TenantId) : ILocalEvent;

public sealed record CrmCustomerChanged(Guid CustomerId, string Code, string Name, Guid TenantId) : ILocalEvent;
