namespace CrestCreates.Workflow.Abstractions;

public enum StepErrorBehavior
{
    Retry,
    Compensate,
    Fail,
    Skip
}
