using CrestCreates.Domain.Repositories;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Runtime.Delivery.Abstractions.Activation;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using ErpManagement.Application.Contracts.Events;
using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Shared.Constants;
using Microsoft.EntityFrameworkCore;

namespace ErpManagement.Application.Events;

/// <summary>
/// Reliable-consumption proof for StockPosted: records one inbox row per (consumer, message).
/// The unique index is the dedup authority; a racing insert loser observes Duplicate.
/// P2 finance consumers attach to the same contract with their own consumer id.
/// </summary>
[GenerateOutboxConsumerActivation]
public sealed partial class ErpStockPostedInboxConsumer : IOutboxRequiredConsumer<StockPostedEvent>
{
    private readonly ICurrentTenant _currentTenant;
    private readonly ICrestRepositoryBase<IntegrationEventInbox, Guid> _inbox;

    public ErpStockPostedInboxConsumer(
        ICurrentTenant currentTenant,
        ICrestRepositoryBase<IntegrationEventInbox, Guid> inbox)
    {
        _currentTenant = currentTenant;
        _inbox = inbox;
    }

    public const string ConsumerIdValue = ErpContractIds.StockPostedConsumerId;
    public string ConsumerId => ConsumerIdValue;

    public async ValueTask<OutboxRequiredConsumerResult> ConsumeAsync(
        StockPostedEvent payload,
        OutboxDeliveryContext context,
        CancellationToken cancellationToken = default)
    {
        using var tenantScope = await _currentTenant.ChangeAsync(payload.TenantId ?? string.Empty);

        var messageId = context.Message.Metadata.MessageId;
        var alreadyRecorded = await _inbox.AnyAsync(
            row => row.ConsumerId == ConsumerIdValue && row.MessageId == messageId,
            cancellationToken).ConfigureAwait(false);
        if (alreadyRecorded)
        {
            return OutboxRequiredConsumerResult.Duplicate();
        }

        var tenantId = Guid.TryParse(payload.TenantId, out var parsed) ? parsed : Guid.Empty;
        try
        {
            await _inbox.InsertAsync(
                new IntegrationEventInbox(
                    Guid.NewGuid(),
                    ConsumerIdValue,
                    messageId,
                    context.Message.Metadata.ContractId,
                    DateTimeOffset.UtcNow,
                    tenantId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Unique (ConsumerId, MessageId) violation: a concurrent delivery already recorded it.
            return OutboxRequiredConsumerResult.Duplicate();
        }

        return OutboxRequiredConsumerResult.Accepted();
    }
}