using CrestCreates.EventBus.Abstractions;
using ErpManagement.Application.Contracts.Integration;
using Microsoft.Extensions.Logging;

namespace ErpManagement.Application.Integration;

public sealed class IntegrationOutboundLogHandler(ILogger<IntegrationOutboundLogHandler> logger)
    : ILocalEventHandler<MesWorkOrderReleased>, ILocalEventHandler<MesWorkOrderCompleted>, ILocalEventHandler<CrmCustomerChanged>
{
    public Task HandleAsync(MesWorkOrderReleased message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("integration outbound (MES): work order {WorkOrderId} released for tenant {TenantId}, quantity {Quantity}",
            message.WorkOrderId, message.TenantId, message.Quantity);
        return Task.CompletedTask;
    }

    public Task HandleAsync(MesWorkOrderCompleted message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("integration outbound (MES): work order {WorkOrderId} completed for tenant {TenantId}, quantity {Quantity}",
            message.WorkOrderId, message.TenantId, message.Quantity);
        return Task.CompletedTask;
    }

    public Task HandleAsync(CrmCustomerChanged message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("integration outbound (CRM): customer {CustomerId} ({Code}) changed for tenant {TenantId}",
            message.CustomerId, message.Code, message.TenantId);
        return Task.CompletedTask;
    }
}
