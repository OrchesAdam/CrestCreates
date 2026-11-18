using CrestCreates.Domain.Repositories;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Runtime.Delivery.Abstractions.Activation;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using ErpManagement.Application.Contracts.Events;
using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Shared.Constants;
using ErpManagement.Domain.Shared.Enums;

namespace ErpManagement.Application.Approval;

/// <summary>
/// Sole write-back path for requisition approval decisions. The HumanTask completion is
/// delivered through the transactional outbox; this consumer restores the admitted decision
/// fact and applies it idempotently (same fact = Duplicate, different fact = Conflict).
/// </summary>
[GenerateOutboxConsumerActivation]
public sealed partial class ErpRequisitionDecisionConsumer : IOutboxRequiredConsumer<HumanTaskCompletedEvent>
{
    private readonly ICurrentTenant _currentTenant;
    private readonly IRuntimeStateContractRegistry _stateRegistry;
    private readonly ICrestRepositoryBase<PurchaseRequisition, Guid> _requisitions;

    public ErpRequisitionDecisionConsumer(
        ICurrentTenant currentTenant,
        IRuntimeStateContractRegistry stateRegistry,
        ICrestRepositoryBase<PurchaseRequisition, Guid> requisitions)
    {
        _currentTenant = currentTenant;
        _stateRegistry = stateRegistry;
        _requisitions = requisitions;
    }

    public const string ConsumerIdValue = ErpContractIds.RequisitionDecisionConsumerId;
    public string ConsumerId => ConsumerIdValue;

    public async ValueTask<OutboxRequiredConsumerResult> ConsumeAsync(
        HumanTaskCompletedEvent payload,
        OutboxDeliveryContext context,
        CancellationToken cancellationToken = default)
    {
        // Outbox delivery runs in a bare DI scope: establish the persisted tenant before EF access.
        using var tenantScope = await _currentTenant.ChangeAsync(payload.HumanTaskKey.TenantId ?? string.Empty);

        ErpRequisitionDecisionFact fact;
        try
        {
            fact = payload.Result is null
                ? throw new InvalidOperationException("missing result")
                : _stateRegistry.Restore<ErpRequisitionDecisionFact>(payload.Result);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return OutboxRequiredConsumerResult.Conflict(
                "ERP_DECISION_PAYLOAD_INVALID",
                "HumanTask completion does not contain the admitted requisition decision fact.");
        }

        if (!Guid.TryParse(fact.RequisitionId, out var requisitionId))
        {
            return OutboxRequiredConsumerResult.Conflict(
                "ERP_DECISION_PAYLOAD_INVALID",
                "The decision fact carries no valid requisition identity.");
        }

        var approval = string.Equals(payload.Outcome, "Approve", StringComparison.Ordinal);
        var rejection = string.Equals(payload.Outcome, "Reject", StringComparison.Ordinal);
        if (!approval && !rejection)
        {
            return OutboxRequiredConsumerResult.Conflict(
                "ERP_DECISION_INVALID",
                "Unsupported requisition approval outcome.");
        }

        var requisition = await _requisitions.GetAsync(requisitionId, cancellationToken).ConfigureAwait(false);
        if (requisition is null)
        {
            return OutboxRequiredConsumerResult.Conflict(
                "ERP_REQUISITION_NOT_FOUND",
                $"The requisition '{requisitionId}' is unavailable.");
        }

        if (requisition.Status is RequisitionStatus.Approved or RequisitionStatus.Rejected)
        {
            var sameDecision =
                (approval && requisition.Status == RequisitionStatus.Approved
                 || rejection && requisition.Status == RequisitionStatus.Rejected)
                && string.Equals(requisition.ApproverId, fact.ApproverId, StringComparison.Ordinal)
                && string.Equals(requisition.ApprovalComment, fact.Comment, StringComparison.Ordinal);
            return sameDecision
                ? OutboxRequiredConsumerResult.Duplicate()
                : OutboxRequiredConsumerResult.Conflict(
                    "ERP_DECISION_CONFLICT",
                    "The requisition already carries a different durable decision identity.");
        }

        if (requisition.Status != RequisitionStatus.PendingApproval)
        {
            return OutboxRequiredConsumerResult.Conflict(
                "ERP_DECISION_STATE_INVALID",
                $"The requisition is in state '{requisition.Status}' and has no pending approval decision.");
        }

        if (approval)
        {
            requisition.Approve(fact.ApproverId, fact.Comment);
        }
        else
        {
            requisition.Reject(fact.ApproverId, fact.Comment);
        }

        await _requisitions.UpdateAsync(requisition, cancellationToken).ConfigureAwait(false);
        return OutboxRequiredConsumerResult.Accepted();
    }
}