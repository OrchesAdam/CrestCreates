namespace CrestCreates.Workflow.Abstractions;

public enum WorkflowSuspensionReceiptWriteStatus { Accepted, Duplicate, Conflict }

public sealed record WorkflowSuspensionReceiptWriteResult
{
    public required WorkflowSuspensionReceiptWriteStatus Status { get; init; }
    public required WorkflowSuspensionReceipt Receipt { get; init; }
}
