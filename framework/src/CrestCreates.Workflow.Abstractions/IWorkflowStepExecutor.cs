namespace CrestCreates.Workflow.Abstractions;

/// <summary>
/// Executes a single workflow step. The executor:
/// - MUST return StepExecutionResult(Failed) for known business failures.
/// - MUST throw only for infrastructure/programming errors.
/// - MUST NOT access persistence or modify WorkflowInstance state.
/// </summary>
public interface IWorkflowStepExecutor
{
    Task<StepExecutionResult> ExecuteAsync(
        WorkflowExecutionContext context,
        CancellationToken ct);
}
