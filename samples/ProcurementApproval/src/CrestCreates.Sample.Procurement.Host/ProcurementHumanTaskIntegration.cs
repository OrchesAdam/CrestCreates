using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Sample.Procurement.Application;
using CrestCreates.Sample.Procurement.Contracts;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using CrestCreates.Sample.Procurement.Contracts.Json;
using CrestCreates.Sample.Procurement.Domain;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Sample.Procurement.Host;

public sealed record ProcurementDecisionReconciliation(
    string HumanTaskInstanceId,
    string? WorkflowInstanceId,
    Guid? RequestId,
    string Outcome,
    string ErrorCode,
    string ErrorMessage,
    HumanTaskInstanceStatus ObservedTaskStatus,
    WorkflowInstanceStatus? ObservedWorkflowStatus,
    int AttemptCount,
    bool IsResolved);

public sealed class ProcurementDecisionReconciliationStore
{
    private readonly ConcurrentDictionary<string, ProcurementDecisionReconciliation> _records = new();
    private readonly ConcurrentDictionary<string, int> _nextHandlerIndexes = new();

    public void RecordFailure(
        HumanTaskCompletedEvent completed,
        Guid? requestId,
        HumanTaskInstanceStatus taskStatus,
        string? workflowInstanceId,
        WorkflowInstanceStatus? workflowStatus,
        Exception exception)
    {
        var errorCode = exception is ProcurementDecisionDispatchException decision
            ? decision.ErrorCode
            : "PROCUREMENT_DECISION_CONTINUATION_FAILED";
        _records.AddOrUpdate(
            completed.HumanTaskInstanceId,
            _ => new ProcurementDecisionReconciliation(
                completed.HumanTaskInstanceId,
                workflowInstanceId,
                requestId,
                completed.Outcome,
                errorCode,
                exception.Message,
                taskStatus,
                workflowStatus,
                1,
                false),
            (_, current) => current with
            {
                ErrorCode = errorCode,
                ErrorMessage = exception.Message,
                ObservedTaskStatus = taskStatus,
                WorkflowInstanceId = workflowInstanceId,
                ObservedWorkflowStatus = workflowStatus,
                AttemptCount = current.AttemptCount + 1,
                IsResolved = false
            });
    }

    public void MarkResolved(string humanTaskInstanceId)
    {
        if (_records.TryGetValue(humanTaskInstanceId, out var current))
            _records[humanTaskInstanceId] = current with { IsResolved = true };
    }

    public ProcurementDecisionReconciliation? Get(string humanTaskInstanceId)
        => _records.GetValueOrDefault(humanTaskInstanceId);

    public int GetNextHandlerIndex(string humanTaskInstanceId)
        => _nextHandlerIndexes.GetValueOrDefault(humanTaskInstanceId);

    public void MarkHandlerCompleted(string humanTaskInstanceId, int nextHandlerIndex)
        => _nextHandlerIndexes[humanTaskInstanceId] = nextHandlerIndex;
}

public sealed class ProcurementDecisionDispatchException(
    string errorCode,
    string message) : InvalidOperationException(message)
{
    public string ErrorCode { get; } = errorCode;
}

