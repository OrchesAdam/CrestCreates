using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

internal interface IWorkflowExecutionRunner
{
    Task<WorkflowInstance> RunAsync(WorkflowInstance instance, CancellationToken ct);
}
