using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
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
    private readonly IDescriptorStableHashBuilder? _hashBuilder;

    internal WorkflowEngine(
        IWorkflowRegistry registry,
        IWorkflowInstanceStore store,
        IWorkflowExecutionRunner executionRunner,
        IWorkflowLifecycleEventPublisher eventPublisher,
        IAuditOperationContextAccessor contexts,
        WorkflowLifecycleEventFactory events,
        IDescriptorStableHashBuilder? hashBuilder = null)
    {
        _registry = registry;
        _store = store;
        _executionRunner = executionRunner;
        _eventPublisher = eventPublisher;
        _contexts = contexts;
        _events = events;
        _hashBuilder = hashBuilder;
    }

    public async Task<WorkflowInstance> ExecuteAsync(
        string workflowId,
        IReadOnlyDictionary<string, RuntimeStateValue>? inputVariables = null,
        CancellationToken ct = default)
        => await ExecuteCoreAsync(workflowId, null, inputVariables, null, ct).ConfigureAwait(false);

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
            ct).ConfigureAwait(false);
    }

    private async Task<WorkflowInstance> ExecuteCoreAsync(
        string workflowId,
        string? tenantId,
        IReadOnlyDictionary<string, RuntimeStateValue>? inputVariables,
        AuditOrigin? explicitOrigin,
        CancellationToken ct)
    {
        var descriptor = _registry.GetById(workflowId);
        if (descriptor == null)
            throw new InvalidOperationException($"Workflow '{workflowId}' not found.");

        var ambient = _contexts.Current;
        var workflowRunOperationId = _events.CreateRunOperationId();
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
            WorkflowPin = CreatePin(descriptor),
            AuditOrigin = origin
        };

        if (inputVariables != null)
        {
            foreach (var kv in inputVariables)
                instance.Variables[kv.Key] = kv.Value
                    ?? throw new InvalidOperationException($"Workflow input variable '{kv.Key}' is null.");
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
        await _store.AddAsync(instance, ct).ConfigureAwait(false);
        instance.Revision = 1;
        await _eventPublisher.PublishAsync(_events.Create(
            "workflow.started",
            instance,
            descriptor,
            startedIdentity,
            workflowRunOperationId,
            null,
            origin.UpstreamOperationId,
            origin.UpstreamAuditId,
            null), CancellationToken.None).ConfigureAwait(false);

        return await _executionRunner.RunAsync(instance, workflowRunOperationId, enclosingAuditId, ct).ConfigureAwait(false);
    }

    private RuntimeDescriptorPin CreatePin(WorkflowDescriptor descriptor)
    {
        if (_hashBuilder is null)
            return new RuntimeDescriptorPin
            {
                Ref = new DescriptorRef(descriptor.Namespace, descriptor.Id, descriptor.Version),
                ContractHash = PlaceholderHash("Contract", "Workflow"),
                DefinitionHash = PlaceholderHash("Definition", "Workflow")
            };
        var hashes = _hashBuilder.Build(descriptor);
        return new RuntimeDescriptorPin
        {
            Ref = new DescriptorRef(descriptor.Namespace, descriptor.Id, descriptor.Version),
            ContractHash = hashes.ContractHash,
            DefinitionHash = hashes.DefinitionHash
        };
    }

    private static CanonicalHash PlaceholderHash(string purpose, string descriptorKind) => new()
    {
        Value = "unresolved",
        Algorithm = "unresolved",
        AlgorithmVersion = "unresolved",
        ArtifactKind = "Descriptor",
        DescriptorKind = descriptorKind,
        Scope = "InternalFull",
        Purpose = purpose,
        ContractVersion = "unresolved",
        CanonicalShapeVersion = "unresolved"
    };
}
