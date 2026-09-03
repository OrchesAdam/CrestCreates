namespace CrestCreates.Workflow.Abstractions;

public enum WorkflowAbortResultStatus { Accepted, Duplicate }

public sealed record WorkflowAbortResult
{
    public required WorkflowAbortResultStatus Status { get; init; }
    public required string AbortOperationId { get; init; }
}

public static class WorkflowLifecycleReasonCodes
{
    public const string Aborted = "WORKFLOW_ABORTED";
}
