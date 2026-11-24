using CrestCreates.Runtime.Delivery.Abstractions.Activation;
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
using CrestCreates.Sample.AssetManagement.Contracts.Mes;
using CrestCreates.Sample.AssetManagement.Host.ProductionRelease;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Sample.AssetManagement.Domain;
using CrestCreates.Sample.AssetManagement.Host.ApprovedReleases;
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
    private readonly AssetMaintenanceTaskContractResolver _taskContracts;
    private readonly IWorkflowContinuationAcceptanceStore _continuationAcceptances;
    private readonly IAssetStore _assets;
    private readonly IRuntimeStateContractRegistry _stateRegistry;
    private readonly ICurrentUser _currentUser;
    private readonly IWorkflowAbortService _workflowAbortService;
    private readonly IAssetMaintenanceWorkflowVersionSelector _workflowVersions;
    private readonly AssetMesClient _mes;
    private readonly AssetProductionRunEvidence _evolutionEvidence;

    public AssetMaintenanceWorkflowService(
        IWorkflowEngine workflows,
        IHumanTaskRuntime humanTasks,
        IHumanTaskInstanceStore taskStore,
        IWorkflowInstanceStore workflowStore,
        AssetMaintenanceTaskContractResolver taskContracts,
        IWorkflowContinuationAcceptanceStore continuationAcceptances,
        IAssetStore assets,
        IRuntimeStateContractRegistry stateRegistry,
        ICurrentUser currentUser,
        IWorkflowAbortService workflowAbortService,
        IAssetMaintenanceWorkflowVersionSelector workflowVersions,
        AssetMesClient mes,
        AssetProductionRunEvidence evolutionEvidence)
    {
        _workflows = workflows;
        _humanTasks = humanTasks;
        _taskStore = taskStore;
        _workflowStore = workflowStore;
        _taskContracts = taskContracts;
        _continuationAcceptances = continuationAcceptances;
        _assets = assets;
        _stateRegistry = stateRegistry;
        _currentUser = currentUser;
        _workflowAbortService = workflowAbortService;
        _workflowVersions = workflowVersions;
        _mes = mes;
        _evolutionEvidence = evolutionEvidence;
    }

    public async Task<AssetMaintenanceWorkflowLease> StartAsync(Guid assetId, string tenantId, string requesterId, CancellationToken ct = default)
    {
        var version = await _workflowVersions.SelectVersionAsync(assetId, tenantId, ct);
        var input = new Dictionary<string, RuntimeStateValue>
        {
            ["assetId"] = _stateRegistry.Capture(assetId),
            ["requesterId"] = _stateRegistry.Capture(requesterId)
        };
        _evolutionEvidence.Bind(tenantId, version, input, _stateRegistry);
        var workflow = await _workflows.ExecuteAsync(new WorkflowExecutionRequest
        {
            WorkflowId = AssetContractIds.MaintenanceWorkflow,
            WorkflowVersion = version,
            TenantId = tenantId,
            InputVariables = input
        }, ct);
        var tasks = await _taskStore.GetPendingByWorkflowAsync(workflow.Key, ct);
        if (workflow.Status != WorkflowInstanceStatus.Suspended || tasks.Count != 1 || string.IsNullOrWhiteSpace(workflow.WaitingHumanTaskId))
            throw new InvalidOperationException("Maintenance workflow must suspend on exactly one HumanTask.");
        return new AssetMaintenanceWorkflowLease(workflow.InstanceId, tasks[0].Id);
    }

    public async Task ConfirmStartedAsync(Guid assetId, string tenantId, AssetMaintenanceWorkflowLease lease, CancellationToken ct = default)
    {
        var workflow = await _workflowStore.GetAsync(new RuntimeInstanceKey(tenantId, lease.WorkflowInstanceId), ct)
            ?? throw new InvalidOperationException("The started maintenance workflow is unavailable.");
        if (workflow.WorkflowPin.Ref.Version == AssetProductionDescriptors.WorkflowVersion)
            await EnsureMesLockAsync(assetId, tenantId, workflow, ct);
    }

    public async Task<AssetMaintenanceWorkflowLease?> TryRecoverStartAsync(Guid assetId, string tenantId, string requesterId,
        string workflowInstanceId, CancellationToken ct = default)
    {
        var workflow = await _workflowStore.GetAsync(new RuntimeInstanceKey(tenantId, workflowInstanceId), ct);
        if (workflow is null || workflow.WorkflowPin.Ref.Version != AssetProductionDescriptors.WorkflowVersion ||
            workflow.Status != WorkflowInstanceStatus.Suspended || workflow.WaitingHumanTaskId is not { } taskId ||
            !workflow.Variables.TryGetValue("requesterId", out var requester) || _stateRegistry.Restore(requester) is not string originalRequester ||
            originalRequester != requesterId)
        {
            return null;
        }

        await EnsureMesLockAsync(assetId, tenantId, workflow, ct);
        return new AssetMaintenanceWorkflowLease(workflowInstanceId, taskId);
    }

    private async Task EnsureMesLockAsync(Guid assetId, string tenantId, WorkflowInstance workflow, CancellationToken ct)
    {
        var workflowId = workflow.InstanceId;
        if (!IsCriticalWorkflowForAsset(workflow, assetId))
            throw new CapabilityFailureException("ASSET_PRODUCTION_WORKFLOW_INVALID", "The workflow is not the approved critical-equipment maintenance version for this asset.");

        var state = await _mes.RegisterAsync(tenantId, assetId, workflowId, ct);
        while (!workflow.Variables.ContainsKey("productionLock"))
        {
            ct.ThrowIfCancellationRequested();
            workflow.Variables["productionLock"] = _stateRegistry.Capture(new AssetProductionLockFact
            {
                AssetId = assetId, WorkflowInstanceId = workflowId, TenantId = tenantId, RegisteredAt = state.RegisteredAt
            });
            try
            {
                await _workflowStore.UpdateAsync(workflow, workflow.Revision, ct);
                return;
            }
            catch (RuntimeConcurrencyException)
            {
                workflow = await RequireCriticalWorkflowAsync(assetId, workflowId, tenantId, ct);
            }
        }

        var existing = ReadLock(workflow);
        if (existing.AssetId != assetId || existing.RegisteredAt != state.RegisteredAt)
            throw new CapabilityFailureException("ASSET_MES_LOCK_CONFLICT", "The MES lock does not match this maintenance workflow.");
    }

    internal async Task<bool> ApplyMesTrialEventAsync(TrialObservation observation, CancellationToken ct)
    {
        var key = new RuntimeInstanceKey(observation.TenantId, observation.WorkOrderId);
        var workflow = await _workflowStore.GetAsync(key, ct);
        if (workflow is null || !IsCriticalWorkflowForAsset(workflow, observation.EquipmentId) ||
            workflow.Status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Failed or WorkflowInstanceStatus.Compensated ||
            workflow.StepResults.Any(result => result.StepId == AssetProductionDescriptors.TrialStepId && result.HumanTaskOutcome is not null))
        {
            return true;
        }

        if (workflow.WaitingHumanTaskKey is not { } taskKey || !workflow.Variables.ContainsKey("productionLock"))
            return false;

        var task = await _taskStore.GetAsync(taskKey, ct);
        if (task is null || ResolveRole(task) != AssetMaintenanceTaskRole.TrialEvidence)
            return false;
        if (task.Status == HumanTaskInstanceStatus.Completed)
            return true;

        var lockFact = ReadLock(workflow);
        var lockState = await _mes.GetLockAsync(observation.TenantId, observation.EquipmentId, workflow.InstanceId, ct);
        if (lockState.Release is not null || lockFact.AssetId != observation.EquipmentId ||
            lockFact.RegisteredAt != lockState.RegisteredAt || observation.ObservedAt < lockFact.RegisteredAt ||
            observation.ObservedAt > DateTimeOffset.UtcNow || observation.Outcome != TrialOutcome.Passed)
        {
            return true;
        }

        await _humanTasks.CompleteAsync(new HumanTaskCompletionRequest
        {
            HumanTaskKey = task.Key,
            Outcome = "Approve",
            ActorId = AssetProductionDescriptors.MesEvidenceActor,
            ActorRoles = [AssetProductionDescriptors.MesEvidenceRole],
            Result = _stateRegistry.Capture(new AssetMaintenanceEvidenceFact
            {
                AssetId = observation.EquipmentId, RequesterId = ReadRequester(RestoreVariables(task.Input)),
                ActorId = AssetProductionDescriptors.MesEvidenceActor, Approved = true,
                Note = "MES trial evidence verified", Observation = observation
            })
        }, ct);
        return true;
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
        var tenantId = RequireTenant();
        var task = await _taskStore.GetAsync(new RuntimeInstanceKey(tenantId, humanTaskId), ct)
            ?? throw new InvalidOperationException("Maintenance HumanTask is unavailable.");
        if (task.TenantId != tenantId)
        {
            throw new UnauthorizedAccessException("The HumanTask belongs to another tenant.");
        }

        var taskRole = ResolveRole(task);
        var approved = string.Equals(outcome, "Approve", StringComparison.OrdinalIgnoreCase);
        var rejected = string.Equals(outcome, "Reject", StringComparison.OrdinalIgnoreCase);
        if (!approved && !rejected)
        {
            throw new ArgumentException("Outcome must be Approve or Reject.", nameof(outcome));
        }

        var values = RestoreVariables(task.Input);
        var assetId = ReadAssetId(values);
        var requesterId = ReadRequester(values);
        var workflow = task.WorkflowKey is { } workflowKey
            ? await _workflowStore.GetAsync(workflowKey, ct)
            : null;
        if (workflow is null)
            throw new InvalidOperationException("The maintenance workflow is unavailable.");
        var critical = workflow.WorkflowPin.Ref.Version == AssetProductionDescriptors.WorkflowVersion;
        if (critical && (!IsCriticalWorkflowForAsset(workflow, assetId) || ReadLock(workflow).AssetId != assetId))
        {
            throw new CapabilityFailureException("ASSET_PRODUCTION_WORKFLOW_INVALID", "The maintenance workflow or its production lock belongs to another asset.");
        }

        if (taskRole == AssetMaintenanceTaskRole.TrialEvidence)
        {
            throw new UnauthorizedAccessException("MES evidence requires the typed trial verification entry point.");
        }

        var requiredRole = taskRole == AssetMaintenanceTaskRole.Quality ? AssetProductionDescriptors.QualityRole : "asset-manager";
        if (!_currentUser.IsInRole(requiredRole))
        {
            throw new UnauthorizedAccessException($"The {requiredRole} role is required to complete this maintenance review.");
        }

        AssetMaintenanceEvidenceFact? trial = null;
        if (critical && taskRole == AssetMaintenanceTaskRole.Quality)
        {
            var initial = workflow.StepResults.LastOrDefault(result => result.StepId == AssetProductionDescriptors.InitialStepId && result.HumanTaskOutcome is not null);
            if (initial?.Output is null || _stateRegistry.Restore(initial.Output) is not AssetMaintenanceEvidenceFact decision ||
                !decision.Approved || decision.ActorId == _currentUser.Id)
            {
                throw new UnauthorizedAccessException("Quality sign-off requires a different approver from the maintenance supervisor.");
            }

            var evidenceStep = workflow.StepResults.LastOrDefault(result => result.StepId == AssetProductionDescriptors.TrialStepId && result.HumanTaskOutcome is not null);
            trial = evidenceStep?.Output is null ? null : _stateRegistry.Restore(evidenceStep.Output) as AssetMaintenanceEvidenceFact;
            if (trial is not { Approved: true, Observation.Outcome: TrialOutcome.Passed } ||
                trial.Observation.EquipmentId != assetId || trial.Observation.WorkOrderId != workflow.InstanceId ||
                trial.Observation.TenantId != tenantId)
            {
                throw new CapabilityFailureException("ASSET_MES_EVIDENCE_MISSING", "Quality sign-off requires persisted MES trial evidence.");
            }
        }

        var completedTask = await _humanTasks.CompleteAsync(new HumanTaskCompletionRequest
        {
            HumanTaskKey = task.Key,
            Outcome = approved ? "Approve" : "Reject",
            ActorId = _currentUser.Id,
            ActorRoles = _currentUser.Roles,
            Result = critical
                ? _stateRegistry.Capture(new AssetMaintenanceEvidenceFact
                {
                    AssetId = assetId, RequesterId = requesterId, ActorId = _currentUser.Id, Approved = approved, Note = note,
                    Observation = trial?.Observation
                })
                : _stateRegistry.Capture(new AssetMaintenanceDecisionFact
                {
                    AssetId = assetId, RequesterId = requesterId, ApproverId = _currentUser.Id, Approved = approved, Note = note
                })
        }, ct);

        if (approved && taskRole is AssetMaintenanceTaskRole.Initial or AssetMaintenanceTaskRole.Quality)
        {
            if (taskRole == AssetMaintenanceTaskRole.Initial)
            {
                await WaitForInitialContinuationAsync(completedTask, critical ? AssetMaintenanceTaskRole.TrialEvidence : AssetMaintenanceTaskRole.Terminal, ct);
            }
            else
            {
                await WaitForWorkflowCompletionAsync(workflow.Key, ct);
            }

            return;
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var asset = await _assets.GetAsync(tenantId, assetId, ct);
            if (asset?.Status is AssetStatus.Available or AssetStatus.Assigned)
            {
                return;
            }

            await Task.Delay(20, ct);
        }

        throw new CapabilityFailureException("ASSET_MAINTENANCE_COMPLETION_TIMEOUT", "The durable maintenance decision was not observed before the completion deadline.");
    }

    private async Task WaitForInitialContinuationAsync(HumanTaskInstance initialTask, AssetMaintenanceTaskRole expectedRole, CancellationToken ct)
    {
        var workflowKey = initialTask.WorkflowKey
            ?? throw new InvalidOperationException("Initial maintenance review is missing its Workflow correlation.");
        if (string.IsNullOrWhiteSpace(initialTask.CompletionEventId))
            throw new InvalidOperationException("Initial maintenance review has no durable completion identity.");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var acceptance = await _continuationAcceptances.GetAsync(
                new RuntimeTenantScope(initialTask.TenantId!), initialTask.CompletionEventId, ct);
            var workflow = await _workflowStore.GetAsync(workflowKey, ct);
            var finalTasks = await _taskStore.GetPendingByWorkflowAsync(workflowKey, ct);
            if (acceptance is not null
                && acceptance.HumanTaskKey == initialTask.Key
                && acceptance.WorkflowKey == workflowKey
                && string.Equals(acceptance.Outcome, initialTask.Outcome, StringComparison.Ordinal)
                && workflow?.Status is WorkflowInstanceStatus.Suspended or WorkflowInstanceStatus.Completed
                && (workflow.Status == WorkflowInstanceStatus.Completed
                    || (finalTasks.Count == 1 && IsKnownRole(finalTasks[0], expectedRole))))
                return;
            await Task.Delay(20, ct);
        }
        throw new CapabilityFailureException("ASSET_MAINTENANCE_CONTINUATION_TIMEOUT", "The durable initial review continuation was not observed before the completion deadline.");
    }

    private AssetMaintenanceTaskRole ResolveRole(HumanTaskInstance task)
    {
        return _taskContracts.Resolve(task.HumanTaskPin);
    }

    private bool IsKnownRole(HumanTaskInstance task, AssetMaintenanceTaskRole expected)
    {
        try { return ResolveRole(task) == expected; }
        catch (InvalidOperationException) { return false; }
    }

    private string RequireTenant() => _currentUser.TenantId ?? throw new UnauthorizedAccessException("A tenant context is required.");

    private async Task<WorkflowInstance> RequireCriticalWorkflowAsync(Guid assetId, string workflowInstanceId, string tenantId, CancellationToken ct)
    {
        var workflow = await _workflowStore.GetAsync(new RuntimeInstanceKey(tenantId, workflowInstanceId), ct)
            ?? throw new InvalidOperationException("The critical maintenance workflow is unavailable.");
        if (!IsCriticalWorkflowForAsset(workflow, assetId))
            throw new CapabilityFailureException("ASSET_PRODUCTION_WORKFLOW_INVALID", "The workflow is not the approved critical-equipment maintenance version for this asset.");

        return workflow;
    }

    private bool IsCriticalWorkflowForAsset(WorkflowInstance workflow, Guid assetId) =>
        workflow.WorkflowPin.Ref.Id == AssetContractIds.MaintenanceWorkflow &&
        workflow.WorkflowPin.Ref.Version == AssetProductionDescriptors.WorkflowVersion &&
        workflow.Variables.TryGetValue("assetId", out var encodedAsset) &&
        _stateRegistry.Restore(encodedAsset) is Guid storedAsset && storedAsset == assetId;

    private AssetProductionLockFact ReadLock(WorkflowInstance workflow) =>
        workflow.Variables.TryGetValue("productionLock", out var encoded) &&
        _stateRegistry.Restore(encoded) is AssetProductionLockFact fact && fact.TenantId == workflow.TenantId &&
        fact.WorkflowInstanceId == workflow.InstanceId
            ? fact
            : throw new CapabilityFailureException("ASSET_MES_LOCK_MISSING", "The maintenance workflow has no recorded production lock.");

    private static Guid ReadAssetId(Dictionary<string, object?> values) =>
        values["assetId"] is Guid id ? id : Guid.Parse(values["assetId"]!.ToString()!);

    private static string ReadRequester(Dictionary<string, object?> values) =>
        values.TryGetValue("requesterId", out var requester) && requester?.ToString() is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException("Maintenance requester is missing from the durable workflow state.");

    private async Task WaitForWorkflowCompletionAsync(RuntimeInstanceKey workflowKey, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var workflow = await _workflowStore.GetAsync(workflowKey, ct);
            if (workflow?.Status == WorkflowInstanceStatus.Completed)
            {
                return;
            }

            await Task.Delay(20, ct);
        }

        throw new CapabilityFailureException("ASSET_MAINTENANCE_CONTINUATION_TIMEOUT", "The quality approval did not complete the maintenance workflow.");
    }

    private Dictionary<string, object?> RestoreVariables(RuntimeStateValue? input)
        => input is not null && _stateRegistry.Restore(input) is RuntimeStateBag bag
            ? bag.Values.ToDictionary(pair => pair.Key, pair => _stateRegistry.Restore(pair.Value))
            : throw new InvalidOperationException("Maintenance workflow variables are unavailable.");
}

