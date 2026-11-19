using CrestCreates.Domain.Features;
using ErpManagement.Application.Contracts.Events;
using ErpManagement.Application.Contracts.Integration;

namespace ErpManagement.Application.Events;

/// <summary>Appends outbound integration facts inside the caller's business transaction.</summary>
public sealed class ErpIntegrationOutbox(
    IFeatureChecker features, StockPostedEventPublisher publisher)
{
    public Task AppendAsync(MesWorkOrderReleased payload, CancellationToken cancellationToken = default)
        => AppendIfEnabledAsync("Erp.MesIntegration", ErpIntegrationWire.MesWorkOrderReleasedId,
            ErpIntegrationWire.MesWorkOrderReleased,
            payload with { MessageId = $"{ErpIntegrationWire.MesWorkOrderReleased}:{payload.WorkOrderId:D}" },
            payload.WorkOrderId, cancellationToken);

    public Task AppendAsync(MesWorkOrderCompleted payload, CancellationToken cancellationToken = default)
        => AppendIfEnabledAsync("Erp.MesIntegration", ErpIntegrationWire.MesWorkOrderCompletedId,
            ErpIntegrationWire.MesWorkOrderCompleted,
            payload with { MessageId = $"{ErpIntegrationWire.MesWorkOrderCompleted}:{payload.WorkOrderId:D}" },
            payload.WorkOrderId, cancellationToken);

    public Task AppendAsync(CrmCustomerChanged payload, Guid changeId, DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        if (changeId == Guid.Empty)
            throw new ArgumentException("CRM customer changes require a change identifier.", nameof(changeId));

        return AppendIfEnabledAsync("Erp.CrmIntegration", ErpIntegrationWire.CrmCustomerChangedId,
            ErpIntegrationWire.CrmCustomerChanged,
            payload with
            {
                MessageId = $"{ErpIntegrationWire.CrmCustomerChanged}:{payload.CustomerId:D}:{changeId:D}",
                ChangedAt = changedAt
            },
            payload.CustomerId, cancellationToken);
    }

    private async Task AppendIfEnabledAsync<TEvent>(string feature, string contractId, string eventName,
        TEvent payload, Guid aggregateId, CancellationToken cancellationToken) where TEvent : IErpIntegrationEvent
    {
        if (await features.IsEnabledAsync(feature, cancellationToken).ConfigureAwait(false))
            await publisher.AppendAsync(payload, contractId, eventName, aggregateId, cancellationToken).ConfigureAwait(false);
    }
}
