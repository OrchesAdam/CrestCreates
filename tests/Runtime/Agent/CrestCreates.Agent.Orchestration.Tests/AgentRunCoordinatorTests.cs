using Xunit;

namespace CrestCreates.Agent.Orchestration.Tests;

public sealed class AgentRunCoordinatorTests
{
    private readonly InMemoryAgentExecutionStore _store = new();
    private readonly AgentExecutionKey _key = new("tenant-a", Guid.NewGuid());
    private readonly AgentArtifactReference _asset = new("asset-metadata", "hash-a");
    private readonly AgentArtifactReference _mes = new("mes-metadata", "hash-m");

    [Fact]
    public async Task ChildrenReserveBudgetAtomicallyAndKeepTheirInputsAndResultsSeparate()
    {
        var coordinator = NewCoordinator();
        var root = await StartAsync(coordinator);
        var assetChild = new AgentRunAssignment(Guid.NewGuid(), "asset", "Check Asset", new(30, 2), ["asset"], [_asset]);
        var mesChild = new AgentRunAssignment(Guid.NewGuid(), "mes", "Check MES", new(40, 2), ["mes"], [_mes]);
        var excessive = mesChild with { Budget = new(71, 2) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.StartChildrenAsync(_key, root, [assetChild, excessive]).AsTask());
        Assert.Equal(new AgentBudget(100, 5), (await coordinator.ReadContextAsync(_key, root)).RemainingBudget);

        var owners = await coordinator.StartChildrenAsync(_key, root, [assetChild, mesChild]);
        var assetContext = await coordinator.ReadContextAsync(_key, owners[0]);
        Assert.Equal(_asset, Assert.Single(assetContext.Inputs));
        Assert.Equal("asset", Assert.Single(assetContext.Tools));
        Assert.Equal(_mes, Assert.Single((await coordinator.ReadContextAsync(_key, owners[1])).Inputs));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ReadChildResultsAsync(_key, owners[0], [mesChild.RunId]).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.StartChildrenAsync(
            _key, owners[0], [mesChild with { RunId = Guid.NewGuid(), Budget = new(1, 0) }]).AsTask());

        var operation = Guid.NewGuid();
        Assert.True(await coordinator.ReserveAsync(_key, owners[1], operation, new(25, 2)));
        Assert.False(await coordinator.ReserveAsync(_key, owners[1], operation, new(25, 2)));
        Assert.True(await coordinator.SettleAsync(_key, owners[1], operation, new(20, 1)));
        Assert.False(await coordinator.SettleAsync(_key, owners[1], operation, new(20, 1)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.SettleAsync(_key, owners[1], operation, new(10, 1)).AsTask());
        var report = new AgentRunResult(AgentResultKind.Returned, "ReportReady", new("mes-report", "hash-r"));
        await coordinator.FinishAsync(_key, owners[1], report);
        await coordinator.FinishAsync(_key, owners[1], report);

        var ordered = await coordinator.ReadChildResultsAsync(_key, root, [assetChild.RunId, mesChild.RunId]);
        Assert.Null(ordered[0]);
        Assert.Equal(report, ordered[1]);
        Assert.Equal(new AgentBudget(50, 2), (await coordinator.ReadContextAsync(_key, root)).RemainingBudget);
        var snapshot = Assert.IsType<AgentExecutionSnapshot>(await _store.FindAsync(_key, default));
        Assert.Null(snapshot.Runs[root.RunId].Result);
        Assert.Equal(new AgentChildCall(root.RunId, root.Epoch, mesChild.RunId), snapshot.Runs[mesChild.RunId].ChildCall);
    }

