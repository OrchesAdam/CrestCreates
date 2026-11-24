using Xunit;

namespace CrestCreates.Agent.Orchestration.Tests;

public sealed class AgentTeamRunnerTests
{
    private static readonly AgentArtifactReference AssetFinding = new("asset-report", "asset-hash");
    private static readonly AgentArtifactReference MesFinding = new("mes-report", "mes-hash");
    private readonly AgentExecutionKey _key = new("tenant", Guid.NewGuid());
    private readonly InMemoryAgentExecutionStore _store = new();

    [Fact]
    public async Task SequentialOnlyPassesExplicitOutputAndStopsBeforeTheNextStepOnFailure()
    {
        var (coordinator, owner) = await StartAsync();
        var discovery = new Worker(context =>
        {
            Assert.Empty(context.Inputs);
            return Returned(AssetFinding);
        });
        var review = new Worker(context =>
        {
            Assert.Equal(AssetFinding, Assert.Single(context.Inputs));
            return new(AgentResultKind.Failed, "InvalidProposal");
        });
        var runner = new AgentTeamRunner(coordinator,
            [new("discovery", ["asset.read"], discovery), new("review", [], review)]);
        var assignments = new[] { Step("discovery"), Step("review"), Step("review") };
        var result = await runner.RunSequentialAsync(_key, owner, [.. assignments]);
        Assert.False(result.AllReturned);
        Assert.Equal(2, result.Steps.Length);
        Assert.DoesNotContain(assignments[2].RunId, (await _store.FindAsync(_key, default))!.Runs);
        var replay = await runner.RunSequentialAsync(_key, owner, [.. assignments]);
        Assert.Equal(result.Steps.ToArray(), replay.Steps.ToArray());
        Assert.Equal(1, discovery.Calls);
        Assert.Equal(1, review.Calls);
    }

