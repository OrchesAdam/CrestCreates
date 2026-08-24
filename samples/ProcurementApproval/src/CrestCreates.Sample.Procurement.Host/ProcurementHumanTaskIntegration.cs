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
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
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
            completed.HumanTaskKey.InstanceId,
            _ => new ProcurementDecisionReconciliation(
                completed.HumanTaskKey.InstanceId,
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
    ProcurementDecisionReconciliationStore reconciliation,
    IRuntimeStateContractRegistry stateRegistry) : ILocalEventBus
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
            var taskKey = completed.HumanTaskKey;
            var nextHandlerIndex = reconciliation.GetNextHandlerIndex(taskKey.InstanceId);
            for (var index = nextHandlerIndex; index < handlers.Length; index++)
            {
                await handlers[index].HandleAsync(completed, cancellationToken).ConfigureAwait(false);
                reconciliation.MarkHandlerCompleted(taskKey.InstanceId, index + 1);
            }
            await RequireCompletedWorkflowAsync(completed, cancellationToken).ConfigureAwait(false);
            reconciliation.MarkResolved(taskKey.InstanceId);
        }
        catch (Exception exception)
        {
            var task = await services.GetRequiredService<IHumanTaskInstanceStore>()
                .GetAsync(completed.HumanTaskKey, CancellationToken.None)
                .ConfigureAwait(false);
            var workflow = string.IsNullOrWhiteSpace(task?.WorkflowInstanceId)
                ? null
                : await services.GetRequiredService<IWorkflowInstanceStore>()
                    .GetAsync(task.WorkflowKey!.Value, CancellationToken.None)
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
            .GetAsync(completed.HumanTaskKey, cancellationToken)
            .ConfigureAwait(false);
        var workflow = string.IsNullOrWhiteSpace(task?.WorkflowInstanceId)
            ? null
            : await services.GetRequiredService<IWorkflowInstanceStore>()
                .GetAsync(task.WorkflowKey!.Value, cancellationToken)
                .ConfigureAwait(false);
        if (workflow?.Status != WorkflowInstanceStatus.Completed)
        {
            throw new ProcurementDecisionDispatchException(
                "PROCUREMENT_WORKFLOW_CONTINUATION_INCOMPLETE",
                "HumanTask decision did not reach a completed procurement workflow state.");
        }
    }

    private Guid? RequestId(HumanTaskInstance? task)
    {
        if (task?.Input is null || stateRegistry.Restore(task.Input) is not RuntimeStateBag bag
            || bag.Values.Count == 0
            || !bag.Values.TryGetValue("requestId", out var requestValue)
            || stateRegistry.Restore(requestValue) is not object value)
            return null;
        return value is Guid requestId || Guid.TryParse(value.ToString(), out requestId)
            ? requestId
            : null;
    }
}