    [Fact]
    public async Task HandoffScopesReadsAndArtifactAccessWhileKeepingDurableReceipts()
    {
        var coordinator = NewCoordinator();
        var root = await StartAsync(coordinator);
        var assignment = new AgentRunAssignment(Guid.NewGuid(), "asset", "Private plan step", new(10, 1), ["asset"], [_asset]);
        var oldPlan = await coordinator.SetPlanAsync(_key, root, 0, [assignment]);
        var discussionId = Guid.NewGuid();
        await coordinator.OpenDiscussionAsync(_key, root, discussionId, ["asset"], 1);
        var child = Assert.Single(await coordinator.StartChildrenAsync(_key, root, [assignment]));
        var report = new AgentArtifactReference("private-report", "report-hash");
        await coordinator.FinishAsync(_key, child, new(AgentResultKind.Returned, "ReportReady", report));
        await coordinator.PublishDiscussionReplyAsync(_key, root, discussionId, child.RunId);
        var operation = Guid.NewGuid();
        await coordinator.ReserveAsync(_key, root, operation, new(12, 1));
        await coordinator.SettleAsync(_key, root, operation, new(7, 1));
        var wait = new AgentEventWait(Guid.NewGuid(), new("human-task", "decided", "approval-1"));
        var delivery = new AgentEventDelivery("decision", _key.TenantId, wait.Selector, new("decision-doc", "private"));
        await coordinator.WaitForEventAsync(_key, root, wait);
        await coordinator.ResumeAsync(_key, root.RunId, wait.WaitId, delivery);

        var offer = new AgentHandoffOffer(Guid.NewGuid(), "quality", ["mes"], [_mes]);
        await coordinator.OfferHandoffAsync(_key, root, offer);
        var previous = await coordinator.ReadRunAsync(_key, root);
        Assert.Equal(oldPlan, previous.Plan);
        Assert.Equal(report, Assert.Single(previous.Discussion!.Messages).Artifact);
        Assert.Equal(offer, previous.PendingHandoff?.Offer);
        Assert.Equal(delivery, Assert.Single(previous.Resumptions).Delivery);
        Assert.Equal(new AgentBudget(7, 1), previous.Reservations[operation].Used);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ReserveAsync(_key, root, Guid.NewGuid(), new(1, 0)).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.AcceptHandoffAsync(_key, root.RunId, offer.HandoffId, "other").AsTask());
        var next = await coordinator.AcceptHandoffAsync(_key, root.RunId, offer.HandoffId, "quality");

