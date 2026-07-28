using CrestCreates.EventBus.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.HumanTask;

public sealed class DefaultHumanTaskRuntime : IHumanTaskRuntime
{
    private readonly IHumanTaskRegistry _registry;
    private readonly IHumanTaskInstanceStore _store;
    private readonly ILocalEventBus _eventBus;
    private readonly IHumanTaskAssigneeResolver _resolver;

    public DefaultHumanTaskRuntime(
        IHumanTaskRegistry registry,
        IHumanTaskInstanceStore store,
        ILocalEventBus eventBus,
        IHumanTaskAssigneeResolver resolver)
    {
        _registry = registry;
        _store = store;
        _eventBus = eventBus;
        _resolver = resolver;
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

        var resolution = await _resolver.ResolveAsync(descriptor, request, ct)
            .ConfigureAwait(false);

        var instance = new HumanTaskInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            HumanTaskId = descriptor.Id,
            HumanTaskVersion = descriptor.Version,
            Status = (!string.IsNullOrWhiteSpace(resolution.AssigneeUserId)
                   || !string.IsNullOrWhiteSpace(resolution.AssigneeRoleId))
                ? HumanTaskInstanceStatus.Assigned
                : HumanTaskInstanceStatus.Created,
            TenantId = request.TenantId,
            AssigneeUserId = resolution.AssigneeUserId,
            AssigneeRoleId = resolution.AssigneeRoleId,
            CandidateUserIds = resolution.CandidateUserIds.ToArray(),
            CandidateRoleIds = resolution.CandidateRoleIds.ToArray(),
            OrganizationUnitId = resolution.OrganizationUnitId,
            PositionId = resolution.PositionId,
            AssigneeResolutionReason = resolution.AssigneeResolutionReason,
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

        var previousStatus = instance.Status;
        var previousOutcome = instance.Outcome;
        var previousOutput = instance.Output;
        var previousCompletedAt = instance.CompletedAt;

        instance.Status = HumanTaskInstanceStatus.Completed;
        instance.Outcome = request.Outcome;
        instance.Output = request.Result;
        instance.CompletedAt = DateTimeOffset.UtcNow;

        // Phase 5b: SaveAsync may throw RuntimeConcurrencyException.
        // If it does, DO NOT publish — let exception propagate.
        await _store.SaveAsync(instance, ct).ConfigureAwait(false);

        try
        {
            await _eventBus.PublishAsync(new HumanTaskCompletedEvent
            {
                HumanTaskId = instance.HumanTaskId,
                HumanTaskInstanceId = instance.Id,
                HumanTaskVersion = instance.HumanTaskVersion,
                Outcome = request.Outcome,
                Result = request.Result
            }, ct).ConfigureAwait(false);
        }
        catch (Exception dispatchException)
        {
            instance.Status = previousStatus;
            instance.Outcome = previousOutcome;
            instance.Output = previousOutput;
            instance.CompletedAt = previousCompletedAt;

            try
            {
                await _store.SaveAsync(instance, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    $"HumanTask '{instance.Id}' completion dispatch and rollback both failed.",
                    dispatchException,
                    rollbackException);
            }

            throw;
        }

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
