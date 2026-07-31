using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Metadata.Abstractions;
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

    internal WorkflowEngine(
        IWorkflowRegistry registry,
        IWorkflowInstanceStore store,
        IWorkflowExecutionRunner executionRunner,
        IWorkflowLifecycleEventPublisher eventPublisher,
        IAuditOperationContextAccessor contexts,
        WorkflowLifecycleEventFactory events)
    {
        _registry = registry;
        _store = store;
        _executionRunner = executionRunner;
        _eventPublisher = eventPublisher;
        _contexts = contexts;
        _events = events;
    }

    public async Task<WorkflowInstance> ExecuteAsync(
        string workflowId,
        Dictionary<string, object?>? inputVariables = null,
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
        Dictionary<string, object?>? inputVariables,
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
            Workflow = new VersionedDescriptorRef<WorkflowDescriptor>(descriptor.Id, descriptor.Version),
            TenantId = tenantId ?? ambient?.TenantId,
            AuditOrigin = origin
        };

        if (inputVariables != null)
        {
            foreach (var kv in inputVariables)
                instance.Variables[kv.Key] = kv.Value;
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
        await _store.SaveAsync(instance, ct).ConfigureAwait(false);
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
}
