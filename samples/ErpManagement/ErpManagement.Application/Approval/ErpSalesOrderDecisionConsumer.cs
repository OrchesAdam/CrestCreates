using CrestCreates.HumanTask.Abstractions;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Runtime.Delivery.Abstractions.Activation;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using ErpManagement.Application.Contracts.Events;
using ErpManagement.Application.Contracts.Interfaces;
using ErpManagement.Application.Services;
using ErpManagement.Domain.Shared.Constants;
using ErpManagement.Domain.Shared.Enums;

namespace ErpManagement.Application.Approval;

[GenerateOutboxConsumerActivation]
public sealed partial class ErpSalesOrderDecisionConsumer(
    ICurrentTenant tenant,
    IRuntimeStateContractRegistry state,
    ISalesOrderRepository orders) : IOutboxRequiredConsumer<HumanTaskCompletedEvent>
{
    public const string ConsumerIdValue = ErpContractIds.SalesDecisionConsumerId;
    public string ConsumerId => ConsumerIdValue;

    public async ValueTask<OutboxRequiredConsumerResult> ConsumeAsync(
        HumanTaskCompletedEvent payload, OutboxDeliveryContext context, CancellationToken cancellationToken = default)
    {
        if (payload.HumanTaskPin?.Ref.Id != ErpContractIds.SalesApprovalHumanTask || payload.HumanTaskPin.Ref.Version != 1
            || payload.Outcome is not ("Approve" or "Reject"))
            return OutboxRequiredConsumerResult.Conflict("ERP_SALES_DECISION_INVALID", "Unsupported sales approval task or outcome.");

        ErpSalesDecisionFact fact;
        try
        {
            fact = payload.Result is null ? throw new InvalidOperationException("Missing result.")
                : state.Restore<ErpSalesDecisionFact>(payload.Result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return OutboxRequiredConsumerResult.Conflict("ERP_SALES_DECISION_INVALID", "Missing admitted sales decision fact.");
        }

        if (!Guid.TryParse(fact.SalesOrderId, out var id) || string.IsNullOrWhiteSpace(fact.ApproverId)
            || payload.ActorId != fact.ApproverId)
            return OutboxRequiredConsumerResult.Conflict("ERP_SALES_DECISION_INVALID", "Invalid sales order or approver identity.");

        var taskTenantId = payload.HumanTaskKey.TenantId;
        using var tenantScope = await tenant.ChangeAsync(taskTenantId ?? string.Empty);
        if (taskTenantId is not null && !string.Equals(tenant.Id, taskTenantId, StringComparison.Ordinal))
            return OutboxRequiredConsumerResult.Conflict("ERP_SALES_DECISION_TENANT", "Sales approval tenant is unavailable.");
        var tenantId = tenant.GetErpTenantId();
        var order = await orders.GetAsync(entity => entity.Id == id && entity.TenantId == tenantId, cancellationToken);
        if (order is null || order.HumanTaskInstanceId != payload.HumanTaskKey.InstanceId
            || order.WorkflowInstanceId != payload.WorkflowKey?.InstanceId || order.RequesterId == fact.ApproverId)
            return OutboxRequiredConsumerResult.Conflict("ERP_SALES_DECISION_CORRELATION", "Sales order and approval task do not match.");

        var approved = payload.Outcome == "Approve";
        if (order.ApprovalAccepted is bool previous)
            return previous == approved && order.ApproverId == fact.ApproverId && order.ApprovalComment == fact.Comment
                ? OutboxRequiredConsumerResult.Duplicate()
                : OutboxRequiredConsumerResult.Conflict("ERP_SALES_DECISION_CONFLICT", "A different sales decision is already recorded.");
        if (order.Status != SalesOrderStatus.PendingApproval)
            return OutboxRequiredConsumerResult.Conflict("ERP_SALES_DECISION_STATE", "Sales order is not pending approval.");

        if (approved) order.Approve(fact.ApproverId, fact.Comment);
        else order.Reject(fact.ApproverId, fact.Comment);
        await orders.UpdateAsync(order, cancellationToken);
        return OutboxRequiredConsumerResult.Accepted();
    }
}
