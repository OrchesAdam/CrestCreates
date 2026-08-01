using CrestCreates.EventBus.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Metadata.Abstractions.Persistence;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;

namespace CrestCreates.HumanTask;

public sealed class DefaultHumanTaskRuntime : IHumanTaskRuntime
{
    private readonly IHumanTaskRegistry _registry;
    private readonly IHumanTaskInstanceStore _store;
    private readonly ILocalEventBus _eventBus;
    private readonly IHumanTaskAssigneeResolver _resolver;
    private readonly IHumanTaskCompletionFailurePolicy _completionFailurePolicy;
    private readonly IRuntimeDescriptorPinResolver<HumanTaskDescriptor> _pinResolver;
    private readonly IRuntimeStateContractRegistry _stateRegistry;
    private readonly IDescriptorSnapshotStore? _snapshots;

    public DefaultHumanTaskRuntime(
        IHumanTaskRegistry registry,
        IHumanTaskInstanceStore store,
        ILocalEventBus eventBus,
        IHumanTaskAssigneeResolver resolver,
        IRuntimeDescriptorPinResolver<HumanTaskDescriptor> pinResolver,
        IRuntimeStateContractRegistry stateRegistry,
        IHumanTaskCompletionFailurePolicy? completionFailurePolicy = null,
        IDescriptorSnapshotStore? snapshots = null)
    {
        _registry = registry;
        _store = store;
        _eventBus = eventBus;
        _resolver = resolver;
        _completionFailurePolicy = completionFailurePolicy
            ?? RejectingHumanTaskCompletionFailurePolicy.Instance;
        _pinResolver = pinResolver ?? throw new ArgumentNullException(nameof(pinResolver));
        _stateRegistry = stateRegistry ?? throw new ArgumentNullException(nameof(stateRegistry));
        _snapshots = snapshots;
    }

    public async Task<HumanTaskInstance> PrepareAsync(
        HumanTaskCreationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        HumanTaskDescriptor? descriptor = request.Version.HasValue
            ? _registry.GetByVersion(request.HumanTaskId, request.Version.Value)
            : _registry.GetById(request.HumanTaskId);
        if (descriptor == null)
            throw new InvalidOperationException($"HumanTask descriptor '{request.HumanTaskId}' not found.");

        if (request.Input is not null)
            _stateRegistry.Validate(request.Input);
        var resolved = _pinResolver.Capture(descriptor);
        var resolution = await _resolver.ResolveAsync(descriptor, request, ct).ConfigureAwait(false);
        var instance = new HumanTaskInstance
        {
            Key = new RuntimeInstanceKey(
                request.TenantId,
                request.InstanceId ?? Guid.NewGuid().ToString("N")),
            HumanTaskPin = resolved.Pin,
            Status = (!string.IsNullOrWhiteSpace(resolution.AssigneeUserId)
                || !string.IsNullOrWhiteSpace(resolution.AssigneeRoleId))
                ? HumanTaskInstanceStatus.Assigned
                : HumanTaskInstanceStatus.Created,
            AssigneeUserId = resolution.AssigneeUserId,
            AssigneeRoleId = resolution.AssigneeRoleId,
            CandidateUserIds = resolution.CandidateUserIds.ToArray(),
            CandidateRoleIds = resolution.CandidateRoleIds.ToArray(),
            OrganizationUnitId = resolution.OrganizationUnitId,
            PositionId = resolution.PositionId,
            AssigneeResolutionReason = resolution.AssigneeResolutionReason,
            WorkflowKey = request.WorkflowKey,
            WorkflowStepId = request.WorkflowStepId,
            Input = request.Input,
            CreatedAt = DateTimeOffset.UtcNow
        };

        return instance;
    }

    public async Task<HumanTaskInstance> CreateAsync(
        HumanTaskCreationRequest request, CancellationToken ct = default)
    {
        var instance = await PrepareAsync(request, ct).ConfigureAwait(false);
        await _store.AddAsync(instance, ct).ConfigureAwait(false);
        instance.Revision = 1;
        return instance;
    }