[GenerateOutboxConsumerActivation]
public sealed partial class AssetMaintenanceDecisionConsumer : CrestCreates.Runtime.Delivery.Abstractions.Handlers.IOutboxRequiredConsumer<HumanTaskCompletedEvent>
{
    private readonly IHumanTaskInstanceStore _tasks;
    private readonly IWorkflowInstanceStore _workflows;
    private readonly IRuntimeStateContractRegistry _stateRegistry;
    private readonly AssetMaintenanceTaskContractResolver _taskContracts;
    private readonly ICapabilityDispatcher _dispatcher;
    private readonly AssetExecutionIdentity _identity;
    private readonly ICurrentPrincipalAccessor _principalAccessor;
    private readonly ILogger<AssetMaintenanceDecisionConsumer> _logger;

    public AssetMaintenanceDecisionConsumer(
        IHumanTaskInstanceStore tasks,
        IWorkflowInstanceStore workflows,
        IRuntimeStateContractRegistry stateRegistry,
        AssetMaintenanceTaskContractResolver taskContracts,
        ICapabilityDispatcher dispatcher,
        AssetExecutionIdentity identity,
        ICurrentPrincipalAccessor principalAccessor,
        ILogger<AssetMaintenanceDecisionConsumer> logger)
    {
        _tasks = tasks;
        _workflows = workflows;
        _stateRegistry = stateRegistry;
        _taskContracts = taskContracts;
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
        AssetMaintenanceDecisionFact fact;
        var workflow = task.WorkflowKey is { } workflowKey ? await _workflows.GetAsync(workflowKey, ct) : null;
        if (workflow is null)
            return CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Conflict("ASSET_MAINTENANCE_FACT_INVALID", "Maintenance completion has no durable workflow.");
        var critical = workflow.WorkflowPin.Ref.Version == AssetProductionDescriptors.WorkflowVersion;
        if (critical)
        {
            if (_stateRegistry.Restore(payload.Result) is not AssetMaintenanceEvidenceFact evidence ||
                workflow.Variables.TryGetValue("assetId", out var encodedAsset) is false ||
                _stateRegistry.Restore(encodedAsset) is not Guid workflowAsset || evidence.AssetId != workflowAsset)
                return CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Conflict("ASSET_MAINTENANCE_FACT_INVALID", "Critical maintenance completion fact has an invalid contract.");
            fact = new AssetMaintenanceDecisionFact
            {
                AssetId = evidence.AssetId, RequesterId = evidence.RequesterId, ApproverId = evidence.ActorId,
                Approved = evidence.Approved, Note = evidence.Note
            };
        }
        else if (_stateRegistry.Restore(payload.Result) is AssetMaintenanceDecisionFact legacy)
        {
            fact = legacy;
        }
        else
        {
            return CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Conflict("ASSET_MAINTENANCE_FACT_INVALID", "Maintenance completion fact has an invalid contract.");
        }
        if (payload.HumanTaskPin != task.HumanTaskPin)
            return CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Conflict("ASSET_MAINTENANCE_TASK_INVALID", "Maintenance completion pin does not match the durable HumanTask contract.");
        AssetMaintenanceTaskRole role;
        try { role = _taskContracts.Resolve(payload.HumanTaskPin); }
        catch (InvalidOperationException exception)
        { return CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Conflict("ASSET_MAINTENANCE_TASK_INVALID", exception.Message); }
        var canonicalApproved = payload.Outcome switch
        {
            "Approve" => true,
            "Reject" => false,
            _ => (bool?)null
        };
        if (!canonicalApproved.HasValue
            || !string.Equals(task.Outcome, payload.Outcome, StringComparison.Ordinal)
            || canonicalApproved.Value != fact.Approved)
            return CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Conflict("ASSET_MAINTENANCE_FACT_INVALID", "Maintenance completion outcome does not match the canonical decision fact.");
        var isInitial = role == AssetMaintenanceTaskRole.Initial;
        if (critical && role == AssetMaintenanceTaskRole.TrialEvidence)
        {
            if (!fact.Approved || _stateRegistry.Restore(payload.Result) is not AssetMaintenanceEvidenceFact
                {
                    Observation.Outcome: TrialOutcome.Passed
                })
                return CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Conflict("ASSET_MAINTENANCE_FACT_INVALID", "MES evidence completion requires a persisted passing observation.");
            return CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Accepted();
        }

        if (isInitial && fact.Approved)
        {
            _logger.LogInformation("Acknowledging initial Asset maintenance approval for {AssetId} without applying a terminal decision.", fact.AssetId);
            return CrestCreates.Runtime.Delivery.Abstractions.Handlers.OutboxRequiredConsumerResult.Accepted();
        }
        var executionRole = critical && role == AssetMaintenanceTaskRole.Quality ? AssetProductionDescriptors.QualityRole : "asset-manager";
        _identity.Set(task.TenantId, fact.ApproverId, dataScope: CrestCreates.Domain.Shared.Enums.DataScope.Tenant, roles: [executionRole]);
        var command = new CrestCreates.Sample.AssetManagement.Application.Handlers.MaintenanceDecisionCommand(
            fact.AssetId,
            new CrestCreates.Sample.AssetManagement.Contracts.Dtos.MaintenanceDecisionInput { AssetId = fact.AssetId, Approved = fact.Approved, Note = fact.Note },
            task.WorkflowInstanceId ?? string.Empty,
            fact.RequesterId);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, fact.ApproverId), new Claim(ClaimTypes.Role, executionRole) }, "outbox"));
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
