using System.Collections.Immutable;

namespace CrestCreates.Agent.Orchestration;

public sealed partial class AgentRunCoordinator
{
    public ValueTask<AgentPlanSnapshot> SetPlanAsync(
        AgentExecutionKey key, AgentRunOwner owner, long expectedVersion,
        ImmutableArray<AgentRunAssignment> steps, CancellationToken cancellationToken = default) =>
        ChangeAsync(key, execution =>
        {
            var run = RequireRunnable(execution, owner);
            var previous = CurrentPlan(run);
            if ((previous?.Version ?? 0) != expectedVersion || steps.IsDefaultOrEmpty ||
                steps.Select(step => step.RunId).Distinct().Count() != steps.Length)
            {
                throw new InvalidOperationException("A plan requires a current version and distinct steps.");
            }

            foreach (var step in steps)
            {
                ValidateAssignment(step);
                RequireNarrowerScope(run, step.Tools, step.Inputs);
            }

            if (previous is not null)
            {
                var reservedPrefix = 0;
                for (var index = 0; index < previous.Steps.Length; index++)
                {
                    if (execution.Runs.ContainsKey(previous.Steps[index].RunId))
                    {
                        reservedPrefix = index + 1;
                    }
                }

                if (steps.Length < reservedPrefix || Enumerable.Range(0, reservedPrefix)
                    .Any(index => !SameAssignment(steps[index], previous.Steps[index])))
                {
                    throw new InvalidOperationException("An authorized plan cannot rewrite a started step or its prefix.");
                }
            }

            var plan = new AgentPlanSnapshot(checked(expectedVersion + 1), owner.Epoch, steps);
            return (execution.Runs.SetItem(owner.RunId, run with { PlanHistory = run.PlanHistory.Add(plan) }), plan);
        }, cancellationToken);

    private static AgentPlanSnapshot? CurrentPlan(AgentRunSnapshot run) =>
        run.Plan is { } plan && plan.OwnerEpoch == run.Owner.Epoch ? plan : null;

    internal async ValueTask<AgentPlanSnapshot?> ReadPlanAsync(
        AgentExecutionKey key, AgentRunOwner owner, CancellationToken cancellationToken) =>
        CurrentPlan(await ReadRunStateAsync(key, owner, cancellationToken));

    internal ValueTask<(AgentRunContext? Context, AgentRunResult? Result)> ClaimTeamChildAsync(
        AgentExecutionKey key, AgentRunOwner parent, AgentRunOwner assignedOwner, CancellationToken cancellationToken) =>
        ChangeAsync<(AgentRunContext?, AgentRunResult?)>(key, execution =>
        {
            RequireRunnable(execution, parent);
            var child = RequireChild(execution, parent, assignedOwner.RunId);
            if (child.Result is { } result)
            {
                return (execution.Runs, (null, result));
            }

            RequireRunnable(execution, assignedOwner);
            RequireUndispatched(child);
            var runs = execution.Runs.SetItem(assignedOwner.RunId, child with { TeamDispatchClaimed = true });
            return (runs, (CreateContext(execution, child), null));
        }, cancellationToken);

    internal CancellationTokenSource CreateDeadlineTimer(DateTimeOffset deadline)
    {
        var remaining = deadline - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            throw new TimeoutException("The agent run deadline elapsed.");
        }

