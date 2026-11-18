using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Repositories;
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

/// <summary>
/// HTTP projection over the formal HumanTask runtime. Decisions are completed through
/// IHumanTaskRuntime and delivered by the transactional outbox; the requisition write-back
/// happens only in ErpRequisitionDecisionConsumer, never here.
/// </summary>
[CrestService]
[CapabilityCompatibilityProjection(
    ContractType = typeof(IApprovalCenterAppService),
    RegistrationMode = HttpProjectionRegistrationMode.ExplicitHost,
    RoutePrefix = "api/approval-center",
    CapabilityIdPrefix = "compat.appservice.approval-center")]
public class ApprovalCenterAppService : IApprovalCenterAppService
{
    private readonly IHumanTaskInstanceStore _taskStore;
    private readonly IHumanTaskRuntime _humanTaskRuntime;
    private readonly IRuntimeStateContractRegistry _stateRegistry;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionChecker _permissionChecker;
    private readonly ICrestRepositoryBase<PurchaseRequisition, Guid> _requisitionRepository;

    public ApprovalCenterAppService(
        IHumanTaskInstanceStore taskStore,
        IHumanTaskRuntime humanTaskRuntime,
        IRuntimeStateContractRegistry stateRegistry,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        IPermissionChecker permissionChecker,
        ICrestRepositoryBase<PurchaseRequisition, Guid> requisitionRepository)
    {
        _taskStore = taskStore;
        _humanTaskRuntime = humanTaskRuntime;
        _stateRegistry = stateRegistry;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
        _permissionChecker = permissionChecker;
        _requisitionRepository = requisitionRepository;
    }

    public async Task<IReadOnlyList<ApprovalTaskDto>> GetTasksAsync(CancellationToken cancellationToken = default)
    {
        await _permissionChecker.CheckAsync(ErpPermissions.HumanTasks.Search);
        var scope = new RuntimeTenantScope(_currentTenant.Id);
        var byRole = await _taskStore.GetPendingByCandidateRoleAsync(
            scope, ErpContractIds.ProcurementManagerRole, cancellationToken);
        var byAssignee = await _taskStore.GetPendingByAssigneeAsync(scope, _currentUser.Id, cancellationToken);

        var tasks = byRole.Concat(byAssignee)
            .GroupBy(task => task.Key.InstanceId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(task => task.CreatedAt)
            .ToList();

        var results = new List<ApprovalTaskDto>(tasks.Count);
        foreach (var task in tasks)
        {
            var dto = await BuildDtoAsync(task, cancellationToken);
            if (dto is not null)
            {
                results.Add(dto);
            }
        }

        return results;
    }

    public async Task<ApprovalTaskDto?> GetTaskAsync(string id, CancellationToken cancellationToken = default)
    {
        await _permissionChecker.CheckAsync(ErpPermissions.HumanTasks.Get);
        var task = await _taskStore.GetAsync(new RuntimeInstanceKey(_currentTenant.Id, id), cancellationToken);
        return task is null ? null : await BuildDtoAsync(task, cancellationToken);
    }

    public Task<ApprovalTaskDto> ApproveAsync(string id, ApprovalDecisionInput input, CancellationToken cancellationToken = default)
        => CompleteDecisionAsync(id, "Approve", input.Comment, cancellationToken);

    public Task<ApprovalTaskDto> RejectAsync(string id, ApprovalDecisionInput input, CancellationToken cancellationToken = default)
        => CompleteDecisionAsync(id, "Reject", input.Comment, cancellationToken);

    private async Task<ApprovalTaskDto> CompleteDecisionAsync(
        string id, string outcome, string comment, CancellationToken cancellationToken)
    {
        await _permissionChecker.CheckAsync(ErpPermissions.HumanTasks.Complete);
        var task = await _taskStore.GetAsync(new RuntimeInstanceKey(_currentTenant.Id, id), cancellationToken)
                   ?? throw new KeyNotFoundException($"审批任务不存在: {id}");
        if (task.Status is not (HumanTaskInstanceStatus.Created or HumanTaskInstanceStatus.Assigned))
        {
            throw new InvalidOperationException("该审批任务不是待办状态。");
        }

        var requisitionId = RestoreRequisitionId(task.Input)
                            ?? throw new InvalidOperationException("审批任务缺少请购单身份。");
        var requisition = await _requisitionRepository.GetAsync(requisitionId, cancellationToken)
                          ?? throw new KeyNotFoundException($"请购单不存在: {requisitionId}");

        if (!Array.Exists(_currentUser.Roles,
                role => string.Equals(role, ErpContractIds.ProcurementManagerRole, StringComparison.OrdinalIgnoreCase)))
        {
            throw new UnauthorizedAccessException("需要 procurement-manager 角色才能审批。");
        }

        if (string.Equals(requisition.RequesterId, _currentUser.Id, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("请购人不能审批自己的请购单。");
        }

        await _humanTaskRuntime.CompleteAsync(new HumanTaskCompletionRequest
        {
            HumanTaskKey = task.Key,
            Outcome = outcome,
            ActorId = _currentUser.Id,
            ActorRoles = _currentUser.Roles,
            Result = _stateRegistry.Capture(new ErpRequisitionDecisionFact
            {
                RequisitionId = requisitionId.ToString(),
                ApproverId = _currentUser.Id,
                Comment = comment
            })
        }, cancellationToken);

        return new ApprovalTaskDto
        {
            HumanTaskId = task.Id,
            RequisitionId = requisition.Id,
            RequisitionNumber = requisition.Number,
            Amount = requisition.TotalAmount,
            RequesterId = requisition.RequesterId,
            CreatedAt = task.CreatedAt
        };
    }

    private async Task<ApprovalTaskDto?> BuildDtoAsync(HumanTaskInstance task, CancellationToken cancellationToken)
    {
        var requisitionId = RestoreRequisitionId(task.Input);
        if (requisitionId is null)
        {
            return null;
        }

        var requisition = await _requisitionRepository.GetAsync(requisitionId.Value, cancellationToken);
        if (requisition is null)
        {
            return null;
        }

        return new ApprovalTaskDto
        {
            HumanTaskId = task.Id,
            RequisitionId = requisition.Id,
            RequisitionNumber = requisition.Number,
            Amount = requisition.TotalAmount,
            RequesterId = requisition.RequesterId,
            CreatedAt = task.CreatedAt
        };
    }

    /// <summary>
    /// The task Input carries the workflow variable bag ({requisitionId: Guid}), not the
    /// decision fact; restore the bag first, then the captured Guid value.
    /// </summary>
    private Guid? RestoreRequisitionId(RuntimeStateValue? input)
    {
        if (input is null || _stateRegistry.Restore(input) is not RuntimeStateBag bag)
        {
            return null;
        }

        if (!bag.Values.TryGetValue("requisitionId", out var value))
        {
            return null;
        }

        var restored = _stateRegistry.Restore(value);
        return restored switch
        {
            Guid guid => guid,
            string text when Guid.TryParse(text, out var parsed) => parsed,
            _ => null
        };
    }
}