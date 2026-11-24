using System;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Agent.Orchestration;

public readonly record struct AgentExecutionKey(string? TenantId, Guid RootRunId);

public readonly record struct AgentRunOwner(Guid RunId, string AgentId, long Epoch);

public readonly record struct AgentBudget(long ModelTokens, int ToolCalls)
{
    internal bool IsNonNegative => ModelTokens >= 0 && ToolCalls >= 0;

    internal bool Covers(AgentBudget amount) =>
        ModelTokens >= amount.ModelTokens && ToolCalls >= amount.ToolCalls;

    internal AgentBudget Reserve(AgentBudget amount)
    {
        if (!amount.IsNonNegative || !Covers(amount))
        {
            throw new InvalidOperationException("The requested agent budget is unavailable.");
        }

        return new(ModelTokens - amount.ModelTokens, ToolCalls - amount.ToolCalls);
    }

    internal AgentBudget Refund(AgentBudget amount) =>
        new(checked(ModelTokens + amount.ModelTokens), checked(ToolCalls + amount.ToolCalls));
}

public sealed record AgentBudgetReservation(AgentBudget Reserved, AgentBudget? Used = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long OwnerEpoch { get; init; }
    public AgentModelDecision? ModelDecision { get; init; }
    public AgentToolObservation? ToolObservation { get; init; }
}

public sealed record AgentToolSelection(string Name, JsonElement? Arguments);

public sealed record AgentModelDecision(AgentToolSelection? Tool, AgentRunResult? Result);

public sealed record AgentToolObservation(string Code, bool Succeeded, JsonElement? StructuredOutput);

public sealed record AgentArtifactReference(string Id, string ContentHash);

public sealed record AgentRunAssignment(
    Guid RunId,
    string AgentId,
    string Task,
    AgentBudget Budget,
    ImmutableArray<string> Tools,
    ImmutableArray<AgentArtifactReference> Inputs);

public sealed record AgentRunContext(
    AgentExecutionKey Execution,
    AgentRunOwner Owner,
    string ActorId,
    string Task,
    DateTimeOffset Deadline,
    AgentBudget RemainingBudget,
    ImmutableArray<string> Tools,
    ImmutableArray<AgentArtifactReference> Inputs,
    AgentEventDelivery? ResumedBy);

public enum AgentResultKind
{
    Returned = 1,
    Failed,
    Cancelled
}

public sealed record AgentRunResult(AgentResultKind Kind, string Code, AgentArtifactReference? Output = null);

public sealed record AgentChildCall(Guid ParentRunId, long ParentEpoch, Guid ChildRunId);

public sealed record AgentHandoffOffer(
    Guid HandoffId,
    string RecipientAgentId,
    ImmutableArray<string> Tools,
    ImmutableArray<AgentArtifactReference> Inputs);

public sealed record AgentHandoffRecord(
    AgentHandoffOffer Offer, AgentRunOwner PreviousOwner, AgentRunOwner? AcceptedOwner);

public sealed record AgentEventSelector(string Source, string Kind, string SubjectId);

public sealed record AgentEventWait(Guid WaitId, AgentEventSelector Selector);

public sealed record AgentEventDelivery(
    string EventId,
    string? TenantId,
    AgentEventSelector Selector,
    AgentArtifactReference Payload);

public sealed record AgentEventResumption(AgentEventWait Wait, AgentEventDelivery Delivery, long OwnerEpoch);

public sealed record AgentRunSnapshot(
    AgentRunOwner Owner,
    string Task,
    AgentBudget RemainingBudget,
    ImmutableArray<string> Tools,
    ImmutableArray<AgentArtifactReference> Inputs,
    AgentChildCall? ChildCall,
    ImmutableArray<AgentHandoffRecord> Handoffs,
    AgentEventWait? Wait,
    ImmutableArray<AgentEventResumption> Resumptions,
    ImmutableDictionary<Guid, AgentBudgetReservation> Reservations,
    AgentRunResult? Result)
{
    public AgentRunAssignment? OriginalAssignment { get; init; }
    public ImmutableArray<AgentPlanSnapshot> PlanHistory { get; init; } = [];
    public AgentDiscussionSnapshot? Discussion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool TeamDispatchClaimed { get; init; }

    [JsonIgnore]
    public AgentPlanSnapshot? Plan => PlanHistory.IsEmpty ? null : PlanHistory[^1];

    [JsonIgnore]
    public AgentHandoffRecord? PendingHandoff =>
        Result is null && !Handoffs.IsEmpty && Handoffs[^1].AcceptedOwner is null ? Handoffs[^1] : null;
}

public sealed record AgentPlanSnapshot(
    long Version, long OwnerEpoch, ImmutableArray<AgentRunAssignment> Steps);

public sealed record AgentDiscussionSnapshot(
    Guid DiscussionId, long OwnerEpoch, int MaxTurns,
    ImmutableArray<string> Participants, ImmutableArray<AgentDiscussionMessage> Messages, bool Closed = false);

public sealed record AgentDiscussionMessage(Guid SpeakerRunId, string AgentId, AgentArtifactReference Artifact);

public enum AgentWorkflowOutcome { Completed = 1, Failed, Aborted }

public sealed record AgentWorkflowFact(
    string InstanceId, string DescriptorId, int Version, string DescriptorHash, AgentWorkflowOutcome Outcome);

public enum AgentCapabilityCommit { Committed = 1, Rejected, Unknown }

public sealed record AgentCapabilityFact(
    string ExecutionId, string CapabilityId, AgentCapabilityCommit Commit, string? BusinessReceiptId);

public sealed record AgentExecutionSnapshot(
    AgentExecutionKey Key,
    long Revision,
    string ActorId,
    DateTimeOffset Deadline,
    ImmutableDictionary<Guid, AgentRunSnapshot> Runs)
{
    public ImmutableDictionary<string, AgentWorkflowFact> Workflows { get; init; } = ImmutableDictionary<string, AgentWorkflowFact>.Empty;
    public ImmutableDictionary<string, AgentCapabilityFact> Capabilities { get; init; } = ImmutableDictionary<string, AgentCapabilityFact>.Empty;
}

public interface IAgentExecutionStore
{
    ValueTask<AgentExecutionSnapshot?> FindAsync(AgentExecutionKey key, CancellationToken cancellationToken);

    ValueTask<bool> TryCreateAsync(AgentExecutionSnapshot execution, CancellationToken cancellationToken);

    ValueTask<bool> TryReplaceAsync(
        AgentExecutionSnapshot execution, long expectedRevision, CancellationToken cancellationToken);
}
