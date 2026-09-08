using System.Security.Claims;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Sample.AssetManagement.Application;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Sample.AssetManagement.Contracts.Json;
using CrestCreates.Sample.AssetManagement.Domain;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Sample.AssetManagement.Host;

public sealed class AssetMaintenanceWorkflowService : IAssetMaintenanceWorkflowStarter
{
    private readonly IWorkflowEngine _workflows;
    private readonly IHumanTaskRuntime _humanTasks;
    private readonly IHumanTaskInstanceStore _taskStore;
    private readonly IWorkflowInstanceStore _workflowStore;
    private readonly IRuntimeDescriptorPinResolver<HumanTaskDescriptor> _taskPinResolver;
    private readonly IAssetStore _assets;
    private readonly IRuntimeStateContractRegistry _stateRegistry;
    private readonly ICurrentUser _currentUser;
    private readonly IWorkflowAbortService _workflowAbortService;

    public AssetMaintenanceWorkflowService(
        IWorkflowEngine workflows,
        IHumanTaskRuntime humanTasks,
        IHumanTaskInstanceStore taskStore,
        IWorkflowInstanceStore workflowStore,
        IRuntimeDescriptorPinResolver<HumanTaskDescriptor> taskPinResolver,
        IAssetStore assets,
        IRuntimeStateContractRegistry stateRegistry,
        ICurrentUser currentUser,
        IWorkflowAbortService workflowAbortService)
    {
        _workflows = workflows;
        _humanTasks = humanTasks;
        _taskStore = taskStore;
        _workflowStore = workflowStore;
        _taskPinResolver = taskPinResolver;
        _assets = assets;
        _stateRegistry = stateRegistry;
        _currentUser = currentUser;
        _workflowAbortService = workflowAbortService;
    }

    public async Task<AssetMaintenanceWorkflowLease> StartAsync(Guid assetId, string tenantId, string requesterId, CancellationToken ct = default)
    {
        var workflow = await _workflows.ExecuteAsync(new WorkflowExecutionRequest
        {
            WorkflowId = AssetContractIds.MaintenanceWorkflow,
            TenantId = tenantId,
            InputVariables = new Dictionary<string, RuntimeStateValue>
            {
                ["assetId"] = _stateRegistry.Capture(assetId),
                ["requesterId"] = _stateRegistry.Capture(requesterId)
            }
        }, ct);
        var tasks = await _taskStore.GetPendingByWorkflowAsync(workflow.Key, ct);
        if (workflow.Status != WorkflowInstanceStatus.Suspended || tasks.Count != 1 || string.IsNullOrWhiteSpace(workflow.WaitingHumanTaskId))
            throw new InvalidOperationException("Maintenance workflow must suspend on exactly one HumanTask.");
        return new AssetMaintenanceWorkflowLease(workflow.InstanceId, tasks[0].Id);
    }

    public async Task AbortAsync(AssetMaintenanceWorkflowLease lease, string reason, CancellationToken ct = default)
    {
        await _workflowAbortService.AbortAsync(
            new RuntimeInstanceKey(_currentUser.TenantId, lease.WorkflowInstanceId),
            new RuntimeInstanceKey(_currentUser.TenantId, lease.HumanTaskId),
            reason,
            $"asset-maintenance-abort:{lease.WorkflowInstanceId}",
            ct);
    }