    public async Task<HumanTaskInstance> CompleteAsync(
        HumanTaskCompletionRequest request, CancellationToken ct = default)
    {
        request.HumanTaskKey.EnsureValid();
        var loaded = await _store.GetAsync(request.HumanTaskKey, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"HumanTask instance '{request.HumanTaskKey.InstanceId}' not found.");
        var isRecovery = loaded.Status == HumanTaskInstanceStatus.CompletionDispatchFailed;
        if (!isRecovery
            && loaded.Status != HumanTaskInstanceStatus.Created
            && loaded.Status != HumanTaskInstanceStatus.Assigned)
            throw new InvalidOperationException($"HumanTask instance '{loaded.Id}' is in status '{loaded.Status}' and cannot be completed.");

        await RuntimeDescriptorPinEvidence.ValidateAsync(loaded.HumanTaskPin, _snapshots, ct).ConfigureAwait(false);
        var descriptor = _pinResolver.Resolve(loaded.HumanTaskPin).Descriptor;
        if (request.Result is not null)
            _stateRegistry.Validate(request.Result);
        CompletionOutcomeMatcher.Resolve(descriptor, request.Outcome);

        if (isRecovery)
        {
            if (!string.Equals(loaded.Outcome, request.Outcome, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"HumanTask instance '{loaded.Id}' cannot recover with a different outcome.");
            var recovery = loaded.Snapshot();
            recovery.CompletionEventId ??= Guid.NewGuid().ToString("N");
            var persistedCompletion = CreateCompletedEvent(recovery, recovery.Outcome!, recovery.Output);
            await _completionFailurePolicy.RecoverAsync(recovery, persistedCompletion, CancellationToken.None).ConfigureAwait(false);
            recovery.Status = HumanTaskInstanceStatus.Completed;
            recovery.CompletionDispatchError = null;
            recovery.CompletionDispatchFailedAt = null;
            await PersistUpdateAsync(recovery, loaded.Revision, ct).ConfigureAwait(false);
            return recovery;
        }

        var candidate = loaded.Snapshot();
        candidate.Status = HumanTaskInstanceStatus.Completed;
        candidate.Outcome = request.Outcome;
        candidate.Output = request.Result;
        candidate.CompletedAt = DateTimeOffset.UtcNow;
        candidate.CompletionEventId ??= Guid.NewGuid().ToString("N");
        await PersistUpdateAsync(candidate, loaded.Revision, ct).ConfigureAwait(false);

        try
        {
            await _eventBus.PublishAsync(CreateCompletedEvent(candidate, request.Outcome, request.Result), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception dispatchException)
        {
            await RecordDispatchFailureAsync(candidate, dispatchException).ConfigureAwait(false);
            throw;
        }
        return candidate;
    }

    public async Task<HumanTaskInstance> CancelAsync(
        RuntimeInstanceKey humanTaskKey, string reason, CancellationToken ct = default)
    {
        humanTaskKey.EnsureValid();
        var loaded = await _store.GetAsync(humanTaskKey, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"HumanTask instance '{humanTaskKey.InstanceId}' not found.");
        if (loaded.Status == HumanTaskInstanceStatus.Completed
            || loaded.Status == HumanTaskInstanceStatus.Cancelled
            || loaded.Status == HumanTaskInstanceStatus.CompletionDispatchFailed)
            throw new InvalidOperationException($"HumanTask instance '{loaded.Id}' is already '{loaded.Status}' and cannot be cancelled.");

        var candidate = loaded.Snapshot();
        candidate.Status = HumanTaskInstanceStatus.Cancelled;
        candidate.CancellationReason = reason;
        candidate.CancelledAt = DateTimeOffset.UtcNow;
        await PersistUpdateAsync(candidate, loaded.Revision, ct).ConfigureAwait(false);
        return candidate;
    }

    private async Task PersistUpdateAsync(HumanTaskInstance candidate, long expectedRevision, CancellationToken ct)
    {
        await _store.UpdateAsync(candidate, expectedRevision, ct).ConfigureAwait(false);
        candidate.Revision = expectedRevision + 1;
    }

    private async Task RecordDispatchFailureAsync(HumanTaskInstance instance, Exception exception)
    {
        var candidate = instance.Snapshot();
        candidate.Status = HumanTaskInstanceStatus.CompletionDispatchFailed;
        candidate.CompletionDispatchError = $"{exception.GetType().Name}: {exception.Message}";
        candidate.CompletionDispatchFailedAt = DateTimeOffset.UtcNow;
        candidate.CompletionDispatchAttemptCount++;
        try
        {
            await PersistUpdateAsync(candidate, instance.Revision, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception stateException)
        {
            throw new AggregateException(
                $"HumanTask '{instance.Id}' completion dispatch failed and its explicit failure state could not be persisted.",
                exception,
                stateException);
        }
    }

    private static HumanTaskCompletedEvent CreateCompletedEvent(
        HumanTaskInstance instance,
        string outcome,
        RuntimeStateValue? result)
        => new()
        {
            EventId = instance.CompletionEventId ?? string.Empty,
            HumanTaskKey = instance.Key,
            WorkflowKey = instance.WorkflowKey,
            HumanTaskPin = instance.HumanTaskPin,
            Outcome = outcome,
            Result = result
        };

    private sealed class RejectingHumanTaskCompletionFailurePolicy : IHumanTaskCompletionFailurePolicy
    {
        public static RejectingHumanTaskCompletionFailurePolicy Instance { get; } = new();

        public Task RecoverAsync(HumanTaskInstance instance, HumanTaskCompletedEvent completion, CancellationToken cancellationToken = default)
            => Task.FromException(new HumanTaskCompletionRecoveryRequiredException(instance.Id));
    }
}
