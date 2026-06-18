namespace CrestCreates.Workflow.Abstractions;

/// <summary>
/// Resolves the executor for the given target.
/// Throws UnsupportedWorkflowTargetException if no executor is registered.
/// WorkflowCompatibilityValidator must guarantee this never fails at runtime.
/// Registry is precomputed at startup — immutable.
/// </summary>
public interface IWorkflowStepExecutorRegistry
{
    IWorkflowStepExecutor Resolve(InteractionTarget target);
}