    public async Task CompleteAsync(string humanTaskId, string outcome, string note, CancellationToken ct = default)
    {
        if (!_currentUser.IsInRole("asset-manager"))
            throw new UnauthorizedAccessException("The asset-manager role is required to complete maintenance review.");
        var task = await _taskStore.GetAsync(new RuntimeInstanceKey(_currentUser.TenantId, humanTaskId), ct)
            ?? throw new InvalidOperationException("Maintenance HumanTask is unavailable.");
        var values = RestoreVariables(task.Input);
        var assetId = values["assetId"] is Guid id ? id : Guid.Parse(values["assetId"]!.ToString()!);
        if (!string.Equals(task.TenantId, _currentUser.TenantId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The HumanTask belongs to another tenant.");
        var taskRole = ResolveRole(task);
        var approved = string.Equals(outcome, "Approve", StringComparison.OrdinalIgnoreCase);
        var rejected = string.Equals(outcome, "Reject", StringComparison.OrdinalIgnoreCase);
        if (!approved && !rejected)
            throw new ArgumentException("Outcome must be Approve or Reject.", nameof(outcome));
        var variables = RestoreVariables(task.Input);
        var requesterId = variables.TryGetValue("requesterId", out var requester)
            ? requester?.ToString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(requesterId))
            throw new InvalidOperationException("Maintenance requester is missing from the durable workflow state.");

        await _humanTasks.CompleteAsync(new HumanTaskCompletionRequest
        {
            HumanTaskKey = task.Key,
            Outcome = approved ? "Approve" : "Reject",
            ActorId = _currentUser.Id,
            ActorRoles = _currentUser.Roles,
            Result = _stateRegistry.Capture(new AssetMaintenanceDecisionFact { AssetId = assetId, RequesterId = requesterId, ApproverId = _currentUser.Id, Approved = approved, Note = note })
        }, ct);

        if (taskRole == MaintenanceTaskRole.Initial && approved)
        {
            await WaitForInitialContinuationAsync(task, ct);
            return;
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var asset = await _assets.GetAsync(_currentUser.TenantId, assetId, ct);
            if (asset?.Status is AssetStatus.Available or AssetStatus.Assigned)
                return;
            await Task.Delay(20, ct);
        }
        throw new CapabilityFailureException("ASSET_MAINTENANCE_COMPLETION_TIMEOUT", "The durable maintenance decision was not observed before the completion deadline.");
    }

    private async Task WaitForInitialContinuationAsync(HumanTaskInstance initialTask, CancellationToken ct)
    {
        var workflowKey = initialTask.WorkflowKey
            ?? throw new InvalidOperationException("Initial maintenance review is missing its Workflow correlation.");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var completed = await _taskStore.GetAsync(initialTask.Key, ct);
            var finalTasks = await _taskStore.GetPendingByWorkflowAsync(workflowKey, ct);
            var workflow = await _workflowStore.GetAsync(workflowKey, ct);
            if (completed?.Status == HumanTaskInstanceStatus.Completed
                && workflow?.Status == WorkflowInstanceStatus.Suspended
                && finalTasks.Count == 1
                && IsKnownRole(finalTasks[0], MaintenanceTaskRole.Terminal))
                return;
            await Task.Delay(20, ct);
        }
        throw new CapabilityFailureException("ASSET_MAINTENANCE_CONTINUATION_TIMEOUT", "The durable initial review continuation was not observed before the completion deadline.");
    }

    private MaintenanceTaskRole ResolveRole(HumanTaskInstance task)
    {
        var descriptor = _taskPinResolver.Resolve(task.HumanTaskPin).Descriptor;
        if (descriptor.Id == AssetContractIds.MaintenanceInitialHumanTask && descriptor.Version == 1)
            return MaintenanceTaskRole.Initial;
        if (descriptor.Id == AssetContractIds.MaintenanceHumanTask && descriptor.Version == 1)
            return MaintenanceTaskRole.Terminal;
        throw new InvalidOperationException("The maintenance HumanTask contract is not an approved Asset maintenance role.");
    }

    private bool IsKnownRole(HumanTaskInstance task, MaintenanceTaskRole expected)
    {
        try { return ResolveRole(task) == expected; }
        catch (InvalidOperationException) { return false; }
    }

    private enum MaintenanceTaskRole
    {
        Initial,
        Terminal
    }

    private Dictionary<string, object?> RestoreVariables(RuntimeStateValue? input)
        => input is not null && _stateRegistry.Restore(input) is RuntimeStateBag bag
            ? bag.Values.ToDictionary(pair => pair.Key, pair => _stateRegistry.Restore(pair.Value))
            : throw new InvalidOperationException("Maintenance workflow variables are unavailable.");
}

public sealed class AssetMaintenanceDecisionConsumer : CrestCreates.Runtime.Delivery.Abstractions.Handlers.IOutboxRequiredConsumer<HumanTaskCompletedEvent>
{
    private readonly IHumanTaskInstanceStore _tasks;
    private readonly IRuntimeStateContractRegistry _stateRegistry;
    private readonly IRuntimeDescriptorPinResolver<HumanTaskDescriptor> _taskPinResolver;
    private readonly ICapabilityDispatcher _dispatcher;
    private readonly AssetExecutionIdentity _identity;
    private readonly ICurrentPrincipalAccessor _principalAccessor;
    private readonly ILogger<AssetMaintenanceDecisionConsumer> _logger;

    public AssetMaintenanceDecisionConsumer(
        IHumanTaskInstanceStore tasks,
        IRuntimeStateContractRegistry stateRegistry,
        IRuntimeDescriptorPinResolver<HumanTaskDescriptor> taskPinResolver,
        ICapabilityDispatcher dispatcher,
        AssetExecutionIdentity identity,
        ICurrentPrincipalAccessor principalAccessor,
        ILogger<AssetMaintenanceDecisionConsumer> logger)
    {
        _tasks = tasks;
        _stateRegistry = stateRegistry;
        _taskPinResolver = taskPinResolver;
        _dispatcher = dispatcher;
        _identity = identity;
        _principalAccessor = principalAccessor;
        _logger = logger;
    }

