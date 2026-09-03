using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Metadata.Abstractions.Runtime;

namespace CrestCreates.Workflow.Abstractions;

/// <summary>
/// Durable evidence that one abort operation committed all of its terminal
/// state changes. The operation id is the caller's replay discriminator.
/// </summary>
public sealed record WorkflowAbortReceipt
{
    public required RuntimeTenantScope Scope { get; init; }
    public required string AbortOperationId { get; init; }
    public required CanonicalHash Integrity { get; init; }
    public required RuntimeInstanceKey WorkflowKey { get; init; }
    public required RuntimeInstanceKey HumanTaskKey { get; init; }
    public required long WorkflowFromRevision { get; init; }
    public required long WorkflowToRevision { get; init; }
    public required RuntimeDescriptorPin WorkflowPin { get; init; }
    public required RuntimeDescriptorPin HumanTaskPin { get; init; }
    public required string Reason { get; init; }
    public DateTimeOffset AcceptedAt { get; init; } = DateTimeOffset.UtcNow;
}

public enum WorkflowAbortReceiptWriteStatus { Accepted, Duplicate, Conflict }

public sealed record WorkflowAbortReceiptWriteResult
{
    public required WorkflowAbortReceiptWriteStatus Status { get; init; }
    public required WorkflowAbortReceipt Receipt { get; init; }
}

public interface IWorkflowAbortReceiptStore
{
    Task<WorkflowAbortReceiptWriteResult> AddAsync(WorkflowAbortReceipt receipt, CancellationToken cancellationToken = default);
    Task<WorkflowAbortReceipt?> GetAsync(RuntimeTenantScope scope, string abortOperationId, CancellationToken cancellationToken = default);
}