        var restarted = NewCoordinator();
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.ReadRunAsync(_key, root).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.SettleAsync(_key, next, operation, new(7, 1)).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.ReadChildResultsAsync(_key, next, [child.RunId]).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.ShareChildOutputAsync(_key, next, child.RunId).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.StartChildrenAsync(_key, next, [assignment]).AsTask());
        var context = await restarted.ReadContextAsync(_key, next);
        var snapshot = await restarted.ReadRunAsync(_key, next);
        Assert.Equal(new AgentRunOwner(root.RunId, "quality", root.Epoch + 1), next);
        Assert.Equal(_mes, Assert.Single(context.Inputs));
        Assert.Equal(context.Inputs, snapshot.Inputs);
        Assert.Equal("mes", Assert.Single(snapshot.Tools));
        Assert.Null(context.ResumedBy);
        Assert.Equal(new AgentBudget(93, 4), context.RemainingBudget);
        Assert.Null(snapshot.OriginalAssignment);
        Assert.Null(snapshot.Discussion);
        Assert.Empty(snapshot.Handoffs);
        Assert.Empty(snapshot.Reservations);
        Assert.Empty(snapshot.Resumptions);
        Assert.Empty(snapshot.PlanHistory);
        Assert.Null(snapshot.Plan);

        Assert.False(await restarted.ResumeAsync(_key, root.RunId, wait.WaitId, delivery));
        var nextPlan = await restarted.SetPlanAsync(_key, next, 0,
            [assignment with { RunId = Guid.NewGuid(), Tools = ["mes"], Inputs = [_mes] }]);
        Assert.Equal(1, nextPlan.Version);
        var nextOperation = Guid.NewGuid();
        await restarted.ReserveAsync(_key, next, nextOperation, new(3, 0));
        var stored = (await _store.FindAsync(_key, default))!;
        var storedRun = stored.Runs[next.RunId];
        var legacyPending = storedRun.Reservations.SetItem(nextOperation,
            storedRun.Reservations[nextOperation] with { OwnerEpoch = 0 });
        var upgraded = stored with
        {
            Revision = stored.Revision + 1,
            Runs = stored.Runs.SetItem(next.RunId, storedRun with { Reservations = legacyPending })
        };
        Assert.True(await _store.TryReplaceAsync(upgraded, stored.Revision, default));
        Assert.False(await restarted.ReserveAsync(_key, next, nextOperation, new(3, 0)));
        Assert.True(await restarted.SettleAsync(_key, next, nextOperation, new(2, 0)));
        Assert.False(await restarted.SettleAsync(_key, next, nextOperation, new(2, 0)));
        var nextWait = new AgentEventWait(Guid.NewGuid(), new("human-task", "decided", "approval-2"));
        var nextDelivery = new AgentEventDelivery("decision-2", _key.TenantId, nextWait.Selector, _mes);
        await restarted.WaitForEventAsync(_key, next, nextWait);
        Assert.True(await restarted.ResumeAsync(_key, next.RunId, nextWait.WaitId, nextDelivery));
        var current = await restarted.ReadRunAsync(_key, next);
        Assert.Equal(nextPlan, Assert.Single(current.PlanHistory));
        Assert.Equal(nextOperation, Assert.Single(current.Reservations).Key);
        Assert.Equal(nextDelivery, Assert.Single(current.Resumptions).Delivery);

        var durable = (await _store.FindAsync(_key, default))!.Runs[root.RunId];
        Assert.Collection(durable.PlanHistory, plan => Assert.Equal(oldPlan, plan), plan => Assert.Equal(nextPlan, plan));
        Assert.Equal(2, durable.Resumptions.Length);
        Assert.Equal(2, durable.Reservations.Count);
        Assert.Equal(next, Assert.Single(durable.Handoffs).AcceptedOwner);
    }

    [Fact]
    public async Task EventResumeMatchesTenantSelectorAndReceiptAcrossCoordinatorInstances()
    {
        var coordinator = NewCoordinator();
        var root = await StartAsync(coordinator);
        var wait = new AgentEventWait(Guid.NewGuid(), new("mes", "trial-passed", "work-order-7"));
        await coordinator.WaitForEventAsync(_key, root, wait);
        var consumer = NewCoordinator();
        var delivery = new AgentEventDelivery("event-7", _key.TenantId, wait.Selector, _mes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.ResumeAsync(
            _key, root.RunId, wait.WaitId, delivery with { TenantId = "tenant-b" }).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.ResumeAsync(
            _key, root.RunId, wait.WaitId, delivery with { Selector = wait.Selector with { SubjectId = "another-work-order" } }).AsTask());
        Assert.True(await consumer.ResumeAsync(_key, root.RunId, wait.WaitId, delivery));
        Assert.Equal(delivery, (await consumer.ReadContextAsync(_key, root)).ResumedBy);
        await consumer.FinishAsync(_key, root, new(AgentResultKind.Returned, "ReadOnlyEvidence"));
        Assert.False(await coordinator.ResumeAsync(_key, root.RunId, wait.WaitId, delivery));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ResumeAsync(
            _key, root.RunId, wait.WaitId, delivery with { Payload = _asset }).AsTask());
    }

    [Fact]
    public async Task ConcurrentReservationsCannotOverspendAndCancellingRootCancelsDescendants()
    {
        var coordinator = NewCoordinator();
        var root = await StartAsync(coordinator);
        var child = Assert.Single(await coordinator.StartChildrenAsync(_key, root,
            [new(Guid.NewGuid(), "mes", "Check", new(100, 5), ["mes"], [_mes])]));
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            try
            {
                return await coordinator.ReserveAsync(_key, child, Guid.NewGuid(), new(20, 1));
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }));
        Assert.Equal(5, (await Task.WhenAll(tasks)).Count(acquired => acquired));
        Assert.Equal(default, (await coordinator.ReadContextAsync(_key, child)).RemainingBudget);
        await coordinator.FinishAsync(_key, root, new(AgentResultKind.Cancelled, "CancelledByHost"));
        var snapshot = Assert.IsType<AgentExecutionSnapshot>(await _store.FindAsync(_key, default));
        Assert.All(snapshot.Runs.Values, run => Assert.Equal(AgentResultKind.Cancelled, run.Result?.Kind));
        Assert.Equal(default, snapshot.Runs[root.RunId].RemainingBudget);
    }

    private AgentRunCoordinator NewCoordinator() => new(_store, TimeProvider.System);

    private ValueTask<AgentRunOwner> StartAsync(AgentRunCoordinator coordinator) => coordinator.StartAsync(
        _key, "user-a", DateTimeOffset.UtcNow.AddMinutes(1), new(_key.RootRunId, "root", "Review proposal", new(100, 5), ["asset", "mes"], [_asset, _mes]));
}
