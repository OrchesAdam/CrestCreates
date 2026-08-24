using CrestCreates.EventBus.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Metadata.Abstractions.Persistence;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Delivery.Abstractions.Registration;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using CrestCreates.Runtime.Delivery.Abstractions.Composition;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using System.Text.Json;

namespace CrestCreates.HumanTask;

public sealed class DefaultHumanTaskRuntime : IHumanTaskRuntime
{
    private readonly IHumanTaskRegistry _registry;
    private readonly IHumanTaskInstanceStore _store;
    private readonly IHumanTaskAssigneeResolver _resolver;
    private readonly IRuntimeDescriptorPinResolver<HumanTaskDescriptor> _pinResolver;
    private readonly IRuntimeStateContractRegistry _stateRegistry;
    private readonly IDescriptorSnapshotStore? _snapshots;
    private readonly IRuntimeTransactionCoordinator? _transactions;
    private readonly IOutboxMessageFactory? _messageFactory;
    private readonly ITransactionalOutboxWriter? _outbox;
    private readonly IReadOnlyList<OutboxRequiredConsumerMetadata> _consumerMetadata;
    private readonly IReadOnlyList<HumanTaskCompletionObligationPolicyRegistration> _obligationPolicies;

    public DefaultHumanTaskRuntime(
        IHumanTaskRegistry registry,
        IHumanTaskInstanceStore store,
        ILocalEventBus eventBus,
        IHumanTaskAssigneeResolver resolver,
        IRuntimeDescriptorPinResolver<HumanTaskDescriptor> pinResolver,
        IRuntimeStateContractRegistry stateRegistry,
        IRuntimeTransactionCoordinator? transactions = null,
        IOutboxMessageFactory? messageFactory = null,
        ITransactionalOutboxWriter? outbox = null,
        IEnumerable<OutboxRequiredConsumerMetadata>? consumerMetadata = null,
        IEnumerable<HumanTaskCompletionObligationPolicyRegistration>? obligationPolicies = null,
        IHumanTaskCompletionFailurePolicy? completionFailurePolicy = null,
        IDescriptorSnapshotStore? snapshots = null)
    {
        _registry = registry;
        _store = store;
        _ = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _resolver = resolver;
        _pinResolver = pinResolver ?? throw new ArgumentNullException(nameof(pinResolver));
        _stateRegistry = stateRegistry ?? throw new ArgumentNullException(nameof(stateRegistry));
        _snapshots = snapshots;
        _transactions = transactions;
        _messageFactory = messageFactory;
        _outbox = outbox;
        _consumerMetadata = (consumerMetadata ?? Array.Empty<OutboxRequiredConsumerMetadata>()).ToArray();
        _obligationPolicies = (obligationPolicies ?? Array.Empty<HumanTaskCompletionObligationPolicyRegistration>()).ToArray();
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
        var requiredConsumers = ResolveRequiredConsumers(request, descriptor);
        ValidateRequiredConsumers(requiredConsumers);
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
        instance.RequiredCompletionConsumerIds = requiredConsumers;

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
        if (loaded.Status != HumanTaskInstanceStatus.Created
            && loaded.Status != HumanTaskInstanceStatus.Assigned)
            throw new InvalidOperationException($"HumanTask instance '{loaded.Id}' is in status '{loaded.Status}' and cannot be completed.");

        await RuntimeDescriptorPinEvidence.ValidateAsync(loaded.HumanTaskPin, _snapshots, ct).ConfigureAwait(false);
        var descriptor = _pinResolver.Resolve(loaded.HumanTaskPin).Descriptor;
        if (request.Result is not null)
            _stateRegistry.Validate(request.Result);
        CompletionOutcomeMatcher.Resolve(descriptor, request.Outcome);

        var candidate = loaded.Snapshot();
        candidate.Status = HumanTaskInstanceStatus.Completed;
        candidate.Outcome = CompletionOutcomeMatcher.Resolve(descriptor, request.Outcome).Condition.ToString();
        candidate.Output = request.Result;
        candidate.CompletedAt = DateTimeOffset.UtcNow;
        candidate.CompletionEventId ??= Guid.NewGuid().ToString("N");
        var completedEvent = CreateCompletedEvent(candidate, candidate.Outcome, request.Result);
        if (_transactions is null || _messageFactory is null || _outbox is null)
            throw new OutboxCompositionException("HumanTask completion requires the transactional outbox runtime composition.");
        var message = _messageFactory.Create(
            new OutboxMessageMetadata
            {
                MessageId = candidate.CompletionEventId,
                TenantId = candidate.TenantId,
                ContractId = HumanTaskDeliveryConstants.CompletedContractId,
                PayloadTypeId = HumanTaskDeliveryConstants.CompletedPayloadTypeId,
                EventName = "humantask.completed",
                EventVersion = 1,
                CorrelationId = candidate.WorkflowKey?.InstanceId,
                CausationId = candidate.CompletionEventId,
                OccurredAt = candidate.CompletedAt ?? DateTimeOffset.UtcNow,
                RequiredConsumerIds = candidate.RequiredCompletionConsumerIds,
                CreatedAt = candidate.CompletedAt ?? DateTimeOffset.UtcNow
            },
            completedEvent,
            HumanTaskJsonSerializerContext.Default.HumanTaskCompletedEvent);
        await _transactions.ExecuteAsync(async transactionCt =>
        {
            await PersistUpdateAsync(candidate, loaded.Revision, transactionCt).ConfigureAwait(false);
            await _outbox.AppendAsync(message, transactionCt).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        // Completion is acknowledged by the durable outbox only.  In-process
        // LocalEvent publication is deliberately not part of this request path:
        // an unbounded or non-cooperative compatibility handler must never delay
        // the caller after the Runtime transaction has committed.
        return candidate;
    }

    private string[] ResolveRequiredConsumers(HumanTaskCreationRequest request, HumanTaskDescriptor descriptor)
    {
        var values = request.RequiredCompletionConsumerIds
            .Concat(_obligationPolicies.Where(policy => string.Equals(policy.HumanTaskDescriptorId, descriptor.Id, StringComparison.Ordinal)
                && policy.HumanTaskDescriptorVersion == descriptor.Version).Select(policy => policy.RequiredConsumerId));
        if (request.WorkflowKey is not null) values = values.Append(HumanTaskDeliveryConstants.WorkflowContinuationConsumerId);
        return values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private void ValidateRequiredConsumers(IReadOnlyList<string> requiredConsumers)
    {
        var active = _consumerMetadata.Select(item => item.ConsumerId).ToHashSet(StringComparer.Ordinal);
        var missing = requiredConsumers.Where(id => !active.Contains(id)).ToArray();
        if (missing.Length != 0)
            throw new InvalidOperationException($"HumanTask completion obligation(s) are not registered: {string.Join(", ", missing)}.");
    }

    public async Task<HumanTaskInstance> CancelAsync(
        RuntimeInstanceKey humanTaskKey, string reason, CancellationToken ct = default)
    {
        humanTaskKey.EnsureValid();
        var loaded = await _store.GetAsync(humanTaskKey, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"HumanTask instance '{humanTaskKey.InstanceId}' not found.");
        if (loaded.Status == HumanTaskInstanceStatus.Completed
            || loaded.Status == HumanTaskInstanceStatus.Cancelled)
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

}
