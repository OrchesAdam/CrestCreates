using CrestCreates.EventBus.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.HumanTask;

public sealed class DefaultHumanTaskRuntime : IHumanTaskRuntime
{
    private readonly IHumanTaskRegistry _registry;
    private readonly IHumanTaskInstanceStore _store;
    private readonly ILocalEventBus _eventBus;

    public DefaultHumanTaskRuntime(
        IHumanTaskRegistry registry,
        IHumanTaskInstanceStore store,
        ILocalEventBus eventBus)
    {
        _registry = registry;
        _store = store;
        _eventBus = eventBus;
    }

    public async Task<HumanTaskInstance> CreateAsync(
        HumanTaskCreationRequest request, CancellationToken ct = default)
    {
        HumanTaskDescriptor? descriptor;
        if (request.Version.HasValue)
            descriptor = _registry.GetByVersion(request.HumanTaskId, request.Version.Value);
        else
            descriptor = _registry.GetById(request.HumanTaskId);

        if (descriptor == null)
            throw new InvalidOperationException(
                $"HumanTask descriptor '{request.HumanTaskId}' not found.");

        var instance = new HumanTaskInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            HumanTaskId = descriptor.Id,
            HumanTaskVersion = descriptor.Version,
            Status = (request.AssigneeUserId != null || request.AssigneeRoleId != null)
                ? HumanTaskInstanceStatus.Assigned
                : HumanTaskInstanceStatus.Created,
            TenantId = request.TenantId,
            AssigneeUserId = request.AssigneeUserId,
            AssigneeRoleId = request.AssigneeRoleId,
            WorkflowInstanceId = request.WorkflowInstanceId,
            WorkflowStepId = request.WorkflowStepId,
            Input = request.Input,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _store.SaveAsync(instance, ct).ConfigureAwait(false);
        return instance;
    }

    public async Task<HumanTaskInstance> CompleteAsync(
        HumanTaskCompletionRequest request, CancellationToken ct = default)
    {
        var instance = await _store.GetByIdAsync(request.HumanTaskInstanceId, ct)
            .ConfigureAwait(false);

        if (instance == null)
            throw new RuntimeEntityNotFoundException(
                $"HumanTask instance '{request.HumanTaskInstanceId}' not found.");

        if (instance.Status != HumanTaskInstanceStatus.Created &&
            instance.Status != HumanTaskInstanceStatus.Assigned)
            throw new InvalidOperationException(
                $"HumanTask instance '{instance.Id}' is in status '{instance.Status}' " +
                "and cannot be completed.");

        var descriptor = _registry.GetByVersion(instance.HumanTaskId, instance.HumanTaskVersion);
        if (descriptor == null)
            throw new InvalidOperationException(
                $"HumanTask descriptor '{instance.HumanTaskId}' v{instance.HumanTaskVersion} " +
                "not found.");

        CompletionOutcomeMatcher.Resolve(descriptor, request.Outcome);

        instance.Status = HumanTaskInstanceStatus.Completed;
        instance.Outcome = request.Outcome;
        instance.Output = request.Result;
        instance.CompletedAt = DateTimeOffset.UtcNow;

        // Phase 5b: SaveAsync may throw RuntimeConcurrencyException.
        // If it does, DO NOT publish — let exception propagate.
        await _store.SaveAsync(instance, ct).ConfigureAwait(false);

        await _eventBus.PublishAsync(new HumanTaskCompletedEvent
        {
            HumanTaskId = instance.HumanTaskId,
            HumanTaskInstanceId = instance.Id,
            HumanTaskVersion = instance.HumanTaskVersion,
            Outcome = request.Outcome,
            Result = request.Result
        }, ct).ConfigureAwait(false);

        return instance;
    }

    public async Task<HumanTaskInstance> CancelAsync(
        string instanceId, string reason, CancellationToken ct = default)
    {
        var instance = await _store.GetByIdAsync(instanceId, ct).ConfigureAwait(false);

        if (instance == null)
            throw new RuntimeEntityNotFoundException(
                $"HumanTask instance '{instanceId}' not found.");

        if (instance.Status == HumanTaskInstanceStatus.Completed ||
            instance.Status == HumanTaskInstanceStatus.Cancelled)
            throw new InvalidOperationException(
                $"HumanTask instance '{instanceId}' is already '{instance.Status}' " +
                "and cannot be cancelled.");

        instance.Status = HumanTaskInstanceStatus.Cancelled;
        instance.CancellationReason = reason;
        instance.CancelledAt = DateTimeOffset.UtcNow;

        // Phase 5b: SaveAsync may throw RuntimeConcurrencyException — let it propagate
        await _store.SaveAsync(instance, ct).ConfigureAwait(false);
        return instance;
    }
}
