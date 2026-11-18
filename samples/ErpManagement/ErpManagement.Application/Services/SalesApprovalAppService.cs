using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Shared.Exceptions;
using ErpManagement.Domain.Shared.Enums;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using ErpManagement.Application.Contracts.DTOs;
using ErpManagement.Application.Contracts.Events;
using ErpManagement.Application.Contracts.Interfaces;
using ErpManagement.Domain.Entities;
using ErpManagement.Domain.Shared.Constants;
using ErpManagement.Domain.Shared.Permissions;

namespace ErpManagement.Application.Services;

[CrestService]
[CapabilityCompatibilityProjection(
    ContractType = typeof(ISalesApprovalAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/sales-approval",
    CapabilityIdPrefix = "compat.appservice.sales-approval")]
public class SalesApprovalAppService(
    IHumanTaskInstanceStore tasks,
    IHumanTaskRuntime runtime,
    IRuntimeStateContractRegistry state,
    ICurrentTenant tenant,
    ICurrentUser user,
    IPermissionChecker permissions,
    IFeatureChecker features,
    ISalesOrderRepository orders) : ISalesApprovalAppService
{
    public async Task<IReadOnlyList<SalesApprovalTaskDto>> GetTasksAsync(CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(ErpPermissions.SalesApproval.Search, cancellationToken);
        var pending = await tasks.GetPendingByCandidateRoleAsync(new RuntimeTenantScope(tenant.Id),
            ErpContractIds.SalesManagerRole, cancellationToken);
        var result = new List<SalesApprovalTaskDto>();
        foreach (var task in pending.OrderBy(task => task.CreatedAt))
        {
            var order = await FindOrderAsync(task, cancellationToken);
            if (order is not null && order.RequesterId != user.Id) result.Add(Map(task, order));
        }
        return result;
    }

    public Task<SalesApprovalTaskDto> ApproveAsync(string id, ApprovalDecisionInput input, CancellationToken cancellationToken = default)
        => CompleteAsync(id, "Approve", input.Comment, cancellationToken);

    public Task<SalesApprovalTaskDto> RejectAsync(string id, ApprovalDecisionInput input, CancellationToken cancellationToken = default)
        => CompleteAsync(id, "Reject", input.Comment, cancellationToken);

    private async Task<SalesApprovalTaskDto> CompleteAsync(string id, string outcome, string? comment, CancellationToken ct)
    {
        await AuthorizeAsync(ErpPermissions.SalesApproval.Complete, ct);
        comment = comment?.Trim() ?? string.Empty;
        if (comment.Length is < 1 or > 1000) throw new CrestValidationException("审批意见必须为 1–1000 字符。");
        var task = await tasks.GetAsync(new RuntimeInstanceKey(tenant.Id, id), ct)
            ?? throw new KeyNotFoundException($"销售审批任务 {id} 不存在。");
        var order = await FindOrderAsync(task, ct)
            ?? throw new KeyNotFoundException($"销售审批任务 {id} 无关联待审批销售订单。");
        if (order.RequesterId == user.Id)
            throw new CrestPermissionException(ErpPermissions.SalesApproval.Complete, "提交人不能审批自己的销售订单。");

        await runtime.CompleteAsync(new HumanTaskCompletionRequest
        {
            HumanTaskKey = task.Key,
            Outcome = outcome,
            ActorId = user.Id,
            ActorRoles = user.Roles,
            Result = state.Capture(new ErpSalesDecisionFact
            {
                SalesOrderId = order.Id.ToString(), ApproverId = user.Id, Comment = comment
            })
        }, ct);
        return Map(task, order);
    }

    private async Task AuthorizeAsync(string permission, CancellationToken ct)
    {
        await permissions.CheckAsync(permission);
        await features.RequireAsync("Erp.Sales", ct);
        if (string.IsNullOrWhiteSpace(user.Id) || !user.Roles.Contains(ErpContractIds.SalesManagerRole, StringComparer.OrdinalIgnoreCase))
            throw new CrestPermissionException(permission, "需要 sales-manager 角色。");
    }

    private async Task<SalesOrder?> FindOrderAsync(HumanTaskInstance task, CancellationToken ct)
    {
        if (task.HumanTaskId != ErpContractIds.SalesApprovalHumanTask || task.HumanTaskVersion != 1
            || !task.CandidateRoleIds.Contains(ErpContractIds.SalesManagerRole, StringComparer.Ordinal)
            || task.Status is not (HumanTaskInstanceStatus.Created or HumanTaskInstanceStatus.Assigned)
            || task.Input is null || state.Restore(task.Input) is not RuntimeStateBag bag
            || !bag.Values.TryGetValue("salesOrderId", out var id) || state.Restore(id) is not Guid orderId)
            return null;
        var tenantId = tenant.GetErpTenantId();
        return await orders.GetAsync(order => order.Id == orderId && order.TenantId == tenantId
            && order.Status == SalesOrderStatus.PendingApproval && order.HumanTaskInstanceId == task.Id
            && order.WorkflowInstanceId == task.WorkflowInstanceId, ct);
    }

    private static SalesApprovalTaskDto Map(HumanTaskInstance task, SalesOrder order) => new()
    {
        HumanTaskId = task.Id,
        SalesOrderId = order.Id,
        SalesOrderNumber = order.Number,
        Amount = order.TotalAmount,
        RequesterId = order.RequesterId,
        CreatedAt = task.CreatedAt
    };
}
