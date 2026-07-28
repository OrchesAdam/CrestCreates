using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Sample.Procurement.Contracts;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using CrestCreates.Sample.Procurement.Contracts.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Sample.Procurement.Host;

public sealed class ProcurementLocalEventBus(IServiceProvider services) : ILocalEventBus
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

        foreach (var handler in services.GetServices<ILocalEventHandler<HumanTaskCompletedEvent>>())
            await handler.HandleAsync(completed, cancellationToken).ConfigureAwait(false);
    }
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
        var expectedTenant = RequiredString(variables, "tenantId");
        if (!string.Equals(expectedTenant, tenant.CurrentTenantId, StringComparison.Ordinal))
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
                ProcurementContractIds.ApproveCapability,
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
                ProcurementContractIds.RejectCapability,
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
            throw new InvalidOperationException($"HumanTask decision dispatch failed with '{result.ErrorCode}'.");
    }

    private static string RequiredString(Dictionary<string, object?> variables, string key)
        => variables.TryGetValue(key, out var value) && value is not null
            ? value.ToString()!
            : throw new InvalidOperationException($"Workflow variable '{key}' is required.");
}

public sealed class ProcurementApprovalTaskService(
    IHumanTaskRuntime runtime,
    IHumanTaskInstanceStore tasks,
    ITenantContext tenant,
    ICurrentUser currentUser)
{
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
        var taskTenant = variables["tenantId"]?.ToString();
        var requesterId = variables["requesterId"]?.ToString();
        if (string.IsNullOrWhiteSpace(taskTenant)
            || !string.Equals(taskTenant, tenant.CurrentTenantId, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The HumanTask belongs to another tenant.");
        if (!currentUser.IsInRole("procurement-manager"))
            throw new UnauthorizedAccessException("The procurement-manager role is required.");
        if (string.Equals(requesterId, currentUser.Id, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("A requester cannot complete their own approval task.");

        await runtime.CompleteAsync(new HumanTaskCompletionRequest
        {
            HumanTaskInstanceId = humanTaskId,
            Outcome = outcome,
            Result = comment
        }, cancellationToken).ConfigureAwait(false);
    }
}