public sealed class ProcurementLocalEventBus(
    IServiceProvider services,
    ProcurementDecisionReconciliationStore reconciliation) : ILocalEventBus
{
    public Task PublishAsync(ILocalEvent @event, CancellationToken cancellationToken = default)
        => @event is HumanTaskCompletedEvent completed
            ? PublishAsync(completed, cancellationToken)
            : Task.CompletedTask;

    public async Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : ILocalEvent
    {
        if (@event is not HumanTaskCompletedEvent completed)
            return;

        try
        {
            var handlers = services.GetServices<ILocalEventHandler<HumanTaskCompletedEvent>>().ToArray();
            var nextHandlerIndex = reconciliation.GetNextHandlerIndex(completed.HumanTaskInstanceId);
            for (var index = nextHandlerIndex; index < handlers.Length; index++)
            {
                await handlers[index].HandleAsync(completed, cancellationToken).ConfigureAwait(false);
                reconciliation.MarkHandlerCompleted(completed.HumanTaskInstanceId, index + 1);
            }
            await RequireCompletedWorkflowAsync(completed, cancellationToken).ConfigureAwait(false);
            reconciliation.MarkResolved(completed.HumanTaskInstanceId);
        }
        catch (Exception exception)
        {
            var task = await services.GetRequiredService<IHumanTaskInstanceStore>()
                .GetByIdAsync(completed.HumanTaskInstanceId, CancellationToken.None)
                .ConfigureAwait(false);
            var workflow = string.IsNullOrWhiteSpace(task?.WorkflowInstanceId)
                ? null
                : await services.GetRequiredService<IWorkflowInstanceStore>()
                    .GetAsync(task.WorkflowInstanceId, CancellationToken.None)
                    .ConfigureAwait(false);
            reconciliation.RecordFailure(
                completed,
                RequestId(task),
                task?.Status ?? HumanTaskInstanceStatus.Completed,
                task?.WorkflowInstanceId,
                workflow?.Status,
                exception);
            throw;
        }
    }

    private async Task RequireCompletedWorkflowAsync(
        HumanTaskCompletedEvent completed,
        CancellationToken cancellationToken)
    {
        var task = await services.GetRequiredService<IHumanTaskInstanceStore>()
            .GetByIdAsync(completed.HumanTaskInstanceId, cancellationToken)
            .ConfigureAwait(false);
        var workflow = string.IsNullOrWhiteSpace(task?.WorkflowInstanceId)
            ? null
            : await services.GetRequiredService<IWorkflowInstanceStore>()
                .GetAsync(task.WorkflowInstanceId, cancellationToken)
                .ConfigureAwait(false);
        if (workflow?.Status != WorkflowInstanceStatus.Completed)
        {
            throw new ProcurementDecisionDispatchException(
                "PROCUREMENT_WORKFLOW_CONTINUATION_INCOMPLETE",
                "HumanTask decision did not reach a completed procurement workflow state.");
        }
    }

    private static Guid? RequestId(HumanTaskInstance? task)
    {
        if (task?.Input is not Dictionary<string, object?> variables
            || !variables.TryGetValue("requestId", out var value)
            || value is null)
            return null;
        return value is Guid requestId || Guid.TryParse(value.ToString(), out requestId)
            ? requestId
            : null;
    }
}

public sealed class ProcurementHumanTaskCompletionFailurePolicy(
    ProcurementLocalEventBus eventBus) : IHumanTaskCompletionFailurePolicy
{
    public Task RecoverAsync(
        HumanTaskInstance instance,
        HumanTaskCompletedEvent completion,
        CancellationToken cancellationToken = default)
        => eventBus.PublishAsync(completion, cancellationToken);
}