    public string ConsumerId => AssetContractIds.MaintenanceDecisionConsumer;

    public async ValueTask<CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult> ConsumeAsync(HumanTaskCompletedEvent payload, CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxDeliveryContext context, CancellationToken ct = default)
    {
        try
        {
            return await ConsumeCoreAsync(payload, context, ct);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Asset maintenance completion failed for {MessageId}.", context.Message.Metadata.MessageId);
            return CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Retry("ASSET_MAINTENANCE_CONSUMER_FAILED", exception.Message);
        }
    }

    private async ValueTask<CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult> ConsumeCoreAsync(HumanTaskCompletedEvent payload, CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxDeliveryContext context, CancellationToken ct = default)
    {
        _logger.LogInformation("Processing asset maintenance completion {MessageId} for HumanTask {HumanTaskKey}.", context.Message.Metadata.MessageId, payload.HumanTaskKey.InstanceId);
        var task = await _tasks.GetAsync(payload.HumanTaskKey, ct);
        if (task is null || string.IsNullOrWhiteSpace(task.TenantId) || payload.Result is null)
        {
            _logger.LogWarning("Maintenance completion precondition failed. TaskFound={TaskFound}, Tenant={Tenant}, ResultPresent={ResultPresent}.", task is not null, task?.TenantId, payload.Result is not null);
            return CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Conflict("ASSET_MAINTENANCE_FACT_INVALID", "Maintenance completion is missing its durable task or decision fact.");
        }
        if (_stateRegistry.Restore(payload.Result) is not AssetMaintenanceDecisionFact fact)
            return CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Conflict("ASSET_MAINTENANCE_FACT_INVALID", "Maintenance completion fact has an invalid contract.");
        var descriptor = _taskPinResolver.Resolve(task.HumanTaskPin).Descriptor;
        var isInitial = descriptor.Id == AssetContractIds.MaintenanceInitialHumanTask && descriptor.Version == 1;
        var isTerminal = descriptor.Id == AssetContractIds.MaintenanceHumanTask && descriptor.Version == 1;
        if (!isInitial && !isTerminal)
            return CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Conflict("ASSET_MAINTENANCE_TASK_INVALID", "Maintenance completion is not bound to a known Asset HumanTask contract.");
        if (isInitial && fact.Approved)
        {
            _logger.LogInformation("Acknowledging initial Asset maintenance approval for {AssetId} without applying a terminal decision.", fact.AssetId);
            return CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Accepted();
        }
        _identity.Set(task.TenantId, fact.ApproverId, dataScope: CrestCreates.Domain.Shared.Enums.DataScope.Tenant, roles: ["asset-manager"]);
        var command = new CrestCreates.Sample.AssetManagement.Application.Handlers.MaintenanceDecisionCommand(
            fact.AssetId,
            new CrestCreates.Sample.AssetManagement.Contracts.Dtos.MaintenanceDecisionInput { AssetId = fact.AssetId, Approved = fact.Approved, Note = fact.Note },
            task.WorkflowInstanceId ?? string.Empty,
            fact.RequesterId);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, fact.ApproverId), new Claim(ClaimTypes.Role, "asset-manager") }, "outbox"));
        using var principalScope = _principalAccessor.Change(principal);
        _logger.LogInformation("Dispatching asset maintenance decision for {AssetId} as {ApproverId}.", fact.AssetId, fact.ApproverId);
        var result = await _dispatcher.DispatchAsync(AssetContractIds.ApplyMaintenanceCapability, InvocationSource.HumanTask, command, ctx =>
        {
            ctx.InputJson = System.Text.Json.JsonSerializer.SerializeToElement(
                command.Decision,
                AssetJsonContext.Default.MaintenanceDecisionInput);
            ctx.TenantId = task.TenantId;
            ctx.UserId = fact.ApproverId;
            ctx.Principal = principal;
        }, ct);
        _logger.LogInformation("Asset maintenance decision dispatch completed for {AssetId}: {Status} {ErrorCode}.", fact.AssetId, result.Status, result.ErrorCode);
        if (!result.IsSuccess)
            _logger.LogWarning("Maintenance decision capability failed with {ErrorCode}: {ErrorMessage}.", result.ErrorCode, result.ErrorMessage);
        return result.IsSuccess
            ? CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Accepted()
            : CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Conflict(result.ErrorCode ?? "ASSET_MAINTENANCE_FAILED", "Maintenance decision could not be applied.");
    }
}

public sealed class AssetLocalEventBus : CrestCreates.EventBus.Abstractions.ILocalEventBus
{
    public Task PublishAsync(CrestCreates.EventBus.Abstractions.ILocalEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : CrestCreates.EventBus.Abstractions.ILocalEvent => Task.CompletedTask;
}
