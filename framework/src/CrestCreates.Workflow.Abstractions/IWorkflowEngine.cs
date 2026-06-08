namespace CrestCreates.Workflow.Abstractions;

public interface IWorkflowEngine
{
    Task<WorkflowInstance> ExecuteAsync(
        string workflowName,
        Dictionary<string, object?>? inputVariables = null,
        CancellationToken ct = default);

    Task<WorkflowInstance> ResumeAsync(
        string instanceId,
        CancellationToken ct = default);
}