public sealed class ProcurementHumanTaskDecisionHandler(
    IHumanTaskInstanceStore tasks,
    ICapabilityDispatcher dispatcher,
    IRuntimeStateContractRegistry stateRegistry)
    : IOutboxRequiredConsumer<HumanTaskCompletedEvent>
{
    public const string ConsumerIdValue = "crest.sample.procurement.decision/v1";
    public string ConsumerId => ConsumerIdValue;

    public async ValueTask<OutboxRequiredConsumerResult> ConsumeAsync(
        HumanTaskCompletedEvent payload,
        OutboxDeliveryContext context,
        CancellationToken cancellationToken = default)
    {
        await HandleAsync(payload, cancellationToken).ConfigureAwait(false);
        return OutboxRequiredConsumerResult.Accepted();
    }

    public async Task HandleAsync(
        HumanTaskCompletedEvent @event,
        CancellationToken cancellationToken = default)
    {
        var task = await tasks.GetAsync(@event.HumanTaskKey, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Completed HumanTask instance is unavailable.");
        var variables = RestoreVariables(task.Input, stateRegistry);
        if (string.IsNullOrWhiteSpace(task.TenantId))
            throw new UnauthorizedAccessException("The completed procurement HumanTask has no persisted tenant.");

        var requestId = variables["requestId"] is Guid value
            ? value
            : Guid.Parse(RequiredString(variables, "requestId"));
        CapabilityExecutionResult result;
        if (string.Equals(@event.Outcome, "Approve", StringComparison.OrdinalIgnoreCase))
        {
            var input = new ApproveProcurementRequestInput
            {
                RequestId = requestId,
                Comment = @event.Result is null ? "Approved through HumanTask" : stateRegistry.Restore(@event.Result) as string ?? "Approved through HumanTask"
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
                Reason = @event.Result is null ? "Rejected through HumanTask" : stateRegistry.Restore(@event.Result) as string ?? "Rejected through HumanTask"
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

    private static Dictionary<string, object?> RestoreVariables(RuntimeStateValue? input, IRuntimeStateContractRegistry registry)
    {
        if (input is null || registry.Restore(input) is not RuntimeStateBag bag)
            throw new InvalidOperationException("Procurement workflow variables are unavailable.");
        return bag.Values.ToDictionary(pair => pair.Key, pair => registry.Restore(pair.Value));
    }

    private static string RequiredString(Dictionary<string, object?> variables, string key)
        => variables.TryGetValue(key, out var value) && value is not null
            ? value.ToString()!
            : throw new InvalidOperationException($"Workflow variable '{key}' is required.");
}

public sealed class ProcurementApprovalTaskService(
    IWorkflowEngine workflowEngine,
    IWorkflowInstanceStore workflows,
    IHumanTaskRuntime runtime,
    IHumanTaskInstanceStore tasks,
    InMemoryProcurementRequestStore requests,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IRuntimeStateContractRegistry stateRegistry)
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
                    InputVariables = new Dictionary<string, RuntimeStateValue>
                    {
                        ["requestId"] = stateRegistry.Capture(requestId),
                        ["requesterId"] = stateRegistry.Capture(requesterId)
                    }
                },
                cancellationToken).ConfigureAwait(false);
            var pending = await tasks.GetPendingByWorkflowAsync(
                workflow.Key,
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

        var workflow = await workflows.GetAsync(new RuntimeInstanceKey(tenantId, request.WorkflowInstanceId), cancellationToken)
            .ConfigureAwait(false);
        if (workflow?.Status != WorkflowInstanceStatus.Suspended
            || string.IsNullOrWhiteSpace(workflow.WaitingHumanTaskId))
        {
            throw new CapabilityFailureException(
                "CAPABILITY_DECISION_STATE_INVALID",
                "The approval workflow is not suspended on a HumanTask.");
        }

        var task = await tasks.GetAsync(workflow.WaitingHumanTaskKey!.Value, cancellationToken)
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
            var pending = await tasks.GetPendingByWorkflowAsync(workflow.Key, cancellationToken)
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
        var task = await tasks.GetAsync(new RuntimeInstanceKey(tenant.CurrentTenantId, humanTaskId), cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("HumanTask is unavailable.");
        var variables = RestoreVariables(task.Input, stateRegistry);
        var requesterId = variables["requesterId"]?.ToString();
        var requestId = variables.TryGetValue("requestId", out var requestIdValue)
            && requestIdValue is Guid parsedRequestId
            ? parsedRequestId
            : (Guid?)null;
        if (string.IsNullOrWhiteSpace(task.TenantId)
            || !string.Equals(task.TenantId, tenant.CurrentTenantId, StringComparison.Ordinal))
            throw Forbidden("The HumanTask belongs to another tenant.");
        if (!currentUser.IsInRole("procurement-manager"))
            throw Forbidden("The procurement-manager role is required.");
        if (string.Equals(requesterId, currentUser.Id, StringComparison.Ordinal))
            throw Forbidden("A requester cannot complete their own approval task.");

        await runtime.CompleteAsync(new HumanTaskCompletionRequest
        {
            HumanTaskKey = task.Key,
            Outcome = outcome,
            Result = stateRegistry.Capture(comment)
        }, cancellationToken).ConfigureAwait(false);

        // Completion is durably handed to the transactional outbox.  The runtime
        // worker may therefore resume the workflow just after CompleteAsync returns;
        // this application facade keeps its existing contract of returning only once
        // the decision is observable by the caller while still using the outbox as the
        // authoritative delivery path.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentTask = await tasks.GetAsync(task.Key, cancellationToken).ConfigureAwait(false);
            var currentWorkflow = task.WorkflowKey is { } workflowKey
                ? await workflows.GetAsync(workflowKey, cancellationToken).ConfigureAwait(false)
                : null;
            var currentRequest = requestId is { } id
                ? requests.GetById(task.TenantId, id)
                : null;
            var expectedRequestStatus = string.Equals(outcome, "Approve", StringComparison.OrdinalIgnoreCase)
                ? ProcurementRequestStatus.Approved
                : string.Equals(outcome, "Reject", StringComparison.OrdinalIgnoreCase)
                    ? ProcurementRequestStatus.Rejected
                    : (ProcurementRequestStatus?)null;
            if (currentTask?.Status == HumanTaskInstanceStatus.Completed
                && currentWorkflow?.Status is WorkflowInstanceStatus.Completed
                    or WorkflowInstanceStatus.Failed
                    or WorkflowInstanceStatus.Compensated
                && (expectedRequestStatus is null || currentRequest?.Status == expectedRequestStatus))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken).ConfigureAwait(false);
        }
    }

    private static Dictionary<string, object?> RestoreVariables(RuntimeStateValue? input, IRuntimeStateContractRegistry registry)
    {
        if (input is null || registry.Restore(input) is not RuntimeStateBag bag)
            throw new InvalidOperationException("Procurement workflow variables are unavailable.");
        return bag.Values.ToDictionary(pair => pair.Key, pair => registry.Restore(pair.Value));
    }

    private static CapabilityFailureException DecisionStateInvalid(string message)
        => new("CAPABILITY_DECISION_STATE_INVALID", message);

    private static CapabilityFailureException Forbidden(string message)
        => new("CAPABILITY_FORBIDDEN", message);
}
