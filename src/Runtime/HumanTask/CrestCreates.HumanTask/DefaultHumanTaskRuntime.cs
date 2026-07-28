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
    private readonly IHumanTaskCompletionFailurePolicy _completionFailurePolicy;

    public DefaultHumanTaskRuntime(
        IHumanTaskRegistry registry,
        IHumanTaskInstanceStore store,
        ILocalEventBus eventBus,
        IHumanTaskAssigneeResolver resolver,
        IHumanTaskCompletionFailurePolicy? completionFailurePolicy = null)
    {
        _registry = registry;
        _store = store;
        _eventBus = eventBus;
        _resolver = resolver;
        _completionFailurePolicy = completionFailurePolicy
            ?? RejectingHumanTaskCompletionFailurePolicy.Instance;
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

        var isRecovery = instance.Status == HumanTaskInstanceStatus.CompletionDispatchFailed;
        if (!isRecovery
            && instance.Status != HumanTaskInstanceStatus.Created
            && instance.Status != HumanTaskInstanceStatus.Assigned)
            throw new InvalidOperationException(
                $"HumanTask instance '{instance.Id}' is in status '{instance.Status}' " +
                "and cannot be completed.");

        var descriptor = _registry.GetByVersion(instance.HumanTaskId, instance.HumanTaskVersion);
        if (descriptor == null)
            throw new InvalidOperationException(
                $"HumanTask descriptor '{instance.HumanTaskId}' v{instance.HumanTaskVersion} " +
                "not found.");

        CompletionOutcomeMatcher.Resolve(descriptor, request.Outcome);

        if (isRecovery)
        {
            if (!string.Equals(instance.Outcome, request.Outcome, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"HumanTask instance '{instance.Id}' failed while dispatching outcome " +
                    $"'{instance.Outcome}' and cannot recover as '{request.Outcome}'.");
            }

            var persistedCompletion = CreateCompletedEvent(
                instance,
                instance.Outcome!,
                instance.Output);
            try
            {
                await _completionFailurePolicy.RecoverAsync(
                    instance,
                    persistedCompletion,
                    ct).ConfigureAwait(false);
            }
            catch (Exception recoveryException)
            {
                await RecordDispatchFailureAsync(instance, recoveryException).ConfigureAwait(false);
                throw;
            }

            instance.Status = HumanTaskInstanceStatus.Completed;
            instance.CompletionDispatchError = null;
            instance.CompletionDispatchFailedAt = null;
            await _store.SaveAsync(instance, ct).ConfigureAwait(false);
            return instance;
        }

        instance.Status = HumanTaskInstanceStatus.Completed;
        instance.Outcome = request.Outcome;
        instance.Output = request.Result;
        instance.CompletedAt = DateTimeOffset.UtcNow;

        // Phase 5b: SaveAsync may throw RuntimeConcurrencyException.
        // If it does, DO NOT publish — let exception propagate.
        await _store.SaveAsync(instance, ct).ConfigureAwait(false);

        try
        {
            await _eventBus.PublishAsync(
                CreateCompletedEvent(instance, request.Outcome, request.Result),
                ct).ConfigureAwait(false);
        }
        catch (Exception dispatchException)
        {
            await RecordDispatchFailureAsync(instance, dispatchException).ConfigureAwait(false);
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

        if (instance.Status == HumanTaskInstanceStatus.Completed
            || instance.Status == HumanTaskInstanceStatus.Cancelled
            || instance.Status == HumanTaskInstanceStatus.CompletionDispatchFailed)
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

    private static HumanTaskCompletedEvent CreateCompletedEvent(
        HumanTaskInstance instance,
        string outcome,
        object? result)
        => new()
        {
            HumanTaskId = instance.HumanTaskId,
            HumanTaskInstanceId = instance.Id,
            HumanTaskVersion = instance.HumanTaskVersion,
            Outcome = outcome,
            Result = result
        };

    private async Task RecordDispatchFailureAsync(
        HumanTaskInstance instance,
        Exception exception)
    {
        instance.Status = HumanTaskInstanceStatus.CompletionDispatchFailed;
        instance.CompletionDispatchError = $"{exception.GetType().Name}: {exception.Message}";
        instance.CompletionDispatchFailedAt = DateTimeOffset.UtcNow;
        instance.CompletionDispatchAttemptCount++;

        try
        {
            await _store.SaveAsync(instance, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception stateException)
        {
            throw new AggregateException(
                $"HumanTask '{instance.Id}' completion dispatch failed and its explicit " +
                "failure state could not be persisted.",
                exception,
                stateException);
        }
    }

    private sealed class RejectingHumanTaskCompletionFailurePolicy
        : IHumanTaskCompletionFailurePolicy
    {
        public static RejectingHumanTaskCompletionFailurePolicy Instance { get; } = new();

        public Task RecoverAsync(
            HumanTaskInstance instance,
            HumanTaskCompletedEvent completion,
            CancellationToken cancellationToken = default)
            => Task.FromException(
                new HumanTaskCompletionRecoveryRequiredException(instance.Id));
    }
}