    [Fact]
    public async Task PlanRetainsCompletedPrefixAndRunsNewTailOnVersionTwo()
    {
        var (coordinator, owner) = await StartAsync();
        var discovery = new Worker(_ => Returned(AssetFinding));
        var review = new Worker(context =>
        {
            Assert.Equal(AssetFinding, Assert.Single(context.Inputs));
            Assert.Equal("review updated dependencies", context.Task);
            return Returned(MesFinding);
        });
        var runner = new AgentTeamRunner(coordinator,
            [new("discovery", [], discovery), new("review", [], review)]);
        var first = Step("discovery");
        var obsolete = Step("review");
        var plan = await coordinator.SetPlanAsync(_key, owner, 0, [first, obsolete]);
        Assert.True((await runner.RunSequentialAsync(_key, owner, [first])).AllReturned);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.SetPlanAsync(
            _key, owner, plan.Version, [first with { Task = "rewritten" }, obsolete]).AsTask());
        var replacement = Step("review") with { Task = "review updated dependencies" };
        var revised = await coordinator.SetPlanAsync(_key, owner, plan.Version, [first, replacement]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunPlanAsync(_key, owner, plan.Version).AsTask());
        Assert.True((await runner.RunPlanAsync(_key, owner, revised.Version)).AllReturned);
        Assert.Equal(1, discovery.Calls);
        Assert.Equal(1, review.Calls);
        var root = (await _store.FindAsync(_key, default))!.Runs[owner.RunId];
        Assert.Equal([1L, 2L], root.PlanHistory.Select(item => item.Version).ToArray());
    }

    [Fact]
    public async Task ParallelDelegationUsesSeparateScopesAndReturnsResultsInAssignmentOrder()
    {
        var (coordinator, owner) = await StartAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var entered = 0;
        ValueTask<AgentRunResult> Branch(AgentRunContext context, CancellationToken token) => RunBranchAsync(context, token);
        async ValueTask<AgentRunResult> RunBranchAsync(AgentRunContext context, CancellationToken token)
        {
            var expected = context.Owner.AgentId == "asset" ? "asset.read" : "mes.read";
            Assert.Equal(expected, Assert.Single(context.Tools));
            if (Interlocked.Increment(ref entered) == 2)
            {
                release.TrySetResult();
            }

            await release.Task.WaitAsync(token);
            return context.Owner.AgentId == "asset" ? Returned(AssetFinding) : new(AgentResultKind.Failed, "UnknownContract");
        }

        var runner = new AgentTeamRunner(coordinator,
            [new("root", [], new Worker(_ => Returned(AssetFinding))), new("asset", ["asset.read"], new AsyncWorker(Branch)),
                new("mes", ["mes.read"], new AsyncWorker(Branch))]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.DelegateAsync(_key, owner, [Step("root")]).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunParallelAsync(
            _key, owner, [Step("mes") with { Tools = ["asset.read"] }]).AsTask());
        var result = await runner.DelegateAsync(_key, owner,
            [Step("mes") with { Tools = ["mes.read"] }, Step("asset") with { Tools = ["asset.read"] }], cancellation.Token);
        Assert.Equal(["mes", "asset"], result.Steps.Select(item => item.AgentId).ToArray());
        Assert.False(result.AllReturned);
        Assert.Equal(2, entered);
        Assert.Null((await _store.FindAsync(_key, default))!.Runs[owner.RunId].Result);
    }

    [Fact]
    public async Task ConcurrentTeamCallClaimsOneDispatchAndRestartReplaysItsResultAndBudget()
    {
        var (coordinator, owner) = await StartAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var calls = 0;
        var worker = new AsyncWorker(async (context, token) =>
        {
            Interlocked.Increment(ref calls);
            Assert.Equal(new AgentBudget(10, 1), context.RemainingBudget);
            Assert.Equal("asset.read", Assert.Single(context.Tools));
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ReserveAsync(
                _key, context.Owner, Guid.NewGuid(), new(11, 1), token).AsTask());
            var operation = Guid.NewGuid();
            Assert.True(await coordinator.ReserveAsync(_key, context.Owner, operation, new(10, 1), token));
            Assert.True(await coordinator.SettleAsync(_key, context.Owner, operation, new(3, 1), token));
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return Returned(AssetFinding);
        });
        var assignment = Step("asset") with { Tools = ["asset.read"] };
        var members = new[] { new AgentTeamMember("asset", ["asset.read"], worker) };
        var original = new AgentTeamRunner(coordinator, members);
        var restartedCoordinator = new AgentRunCoordinator(_store, TimeProvider.System);
        var restarted = new AgentTeamRunner(restartedCoordinator, members);
        var first = original.RunParallelAsync(_key, owner, [assignment], watchdog.Token).AsTask();
        await entered.Task.WaitAsync(watchdog.Token);
        try
        {
            var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.RunSequentialAsync(
                _key, owner, [assignment], watchdog.Token).AsTask());
            Assert.Contains("requires reconciliation", duplicate.Message);
            Assert.Equal(new AgentBudget(90, 19), (await coordinator.ReadContextAsync(_key, owner)).RemainingBudget);
            var child = Assert.Single(await coordinator.StartChildrenAsync(_key, owner, [assignment]));
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.OfferHandoffAsync(_key, child,
                new(Guid.NewGuid(), "another-worker", [], [])).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.WaitForEventAsync(_key, child,
                new(Guid.NewGuid(), new("mes", "approved", "work-order"))).AsTask());
        }
        finally
        {
            release.TrySetResult();
        }

        var delivered = Assert.Single((await first).Steps);
        var replayed = Assert.Single((await restarted.RunParallelAsync(_key, owner, [assignment], watchdog.Token)).Steps);
        Assert.Equal(delivered, replayed);
        Assert.Equal(1, calls);
        Assert.Equal(new AgentBudget(97, 19), (await coordinator.ReadContextAsync(_key, owner)).RemainingBudget);
    }

    [Fact]
    public async Task ChildReplayCannotAcquireItsHandoffRecipientAuthority()
    {
        var (coordinator, parent) = await StartAsync();
        var assignment = Step("origin") with { Tools = ["asset.read"] };
        var child = Assert.Single(await coordinator.StartChildrenAsync(_key, parent, [assignment]));
        var offer = new AgentHandoffOffer(Guid.NewGuid(), "recipient", [], []);
        await coordinator.OfferHandoffAsync(_key, child, offer);
        var recipient = await coordinator.AcceptHandoffAsync(_key, child.RunId, offer.HandoffId, offer.RecipientAgentId);
        Assert.Equal(child, Assert.Single(await coordinator.StartChildrenAsync(_key, parent, [assignment])));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ReadRunAsync(_key, child).AsTask());
        var recipientView = await coordinator.ReadRunAsync(_key, recipient);
        Assert.Null(recipientView.ChildCall);
        Assert.Null(recipientView.OriginalAssignment);

        var originWorker = new Worker(_ => Returned(AssetFinding));
        var recipientWorker = new Worker(_ => Returned(MesFinding));
        var team = new AgentTeamRunner(coordinator,
            [new("origin", ["asset.read"], originWorker), new("recipient", [], recipientWorker)]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => team.RunParallelAsync(_key, parent, [assignment]).AsTask());
        Assert.Null(Assert.Single(await coordinator.ReadChildResultsAsync(_key, parent, [child.RunId])));
        var result = Returned(MesFinding);
        await coordinator.FinishAsync(_key, recipient, result);
        Assert.Equal(result, Assert.Single((await team.RunParallelAsync(_key, parent, [assignment])).Steps).Result);
        Assert.Equal(0, originWorker.Calls);
        Assert.Equal(0, recipientWorker.Calls);
    }

    [Fact]
    public async Task UnconfirmedWorkerDispatchSurvivesRestartAndCanBeReconciledWithoutRerunning()
    {
        var (coordinator, owner) = await StartAsync();
        var assignment = Step("asset");
        var worker = new Worker(_ => throw new IOException("Worker response lost."));
        var members = new[] { new AgentTeamMember("asset", [], worker) };
        await Assert.ThrowsAsync<IOException>(() => new AgentTeamRunner(coordinator, members)
            .RunSequentialAsync(_key, owner, [assignment]).AsTask());
        var recovered = new AgentRunCoordinator(_store, TimeProvider.System);
        var runner = new AgentTeamRunner(recovered, members);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunSequentialAsync(_key, owner, [assignment]).AsTask());
        var child = Assert.Single(await recovered.StartChildrenAsync(_key, owner, [assignment]));
        var confirmed = Returned(AssetFinding);
        await recovered.FinishAsync(_key, child, confirmed);
        Assert.Equal(confirmed, Assert.Single((await runner.RunSequentialAsync(_key, owner, [assignment])).Steps).Result);
        Assert.Equal(1, worker.Calls);
    }

    [Fact]
    public async Task TeamDeadlineCancelsWorkerAndRejectsItsLateResult()
    {
        var clock = new DeadlineClock();
        var (coordinator, owner) = await StartAsync(clock);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var assignment = Step("asset");
        var worker = new AsyncWorker(async (context, token) =>
        {
            Assert.Equal(clock.GetUtcNow().AddMinutes(2), context.Deadline);
            entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return Returned(AssetFinding);
            }

            throw new InvalidOperationException("Expected the deadline token to be cancelled.");
        });
        var runner = new AgentTeamRunner(coordinator, [new("asset", [], worker)]);
        var pending = runner.RunParallelAsync(_key, owner, [assignment], watchdog.Token).AsTask();
        await entered.Task.WaitAsync(watchdog.Token);
        clock.Expire();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(watchdog.Token));
        Assert.Null(Assert.Single(await coordinator.ReadChildResultsAsync(_key, owner, [assignment.RunId])));
    }

    [Fact]
    public async Task GroupDiscussionOnlySharesPublishedMessagesAndStopsAtItsTurnLimit()
    {
        var (coordinator, owner) = await StartAsync();
        var unrelated = new Worker(_ => Returned(new("private-notes", "private-hash")));
        var maintenance = new Worker(context =>
        {
            Assert.Empty(context.Inputs);
            return Returned(AssetFinding);
        });
        var quality = new Worker(context =>
        {
            Assert.Equal(AssetFinding, Assert.Single(context.Inputs));
            return Returned(MesFinding);
        });
        var runner = new AgentTeamRunner(coordinator,
            [new("private", [], unrelated), new("maintenance", [], maintenance), new("quality", [], quality)]);
        await runner.RunSequentialAsync(_key, owner, [Step("private")]);
        var discussionId = Guid.NewGuid();
        var participants = new[] { Step("maintenance"), Step("quality") };
        var result = await runner.RunDiscussionAsync(_key, owner, discussionId, 2, [.. participants]);
        Assert.True(result.Closed);
        Assert.Equal([AssetFinding, MesFinding], result.Messages.Select(item => item.Artifact).ToArray());
        Assert.Equal(result, await runner.RunDiscussionAsync(_key, owner, discussionId, 2, [.. participants]));
        Assert.Equal(1, maintenance.Calls);
        Assert.Equal(1, quality.Calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunDiscussionAsync(
            _key, owner, discussionId, 3, [.. participants]).AsTask());
    }

    [Fact]
    public async Task AgentWorkflowAndCapabilityReportSeparateOutcomes()
    {
        var (coordinator, owner) = await StartAsync();
        await coordinator.FinishAsync(_key, owner, Returned(AssetFinding));
        var workflow = new AgentWorkflowFact("wf-instance", "maintenance", 2, "descriptor-hash", AgentWorkflowOutcome.Completed);
        var uncertain = new AgentCapabilityFact("cap-execution", "mes.release", AgentCapabilityCommit.Unknown, null);
        Assert.True(await coordinator.RecordWorkflowFactAsync(_key, workflow));
        Assert.False(await coordinator.RecordWorkflowFactAsync(_key, workflow));
        Assert.True(await coordinator.RecordCapabilityFactAsync(_key, uncertain));
        await Assert.ThrowsAsync<ArgumentException>(() => Task.Run(async () =>
            await coordinator.RecordCapabilityFactAsync(_key, uncertain with { Commit = AgentCapabilityCommit.Committed })));
        Assert.True(await coordinator.RecordCapabilityFactAsync(_key, uncertain with { Commit = AgentCapabilityCommit.Rejected }));
        var state = (await _store.FindAsync(_key, default))!;
        Assert.Equal(AgentResultKind.Returned, state.Runs[owner.RunId].Result?.Kind);
        Assert.Equal(AgentWorkflowOutcome.Completed, state.Workflows[workflow.InstanceId].Outcome);
        Assert.Equal(AgentCapabilityCommit.Rejected, state.Capabilities[uncertain.ExecutionId].Commit);
    }

    private async Task<(AgentRunCoordinator, AgentRunOwner)> StartAsync(TimeProvider? clock = null)
    {
        clock ??= TimeProvider.System;
        var coordinator = new AgentRunCoordinator(_store, clock);
        var owner = await coordinator.StartAsync(_key, "engineer", clock.GetUtcNow().AddMinutes(2),
            new(_key.RootRunId, "root", "Review change", new(100, 20), ["asset.read", "mes.read"], []));
        return (coordinator, owner);
    }

    private static AgentRunAssignment Step(string agentId) => new(Guid.NewGuid(), agentId, "Check proposal", new(10, 1), [], []);

    private static AgentRunResult Returned(AgentArtifactReference artifact) => new(AgentResultKind.Returned, "Report", artifact);

    private sealed class DeadlineClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        private Action? _expire;

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _expire = () =>
            {
                _now += dueTime;
                callback(state);
            };
            return System.CreateTimer(callback, state, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public void Expire() => _expire!();
    }

    private sealed class Worker(Func<AgentRunContext, AgentRunResult> run) : IAgentRunWorker
    {
        public int Calls { get; private set; }

        public ValueTask<AgentRunResult> RunAsync(AgentRunContext context, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(run(context));
        }
    }

    private sealed class AsyncWorker(Func<AgentRunContext, CancellationToken, ValueTask<AgentRunResult>> run) : IAgentRunWorker
    {
        public ValueTask<AgentRunResult> RunAsync(AgentRunContext context, CancellationToken cancellationToken) =>
            run(context, cancellationToken);
    }
}
