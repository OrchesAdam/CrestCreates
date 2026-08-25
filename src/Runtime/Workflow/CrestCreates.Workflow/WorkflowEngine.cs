using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using CrestCreates.Workflow.Accountability;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class WorkflowEngine : IWorkflowEngine
{
    private readonly IWorkflowRegistry _registry;
    private readonly IWorkflowInstanceStore _store;
    private readonly IWorkflowExecutionRunner _executionRunner;
    private readonly IWorkflowLifecycleEventPublisher _eventPublisher;
    private readonly IAuditOperationContextAccessor _contexts;
    private readonly WorkflowLifecycleEventFactory _events;
    private readonly IRuntimeDescriptorPinResolver<WorkflowDescriptor> _pinResolver;
    private readonly IRuntimeStateContractRegistry _stateRegistry;
    private readonly IRuntimeTransactionCoordinator? _transactions;
    private readonly WorkflowAccountabilityOutboxAppender _accountabilityOutbox;

    internal WorkflowEngine(
        IWorkflowRegistry registry,
        IWorkflowInstanceStore store,
        IWorkflowExecutionRunner executionRunner,
        IWorkflowLifecycleEventPublisher eventPublisher,
        IAuditOperationContextAccessor contexts,
        WorkflowLifecycleEventFactory events,
        IRuntimeDescriptorPinResolver<WorkflowDescriptor> pinResolver,
        IRuntimeStateContractRegistry stateRegistry,
        IRuntimeTransactionCoordinator? transactions,
        WorkflowAccountabilityOutboxAppender accountabilityOutbox)
    {
        _registry = registry;
        _store = store;
        _executionRunner = executionRunner;
        _eventPublisher = eventPublisher;
        _contexts = contexts;
        _events = events;
        _pinResolver = pinResolver ?? throw new ArgumentNullException(nameof(pinResolver));
        _stateRegistry = stateRegistry ?? throw new ArgumentNullException(nameof(stateRegistry));
        _transactions = transactions;
        _accountabilityOutbox = accountabilityOutbox ?? throw new ArgumentNullException(nameof(accountabilityOutbox));
    }

    public async Task<WorkflowInstance> ExecuteAsync(
        string workflowId,
        IReadOnlyDictionary<string, RuntimeStateValue>? inputVariables = null,
        CancellationToken ct = default)
        => await ExecuteCoreAsync(workflowId, null, inputVariables, null, null, ct).ConfigureAwait(false);

    public async Task<WorkflowInstance> ExecuteAsync(
        WorkflowExecutionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ExecuteCoreAsync(
            request.WorkflowId,
            request.TenantId,
            request.InputVariables,
            request.Origin,
            request.OperationId,
            ct).ConfigureAwait(false);
    }

    private async Task<WorkflowInstance> ExecuteCoreAsync(
        string workflowId,
        string? tenantId,
        IReadOnlyDictionary<string, RuntimeStateValue>? inputVariables,
        AuditOrigin? explicitOrigin,
        string? requestedOperationId,
        CancellationToken ct)
    {
        var descriptor = _registry.GetById(workflowId);
        if (descriptor == null)
            throw new InvalidOperationException($"Workflow '{workflowId}' not found.");

        var ambient = _contexts.Current;
        if (requestedOperationId is not null && string.IsNullOrWhiteSpace(requestedOperationId))
            throw new ArgumentException("Workflow operation identity cannot be blank when supplied.", nameof(requestedOperationId));
        var workflowRunOperationId = requestedOperationId ?? _events.CreateRunOperationId();
        var origin = explicitOrigin ?? (ambient is null ? new AuditOrigin
        {
            CorrelationId = _events.CreateRunOperationId(),
            InitiatingActor = new AuditActor { Kind = "system", Id = "system" },
            InvocationSource = "system"
        } : new AuditOrigin
        {
            CorrelationId = ambient.CorrelationId,
            UpstreamOperationId = ambient.OperationId,
            UpstreamAuditId = ambient.EnclosingAuditId,
            InitiatingActor = ambient.Actor,
            InvocationSource = ambient.InvocationSource
        });
        var instance = new WorkflowInstance
        {
            Key = new RuntimeInstanceKey(tenantId ?? ambient?.TenantId, Guid.NewGuid().ToString("N")),
            WorkflowPin = _pinResolver.Capture(descriptor).Pin,
            AuditOrigin = origin
        };

        if (inputVariables != null)
        {
            foreach (var kv in inputVariables)
            {
                if (kv.Value is null)
                    throw new InvalidOperationException($"Workflow input variable '{kv.Key}' is null.");
                _stateRegistry.Validate(kv.Value);
                instance.Variables[kv.Key] = kv.Value;
            }
        }

        var enclosingAuditId = ambient?.EnclosingAuditId;
        using var scope = _contexts.Push(new AuditOperationContext
        {
            CorrelationId = origin.CorrelationId,
            OperationId = workflowRunOperationId,
            EnclosingAuditId = enclosingAuditId,
            Actor = new AuditActor
            {
                Kind = "workflow",
                Id = instance.InstanceId,
                InitiatedBy = new AuditActorReference(origin.InitiatingActor.Kind, origin.InitiatingActor.Id)
            },
            TenantId = instance.TenantId,
            InvocationSource = "workflow"
        });

        var startedIdentity = _events.AllocateLifecycleIdentity();
        instance.LastLifecycleAuditId = startedIdentity.AuditId;
        var startedEvent = _events.Create(
            "workflow.started",
            instance,
            descriptor,
            startedIdentity,
            workflowRunOperationId,
            null,
            origin.UpstreamOperationId,
            origin.UpstreamAuditId,
            null);
        if (_accountabilityOutbox.IsEnabled && _transactions is null)
            throw new InvalidOperationException("Reliable Workflow Accountability requires the Runtime transaction coordinator.");
        var accountabilityMessage = await _accountabilityOutbox.PrepareAsync(startedEvent, ct).ConfigureAwait(false);
        if (_transactions is null || !_accountabilityOutbox.IsEnabled)
        {
            await _store.AddAsync(instance, ct).ConfigureAwait(false);
            instance.Revision = 1;
        }
        else
        {
            await _transactions.ExecuteAsync(async transactionCt =>
            {
                await _store.AddAsync(instance, transactionCt).ConfigureAwait(false);
                instance.Revision = 1;
                await _accountabilityOutbox.AppendAsync(accountabilityMessage!, transactionCt).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }
        await _eventPublisher.PublishAsync(startedEvent, CancellationToken.None).ConfigureAwait(false);

        return await _executionRunner.RunAsync(instance, workflowRunOperationId, enclosingAuditId, ct).ConfigureAwait(false);
    }

}
