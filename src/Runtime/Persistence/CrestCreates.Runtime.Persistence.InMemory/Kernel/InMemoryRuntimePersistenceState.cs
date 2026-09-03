using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Persistence;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Workflow.Abstractions;
using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;

namespace CrestCreates.Runtime.Persistence.InMemory.Kernel;

internal sealed class InMemoryRuntimePersistenceState
{
    public Dictionary<RuntimeInstanceKey, WorkflowInstance> Workflows { get; } = new();
    public Dictionary<RuntimeInstanceKey, HumanTaskInstance> HumanTasks { get; } = new();
    public Dictionary<string, (DescriptorSnapshot Snapshot, string Fingerprint)> Snapshots { get; } = new(StringComparer.Ordinal);
    public Dictionary<(RuntimeTenantScope Scope, string Operation), WorkflowSuspensionReceipt> Receipts { get; } = new();
    public Dictionary<(RuntimeTenantScope Scope, string Operation), WorkflowAbortReceipt> AbortReceipts { get; } = new();
    public Dictionary<string, InMemoryOutboxRecord> Outbox { get; } = new(StringComparer.Ordinal);
    public Dictionary<(RuntimeTenantScope Scope, string CompletionEventId), WorkflowContinuationAcceptance> ContinuationAcceptances { get; } = new();

    public InMemoryRuntimePersistenceState Clone()
    {
        var clone = new InMemoryRuntimePersistenceState();
        foreach (var (key, value) in Workflows)
            clone.Workflows[key] = value.Snapshot();
        foreach (var (key, value) in HumanTasks)
            clone.HumanTasks[key] = value.Snapshot();
        foreach (var (key, value) in Snapshots)
            clone.Snapshots[key] = (value.Snapshot.Snapshot(), value.Fingerprint);
        foreach (var (key, value) in Receipts)
            clone.Receipts[key] = value;
        foreach (var (key, value) in AbortReceipts)
            clone.AbortReceipts[key] = value;
        foreach (var (key, value) in Outbox)
            clone.Outbox[key] = value.Clone();
        foreach (var (key, value) in ContinuationAcceptances)
            clone.ContinuationAcceptances[key] = value;
        return clone;
    }
}

internal sealed class InMemoryOutboxRecord
{
    public required OutboxMessage Message { get; init; }
    public OutboxDeliveryStatus Status { get; set; } = OutboxDeliveryStatus.Pending;
    public int Attempt { get; set; }
    public long Fence { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastFailureCode { get; set; }
    public string? TerminalLeaseOwner { get; set; }
    public long? TerminalFence { get; set; }
    public string? TerminalFailureCode { get; set; }
    public InMemoryOutboxRecord Clone() => new()
    {
        Message = Message.Snapshot(), Status = Status, Attempt = Attempt, Fence = Fence,
        LeaseOwner = LeaseOwner, LeaseExpiresAt = LeaseExpiresAt, NextAttemptAt = NextAttemptAt,
        LastFailureCode = LastFailureCode, TerminalLeaseOwner = TerminalLeaseOwner,
        TerminalFence = TerminalFence, TerminalFailureCode = TerminalFailureCode
    };
}
