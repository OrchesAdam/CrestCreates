using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class WorkflowEngine : IWorkflowEngine
{
    private readonly IWorkflowRegistry _registry;
    private readonly IWorkflowInstanceStore _store;
    private readonly IWorkflowExecutionRunner _executionRunner;
    private readonly IWorkflowLifecycleEventPublisher _eventPublisher;

    internal WorkflowEngine(
        IWorkflowRegistry registry,
        IWorkflowInstanceStore store,
        IWorkflowExecutionRunner executionRunner,
        IWorkflowLifecycleEventPublisher eventPublisher)
    {
        _registry = registry;
        _store = store;
        _executionRunner = executionRunner;
        _eventPublisher = eventPublisher;
    }

    public async Task<WorkflowInstance> ExecuteAsync(
        string workflowId,
        Dictionary<string, object?>? inputVariables = null,
        CancellationToken ct = default)
        => await ExecuteCoreAsync(workflowId, null, inputVariables, ct).ConfigureAwait(false);

    public async Task<WorkflowInstance> ExecuteAsync(
        WorkflowExecutionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ExecuteCoreAsync(
            request.WorkflowId,
            request.TenantId,
            request.InputVariables,
            ct).ConfigureAwait(false);
    }

    private async Task<WorkflowInstance> ExecuteCoreAsync(
        string workflowId,
        string? tenantId,
        Dictionary<string, object?>? inputVariables,
        CancellationToken ct)
    {
        var descriptor = _registry.GetById(workflowId);
        if (descriptor == null)
            throw new InvalidOperationException($"Workflow '{workflowId}' not found.");

        var instance = new WorkflowInstance
        {
            Workflow = new VersionedDescriptorRef<WorkflowDescriptor>(descriptor.Id, descriptor.Version),
            TenantId = tenantId
        };

        if (inputVariables != null)
        {
            foreach (var kv in inputVariables)
                instance.Variables[kv.Key] = kv.Value;
        }

        await _store.SaveAsync(instance, ct).ConfigureAwait(false);

        await _eventPublisher.PublishAsync(new WorkflowLifecycleEvent
        {
            EventType = "workflow.started",
            WorkflowInstanceId = instance.InstanceId,
            WorkflowId = descriptor.Id,
            Status = WorkflowInstanceStatus.Running
        }, ct).ConfigureAwait(false);

        return await _executionRunner.RunAsync(instance, ct).ConfigureAwait(false);
    }
}
