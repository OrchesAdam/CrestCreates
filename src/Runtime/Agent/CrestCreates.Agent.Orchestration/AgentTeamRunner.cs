using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;

namespace CrestCreates.Agent.Orchestration;

public interface IAgentRunWorker
{
    ValueTask<AgentRunResult> RunAsync(AgentRunContext context, CancellationToken cancellationToken);
}

public sealed record AgentTeamMember(string AgentId, ImmutableArray<string> Tools, IAgentRunWorker Worker);

public sealed record AgentTeamStep(Guid RunId, string AgentId, AgentRunResult Result);

public sealed record AgentTeamResult(ImmutableArray<AgentTeamStep> Steps)
{
    public bool AllReturned => !Steps.IsEmpty && Steps.All(step => step.Result.Kind == AgentResultKind.Returned);
}

public sealed class AgentTeamRunner
{
    private readonly AgentRunCoordinator _coordinator;
    private readonly ImmutableDictionary<string, AgentTeamMember> _members;

    public AgentTeamRunner(AgentRunCoordinator coordinator, IEnumerable<AgentTeamMember> members)
    {
        _coordinator = coordinator;
        _members = members.ToImmutableDictionary(member => member.AgentId, StringComparer.Ordinal);
    }

    public async ValueTask<AgentTeamResult> RunSequentialAsync(
        AgentExecutionKey key, AgentRunOwner parent, ImmutableArray<AgentRunAssignment> assignments,
        CancellationToken cancellationToken = default) =>
        await RunSequentialAsync(key, parent, assignments, null, cancellationToken);

    public async ValueTask<AgentTeamResult> RunPlanAsync(
        AgentExecutionKey key, AgentRunOwner parent, long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var plan = await _coordinator.ReadPlanAsync(key, parent, cancellationToken);
        if (plan is null || plan.Version != expectedVersion)
        {
            throw new InvalidOperationException("The active plan has changed.");
        }

        return await RunSequentialAsync(key, parent, plan.Steps, expectedVersion, cancellationToken);
    }

    public async ValueTask<AgentTeamResult> RunParallelAsync(
        AgentExecutionKey key, AgentRunOwner parent, ImmutableArray<AgentRunAssignment> assignments,
        CancellationToken cancellationToken = default)
    {
        ValidateAssignments(assignments);
        var owners = await _coordinator.StartChildrenAsync(key, parent, assignments, cancellationToken);
        var tasks = owners.Select(owner => RunChildAsync(key, parent, owner, cancellationToken).AsTask());
        return new([.. await Task.WhenAll(tasks)]);
    }

    public async ValueTask<AgentTeamResult> DelegateAsync(
        AgentExecutionKey key, AgentRunOwner supervisor, ImmutableArray<AgentRunAssignment> assignments,
        CancellationToken cancellationToken = default)
    {
        if (!assignments.IsDefaultOrEmpty && assignments.Any(item => item.AgentId == supervisor.AgentId))
        {
            throw new InvalidOperationException("A supervisor's delegation requires a different team member.");
        }

        return await RunParallelAsync(key, supervisor, assignments, cancellationToken);
    }

    public async ValueTask<AgentDiscussionSnapshot> RunDiscussionAsync(
        AgentExecutionKey key, AgentRunOwner moderator, Guid discussionId, int maxTurns,
        ImmutableArray<AgentRunAssignment> participants, CancellationToken cancellationToken = default)
    {
        ValidateAssignments(participants);
        var discussion = await _coordinator.OpenDiscussionAsync(key, moderator, discussionId,
            participants, maxTurns, cancellationToken);
        while (!discussion.Closed)
        {
            var round = discussion.Messages.Length;
            var participant = discussion.Assignments[round % discussion.Assignments.Length];
            var assignment = participant with
            {
                RunId = SpeakerRunId(discussionId, round),
                Inputs = [.. participant.Inputs.Concat(discussion.Messages.Select(message => message.Artifact)).Distinct()]
            };
            var owners = await _coordinator.StartChildrenAsync(key, moderator, [assignment], cancellationToken);
            var turn = await RunChildAsync(key, moderator, owners[0], cancellationToken);
            if (turn.Result is not { Kind: AgentResultKind.Returned, Output: not null })
            {
                throw new InvalidOperationException("The discussion speaker did not return a publishable reply.");
            }

            discussion = await _coordinator.PublishDiscussionReplyAsync(key, moderator, discussionId, turn.RunId, cancellationToken);
        }

        return discussion;
    }

    private async ValueTask<AgentTeamResult> RunSequentialAsync(
        AgentExecutionKey key, AgentRunOwner parent, ImmutableArray<AgentRunAssignment> assignments,
        long? planVersion, CancellationToken cancellationToken)
    {
        ValidateAssignments(assignments);
        var outcomes = ImmutableArray.CreateBuilder<AgentTeamStep>(assignments.Length);
        AgentArtifactReference? previousOutput = null;
        foreach (var assignment in assignments)
        {
            var scoped = previousOutput is not null && !assignment.Inputs.Contains(previousOutput)
                ? assignment with { Inputs = assignment.Inputs.Add(previousOutput) }
                : assignment;
            var owners = await _coordinator.StartChildrenAsync(key, parent, [scoped], planVersion, cancellationToken);
            var step = await RunChildAsync(key, parent, owners[0], cancellationToken);
            outcomes.Add(step);
            if (step.Result.Kind != AgentResultKind.Returned)
            {
                break;
            }

            previousOutput = step.Result.Output is null ? null :
                await _coordinator.ShareChildOutputAsync(key, parent, step.RunId, cancellationToken);
        }

        return new(outcomes.ToImmutable());
    }

    private async ValueTask<AgentTeamStep> RunChildAsync(
        AgentExecutionKey key, AgentRunOwner parent, AgentRunOwner owner, CancellationToken cancellationToken)
    {
        var claim = await _coordinator.ClaimTeamChildAsync(key, parent, owner, cancellationToken);
        if (claim.Result is { } prior)
        {
            return new(owner.RunId, owner.AgentId, prior);
        }

        var context = claim.Context!;
        var member = RequireMember(owner.AgentId, context.Tools);
        using var deadline = _coordinator.CreateDeadlineTimer(context.Deadline);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        stop.Token.ThrowIfCancellationRequested();
        var result = await member.Worker.RunAsync(context, stop.Token);
        await _coordinator.FinishTeamChildAsync(key, owner, result, deadline.Token);
        cancellationToken.ThrowIfCancellationRequested();
        return new(owner.RunId, owner.AgentId, result);
    }

    private void ValidateAssignments(ImmutableArray<AgentRunAssignment> assignments)
    {
        if (assignments.IsDefaultOrEmpty)
        {
            throw new ArgumentException("The team requires explicit assignments.", nameof(assignments));
        }

        foreach (var assignment in assignments)
        {
            RequireMember(assignment.AgentId, assignment.Tools);
        }
    }

    private AgentTeamMember RequireMember(string agentId, ImmutableArray<string> tools)
    {
        if (!_members.TryGetValue(agentId, out var member) || tools.IsDefault ||
            tools.Any(tool => !member.Tools.Contains(tool, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException("The agent is not registered for the requested tool scope.");
        }

        return member;
    }

    private static Guid SpeakerRunId(Guid discussionId, int turn)
    {
        Span<byte> material = stackalloc byte[20];
        discussionId.TryWriteBytes(material);
        BinaryPrimitives.WriteInt32LittleEndian(material[16..], turn);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(material, hash);
        return new(hash[..16]);
    }
}