public sealed class ProcurementHumanTaskDecisionHandler(
    IHumanTaskInstanceStore tasks,
    ICapabilityDispatcher dispatcher,
    ITenantContext tenant,
    ICurrentUser currentUser)
    : ILocalEventHandler<HumanTaskCompletedEvent>
{
    public async Task HandleAsync(
        HumanTaskCompletedEvent @event,
        CancellationToken cancellationToken = default)
    {
        var task = await tasks.GetByIdAsync(@event.HumanTaskInstanceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Completed HumanTask instance is unavailable.");
        var variables = task.Input as Dictionary<string, object?>
            ?? throw new InvalidOperationException("Procurement workflow variables are unavailable.");
        if (string.IsNullOrWhiteSpace(task.TenantId)
            || !string.Equals(task.TenantId, tenant.CurrentTenantId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The HumanTask belongs to another tenant.");
        if (!currentUser.IsInRole("procurement-manager"))
            throw new UnauthorizedAccessException("The procurement-manager role is required.");

        var requestId = variables["requestId"] is Guid value
            ? value
            : Guid.Parse(RequiredString(variables, "requestId"));
        CapabilityExecutionResult result;
        if (string.Equals(@event.Outcome, "Approve", StringComparison.OrdinalIgnoreCase))
        {
            var input = new ApproveProcurementRequestInput
            {
                RequestId = requestId,
                Comment = @event.Result as string ?? "Approved through HumanTask"
            };
            result = await dispatcher.DispatchAsync(
                ProcurementContractIds.ApplyApprovalDecisionCapability,
                InvocationSource.HumanTask,
                input,
                context => context.InputJson = JsonSerializer.SerializeToElement(
                    input,
                    ProcurementJsonContext.Default.ApproveProcurementRequestInput),
                ct: cancellationToken).ConfigureAwait(false);
        }
        else if (string.Equals(@event.Outcome, "Reject", StringComparison.OrdinalIgnoreCase))
        {
            var input = new RejectProcurementRequestInput
            {
                RequestId = requestId,
                Reason = @event.Result as string ?? "Rejected through HumanTask"
            };
            result = await dispatcher.DispatchAsync(
                ProcurementContractIds.ApplyRejectionDecisionCapability,
                InvocationSource.HumanTask,
                input,
                context => context.InputJson = JsonSerializer.SerializeToElement(
                    input,
                    ProcurementJsonContext.Default.RejectProcurementRequestInput),
                ct: cancellationToken).ConfigureAwait(false);
        }
        else
        {
            throw new InvalidOperationException("Unsupported procurement approval outcome.");
        }

        if (!result.IsSuccess)
            throw new ProcurementDecisionDispatchException(
                result.ErrorCode ?? "PROCUREMENT_DECISION_FAILED",
                $"HumanTask decision dispatch failed with '{result.ErrorCode}'.");
    }

    private static string RequiredString(Dictionary<string, object?> variables, string key)
        => variables.TryGetValue(key, out var value) && value is not null
            ? value.ToString()!
            : throw new InvalidOperationException($"Workflow variable '{key}' is required.");
}

public sealed class ProcurementApprovalTaskService(
    IWorkflowEngine workflowEngine,
    IWorkflowInstanceStore workflows,
    InMemoryWorkflowInstanceStore inMemoryWorkflows,
    IHumanTaskRuntime runtime,
    IHumanTaskInstanceStore tasks,
    InMemoryHumanTaskInstanceStore inMemoryTasks,
    InMemoryProcurementRequestStore requests,
    ITenantContext tenant,
    ICurrentUser currentUser)
    : IProcurementApprovalOrchestrator
{
    public async Task<ProcurementApprovalWorkflowLease> StartAsync(
        Guid requestId,
        string tenantId,
        string requesterId,
        CancellationToken cancellationToken = default)
    {
        WorkflowInstance? workflow = null;
        try
        {
            workflow = await workflowEngine.ExecuteAsync(
                new WorkflowExecutionRequest
                {
                    WorkflowId = ProcurementContractIds.ApprovalWorkflow,
                    TenantId = tenantId,
                    InputVariables = new Dictionary<string, object?>
                    {
                        ["requestId"] = requestId,
                        ["requesterId"] = requesterId
                    }
                },
                cancellationToken).ConfigureAwait(false);
            var pending = await tasks.GetPendingByWorkflowAsync(
                workflow.InstanceId,
                cancellationToken).ConfigureAwait(false);
            if (workflow.Status != WorkflowInstanceStatus.Suspended
                || !string.Equals(workflow.TenantId, tenantId, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(workflow.WaitingHumanTaskId)
                || pending.Count != 1
                || !string.Equals(pending[0].Id, workflow.WaitingHumanTaskId, StringComparison.Ordinal)
                || !string.Equals(pending[0].TenantId, tenantId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Approval workflow must suspend with exactly one correlated pending HumanTask.");
            }

            return new ProcurementApprovalWorkflowLease(workflow.InstanceId, pending[0].Id);
        }
        catch (Exception exception)
        {
            RollbackCreatedRuntime(tenantId, requestId, workflow?.InstanceId);
            if (exception is OperationCanceledException)
                throw;
            throw new CapabilityFailureException(
                CapabilityExecutionErrorCodes.DependencyUnavailable,
                $"The approval workflow could not be established: {exception.Message}");
        }
    }

    public Task RollbackAsync(
        ProcurementApprovalWorkflowLease lease,
        CancellationToken cancellationToken = default)
    {
        inMemoryTasks.TryRemove(lease.HumanTaskInstanceId);
        inMemoryWorkflows.TryRemove(lease.WorkflowInstanceId);
        return Task.CompletedTask;
    }

    public async Task CompleteDecisionAsync(
        Guid requestId,
        string outcome,
        string comment,
        CancellationToken cancellationToken = default)
    {
        var tenantId = tenant.CurrentTenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
            throw Forbidden("A trusted tenant is required.");
        var request = requests.GetById(tenantId, requestId)
            ?? throw new CapabilityFailureException(
                "CAPABILITY_RESOURCE_NOT_FOUND",
                $"Procurement request '{requestId}' is unavailable.");
        var isApproval = string.Equals(outcome, "Approve", StringComparison.OrdinalIgnoreCase);
        var isRejection = string.Equals(outcome, "Reject", StringComparison.OrdinalIgnoreCase);
        var matchingTerminalDecision =
            request.Status == ProcurementRequestStatus.Approved && isApproval
            || request.Status == ProcurementRequestStatus.Rejected && isRejection;
        var oppositeTerminalDecision =
            request.Status == ProcurementRequestStatus.Approved && isRejection
            || request.Status == ProcurementRequestStatus.Rejected && isApproval;
        if (oppositeTerminalDecision)
        {
            throw new CapabilityFailureException(
                "CAPABILITY_DECISION_CONFLICT",
                $"Procurement request '{requestId}' already has the opposite decision.");
        }
        if ((request.Status != ProcurementRequestStatus.PendingApproval && !matchingTerminalDecision)
            || string.IsNullOrWhiteSpace(request.WorkflowInstanceId))
        {
            throw new CapabilityFailureException(
                "CAPABILITY_DECISION_CONFLICT",
                $"Procurement request '{requestId}' has no pending approval decision.");
        }

        var workflow = await workflows.GetAsync(request.WorkflowInstanceId, cancellationToken)
            .ConfigureAwait(false);
        if (workflow?.Status != WorkflowInstanceStatus.Suspended
            || string.IsNullOrWhiteSpace(workflow.WaitingHumanTaskId))
        {
            throw new CapabilityFailureException(
                "CAPABILITY_DECISION_STATE_INVALID",
                "The approval workflow is not suspended on a HumanTask.");
        }

        var task = await tasks.GetByIdAsync(workflow.WaitingHumanTaskId, cancellationToken)
            .ConfigureAwait(false);
        if (task is null
            || !string.Equals(task.WorkflowInstanceId, workflow.InstanceId, StringComparison.Ordinal)
            || !string.Equals(task.TenantId, tenantId, StringComparison.Ordinal))
        {
            throw new CapabilityFailureException(
                "CAPABILITY_DECISION_STATE_INVALID",
                "The workflow waiting HumanTask is unavailable or belongs to another tenant.");
        }

        if (matchingTerminalDecision
            && task.Status != HumanTaskInstanceStatus.CompletionDispatchFailed)
            throw DecisionStateInvalid("A terminal decision can resume only from an explicit completion failure.");
        if (task.Status == HumanTaskInstanceStatus.CompletionDispatchFailed
            && !string.Equals(task.Outcome, outcome, StringComparison.OrdinalIgnoreCase))
            throw new CapabilityFailureException(
                "CAPABILITY_DECISION_CONFLICT",
                "The requested decision conflicts with the failed HumanTask completion outcome.");
        if (task.Status != HumanTaskInstanceStatus.CompletionDispatchFailed)
        {
            var pending = await tasks.GetPendingByWorkflowAsync(workflow.InstanceId, cancellationToken)
                .ConfigureAwait(false);
            if (pending.Count != 1 || pending[0].Id != task.Id)
                throw DecisionStateInvalid("Exactly one correlated pending HumanTask is required.");
        }

        await CompleteAsync(task.Id, outcome, comment, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task CompleteAsync(
        string humanTaskId,
        string outcome,
        string comment,
        CancellationToken cancellationToken = default)
    {
        var task = await tasks.GetByIdAsync(humanTaskId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("HumanTask is unavailable.");
        var variables = task.Input as Dictionary<string, object?>
            ?? throw new InvalidOperationException("Procurement workflow variables are unavailable.");
        var requesterId = variables["requesterId"]?.ToString();
        if (string.IsNullOrWhiteSpace(task.TenantId)
            || !string.Equals(task.TenantId, tenant.CurrentTenantId, StringComparison.Ordinal))
            throw Forbidden("The HumanTask belongs to another tenant.");
        if (!currentUser.IsInRole("procurement-manager"))
            throw Forbidden("The procurement-manager role is required.");
        if (string.Equals(requesterId, currentUser.Id, StringComparison.Ordinal))
            throw Forbidden("A requester cannot complete their own approval task.");

        await runtime.CompleteAsync(new HumanTaskCompletionRequest
        {
            HumanTaskInstanceId = humanTaskId,
            Outcome = outcome,
            Result = comment
        }, cancellationToken).ConfigureAwait(false);
    }

    private void RollbackCreatedRuntime(
        string tenantId,
        Guid requestId,
        string? workflowInstanceId)
    {
        var workflowIds = inMemoryWorkflows.GetAll()
            .Where(instance =>
                (string.Equals(instance.TenantId, tenantId, StringComparison.Ordinal)
                 && string.Equals(instance.InstanceId, workflowInstanceId, StringComparison.Ordinal))
                || MatchesRequest(instance, tenantId, requestId))
            .Select(instance => instance.InstanceId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var task in inMemoryTasks.GetAll()
                     .Where(task => task.WorkflowInstanceId is not null
                         && workflowIds.Contains(task.WorkflowInstanceId)))
            inMemoryTasks.TryRemove(task.Id);
        foreach (var instanceId in workflowIds)
            inMemoryWorkflows.TryRemove(instanceId);
    }

    private static bool MatchesRequest(
        WorkflowInstance instance,
        string tenantId,
        Guid requestId)
        => string.Equals(instance.TenantId, tenantId, StringComparison.Ordinal)
            && instance.Variables.TryGetValue("requestId", out var value)
            && (value is Guid id ? id == requestId : Guid.TryParse(value?.ToString(), out id) && id == requestId);

    private static CapabilityFailureException DecisionStateInvalid(string message)
        => new("CAPABILITY_DECISION_STATE_INVALID", message);

    private static CapabilityFailureException Forbidden(string message)
        => new("CAPABILITY_FORBIDDEN", message);
}