        return new CancellationTokenSource(remaining, clock);
    }

    public ValueTask<AgentArtifactReference> ShareChildOutputAsync(
        AgentExecutionKey key, AgentRunOwner owner, Guid childRunId, CancellationToken cancellationToken = default) =>
        ChangeAsync(key, execution =>
        {
            var run = RequireRunnable(execution, owner);
            var output = RequireChildOutput(execution, owner, childRunId);
            if (run.Inputs.Contains(output))
            {
                return (execution.Runs, output);
            }

            return (execution.Runs.SetItem(owner.RunId, run with { Inputs = run.Inputs.Add(output) }), output);
        }, cancellationToken);

    public ValueTask<AgentDiscussionSnapshot> OpenDiscussionAsync(
        AgentExecutionKey key, AgentRunOwner moderator, Guid discussionId, ImmutableArray<string> participants,
        int maxTurns, CancellationToken cancellationToken = default) =>
        ChangeAsync(key, execution =>
        {
            var run = RequireRunnable(execution, moderator);
            RequireId(discussionId);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTurns);
            if (participants.IsDefaultOrEmpty || participants.Any(string.IsNullOrWhiteSpace) ||
                participants.Distinct(StringComparer.Ordinal).Count() != participants.Length)
            {
                throw new ArgumentException("A discussion requires distinct registered participants.", nameof(participants));
            }

            if (run.Discussion is { } previous && previous.OwnerEpoch == moderator.Epoch)
            {
                if (previous.DiscussionId != discussionId || previous.MaxTurns != maxTurns ||
                    !previous.Participants.SequenceEqual(participants, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException("The run already has a different discussion.");
                }

                return (execution.Runs, previous);
            }

            var discussion = new AgentDiscussionSnapshot(discussionId, moderator.Epoch, maxTurns, participants, []);
            return (execution.Runs.SetItem(moderator.RunId, run with { Discussion = discussion }), discussion);
        }, cancellationToken);

    public ValueTask<AgentDiscussionSnapshot> PublishDiscussionReplyAsync(
        AgentExecutionKey key, AgentRunOwner moderator, Guid discussionId, Guid speakerRunId,
        CancellationToken cancellationToken = default) =>
        ChangeAsync(key, execution =>
        {
            var run = RequireRunnable(execution, moderator);
            if (run.Discussion is not { } discussion || discussion.DiscussionId != discussionId ||
                discussion.OwnerEpoch != moderator.Epoch)
            {
                throw new InvalidOperationException("The moderator does not hold this discussion.");
            }

            if (discussion.Messages.Any(message => message.SpeakerRunId == speakerRunId))
            {
                return (execution.Runs, discussion);
            }

            var child = RequireRun(execution, speakerRunId);
            var expected = discussion.Participants[discussion.Messages.Length % discussion.Participants.Length];
            if (discussion.Closed || child.OriginalAssignment?.AgentId != expected || child.Owner.AgentId != expected)
            {
                throw new InvalidOperationException("The speaker is out of turn or the discussion is closed.");
            }

            var output = RequireChildOutput(execution, moderator, speakerRunId);
            var replies = discussion.Messages.Add(new(speakerRunId, expected, output));
            var updated = discussion with { Messages = replies, Closed = replies.Length == discussion.MaxTurns };
            return (execution.Runs.SetItem(moderator.RunId, run with
            {
                Discussion = updated,
                Inputs = run.Inputs.Contains(output) ? run.Inputs : run.Inputs.Add(output)
            }), updated);
        }, cancellationToken);

    public ValueTask<bool> RecordWorkflowFactAsync(
        AgentExecutionKey key, AgentWorkflowFact fact, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fact.InstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fact.DescriptorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fact.DescriptorHash);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fact.Version);
        if (!Enum.IsDefined(fact.Outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(fact));
        }

        return ChangeExecutionAsync(key, execution =>
        {
            if (execution.Workflows.TryGetValue(fact.InstanceId, out var previous))
            {
                return SameFact(execution, previous, fact);
            }

            return (execution with { Workflows = execution.Workflows.Add(fact.InstanceId, fact) }, true);
        }, cancellationToken);
    }

    public ValueTask<bool> RecordCapabilityFactAsync(
        AgentExecutionKey key, AgentCapabilityFact fact, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fact.ExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fact.CapabilityId);
        if (!Enum.IsDefined(fact.Commit) ||
            (fact.Commit == AgentCapabilityCommit.Committed && string.IsNullOrWhiteSpace(fact.BusinessReceiptId)))
        {
            throw new ArgumentException("A confirmed commitment requires a business receipt.", nameof(fact));
        }

        return ChangeExecutionAsync(key, execution =>
        {
            if (execution.Capabilities.TryGetValue(fact.ExecutionId, out var previous))
            {
                if (previous.Commit != AgentCapabilityCommit.Unknown || fact.Commit == AgentCapabilityCommit.Unknown ||
                    previous.CapabilityId != fact.CapabilityId)
                {
                    return SameFact(execution, previous, fact);
                }
            }

            return (execution with { Capabilities = execution.Capabilities.SetItem(fact.ExecutionId, fact) }, true);
        }, cancellationToken);
    }

    private static (AgentExecutionSnapshot Snapshot, bool Result) SameFact<T>(AgentExecutionSnapshot execution, T stored, T received)
    {
        if (!EqualityComparer<T>.Default.Equals(stored, received))
        {
            throw new InvalidOperationException("The observed execution identity is already bound to a different fact.");
        }

        return (execution, false);
    }

    private static AgentArtifactReference RequireChildOutput(AgentExecutionSnapshot execution, AgentRunOwner owner, Guid childRunId)
    {
        var child = RequireChild(execution, owner, childRunId);
        if (child.Result is not { Kind: AgentResultKind.Returned, Output: { } artifact })
        {
            throw new InvalidOperationException("The child has no shareable output in this ownership scope.");
        }

        return artifact;
    }
}
