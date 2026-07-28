namespace CrestCreates.Workflow.Abstractions;

public interface IWorkflowEngine
{
    /// <summary>
    /// TODO: Phase 5 — migrate to VersionedDescriptorRef&lt;WorkflowDescriptor&gt;
    /// for unambiguous version targeting.
    /// </summary>
    Task<WorkflowInstance> ExecuteAsync(
        string workflowId,
        Dictionary<string, object?>? inputVariables = null,
        CancellationToken ct = default);

    Task<WorkflowInstance> ExecuteAsync(
        WorkflowExecutionRequest request,
        CancellationToken ct = default)
        => ExecuteAsync(request.WorkflowId, request.InputVariables, ct);
}
