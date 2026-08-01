using CrestCreates.Runtime.Persistence.Abstractions.State;

namespace CrestCreates.Workflow.Abstractions;

public interface IWorkflowEngine
{
    Task<WorkflowInstance> ExecuteAsync(
        string workflowId,
        IReadOnlyDictionary<string, RuntimeStateValue>? inputVariables = null,
        CancellationToken ct = default);

    Task<WorkflowInstance> ExecuteAsync(
        WorkflowExecutionRequest request,
        CancellationToken ct = default)
        => ExecuteAsync(request.WorkflowId, request.InputVariables, ct);
}
