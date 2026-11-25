using System.Collections.Immutable;
using System.Text.Json;
using Xunit;

namespace CrestCreates.Agent.Orchestration.Tests;

public sealed class AgentReActRunnerTests
{
    private readonly AgentExecutionKey _key = new("tenant-a", Guid.NewGuid());
    private readonly InMemoryAgentExecutionStore _store = new();

    [Fact]
    public async Task ReActUsesAnObservedToolResultAndReplaysTheTerminalResultWithoutDispatch()
    {
        var (runner, owner, coordinator) = await StartAsync();
        var model = new ScriptedModel(request => request.Round switch
        {
            1 when request.PreviousExchanges.IsEmpty => new(12, new(new("metadata.read", null), null)),
            2 when Assert.Single(request.PreviousExchanges).Observation.StructuredOutput?.GetInt32() == 2 =>
                new(13, new(null, new(AgentResultKind.Returned, "Found", new("workflow/2", "workflow-hash")))),
            _ => throw new InvalidOperationException("Unexpected model turn.")
        });
        var tools = new ScriptedTools(_ => new("Success", true, JsonSerializer.SerializeToElement(2)));
        var result = await runner.RunAsync(_key, owner, model, tools, 3, 30);
        Assert.Equal(AgentResultKind.Returned, result.Kind);
        Assert.Equal("workflow/2", result.Output?.Id);
        Assert.Equal(result, await new AgentReActRunner(coordinator, TimeProvider.System).RunAsync(_key, owner, model, tools, 3, 30));
        Assert.Equal(2, model.Calls);
        Assert.Equal(1, tools.Calls);
        var state = (await _store.FindAsync(_key, default))!.Runs[owner.RunId];
        Assert.Equal(new AgentBudget(75, 2), state.RemainingBudget);
        Assert.Equal(3, state.Reservations.Count);
    }

    [Fact]
    public async Task ReActRejectsUngrantedToolBeforeDispatch()
    {
        var (runner, owner, _) = await StartAsync();
        var model = new ScriptedModel(_ => new(12, new(new("production.release", null), null)));
        var tools = new ScriptedTools(_ => throw new InvalidOperationException("Should not dispatch."));
        var result = await runner.RunAsync(_key, owner, model, tools, 3, 30);
        Assert.Equal(new AgentRunResult(AgentResultKind.Failed, "ToolNotGranted"), result);
        Assert.Equal(0, tools.Calls);
    }

    [Fact]
    public async Task ReActDoesNotRepeatAToolWithAnUnconfirmedReceipt()
    {
        var (runner, owner, coordinator) = await StartAsync();
        var model = new ScriptedModel(_ => new(12, new(new("metadata.read", null), null)));
        var tools = new ScriptedTools(_ => throw new IOException("Lost tool response."));
        await Assert.ThrowsAsync<IOException>(() => runner.RunAsync(_key, owner, model, tools, 3, 30).AsTask());
        var reloaded = new AgentReActRunner(coordinator, TimeProvider.System);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => reloaded.RunAsync(_key, owner, model, tools, 3, 30).AsTask());
        Assert.Contains("requires reconciliation", error.Message);
        Assert.Equal(1, model.Calls);
        Assert.Equal(1, tools.Calls);
        var state = (await _store.FindAsync(_key, default))!.Runs[owner.RunId];
        Assert.Null(state.Result);
        Assert.Single(state.Reservations.Values, item => item.Used is null);
    }

    [Fact]
    public async Task ReActReconcilesALostModelResponseAndContinuesWithoutRepeatingTheTurn()
    {
        var (_, owner, coordinator) = await StartAsync();
        AgentModelTurnResponse? recovered = null;
        var lostModel = new ScriptedModel(_ =>
        {
            recovered = new(12, new(new("metadata.read", JsonSerializer.SerializeToElement(new { Query = "asset" })), null));
            throw new IOException("Lost model response.");
        });
        var tools = new ScriptedTools(_ => new("Success", true, JsonSerializer.SerializeToElement(7)));
        await Assert.ThrowsAsync<IOException>(() => new AgentReActRunner(coordinator, TimeProvider.System)
            .RunAsync(_key, owner, lostModel, tools, 3, 30).AsTask());
        var interrupted = (await _store.FindAsync(_key, default))!.Runs[owner.RunId];
        var operation = Assert.Single(interrupted.Reservations, entry => entry.Value.Used is null).Key;
        Assert.True(await coordinator.ReconcileAsync(_key, owner, operation,
            new AgentModelTurnReconciliation(recovered!.UsedTokens, recovered.Decision)));

        var continuationCalls = 0;
        var continuation = new ScriptedModel(request =>
        {
            continuationCalls++;
            Assert.Equal(2, request.Round);
            Assert.Equal(7, Assert.Single(request.PreviousExchanges).Observation.StructuredOutput?.GetInt32());
            return new(13, new(null, new(AgentResultKind.Returned, "Recovered", new("report", "hash"))));
        });
        var result = await new AgentReActRunner(new AgentRunCoordinator(_store, TimeProvider.System), TimeProvider.System)
            .RunAsync(_key, owner, continuation, tools, 3, 30);
        Assert.Equal("Recovered", result.Code);
        Assert.Equal(1, lostModel.Calls);
        Assert.Equal(1, continuationCalls);
        Assert.Equal(1, tools.Calls);
    }

    [Fact]
    public async Task ReActReconcilesALostToolResponseAndContinuesWithoutRepeatingTheTool()
    {
        var (_, owner, coordinator) = await StartAsync();
        var model = new ScriptedModel(request => request.Round == 1
            ? new(12, new(new("metadata.read", null), null))
            : new(13, new(null, new(AgentResultKind.Returned, "Recovered", new("report", "hash")))));
        AgentToolObservation? recovered = null;
        var tools = new ScriptedTools(_ =>
        {
            recovered = new("Success", true, JsonSerializer.SerializeToElement("before\0after"));
            throw new IOException("Lost tool response.");
        });
        await Assert.ThrowsAsync<IOException>(() => new AgentReActRunner(coordinator, TimeProvider.System)
            .RunAsync(_key, owner, model, tools, 3, 30).AsTask());
        var interrupted = (await _store.FindAsync(_key, default))!.Runs[owner.RunId];
        var operation = Assert.Single(interrupted.Reservations, entry => entry.Value.Used is null).Key;
        var reconciliation = new AgentToolObservationReconciliation(recovered!);
        Assert.True(await coordinator.ReconcileAsync(_key, owner, operation, reconciliation));
        Assert.False(await coordinator.ReconcileAsync(_key, owner, operation, reconciliation));

        var result = await new AgentReActRunner(new AgentRunCoordinator(_store, TimeProvider.System), TimeProvider.System)
            .RunAsync(_key, owner, model, tools, 3, 30);
        Assert.Equal("Recovered", result.Code);
        Assert.Equal(2, model.Calls);
        Assert.Equal(1, tools.Calls);
        var state = (await _store.FindAsync(_key, default))!.Runs[owner.RunId];
        Assert.Equal("before\0after", state.Reservations[operation].ToolObservation?.StructuredOutput?.GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecordedModelAndToolReceiptsSurviveRestartAndAreScopedToTheirEpoch(bool handoff)
    {
        var (_, initial, coordinator) = await StartAsync();
        var offer = new AgentHandoffOffer(Guid.NewGuid(), "review", ["metadata.read"], []);
        await coordinator.OfferHandoffAsync(_key, initial, offer);
        var owner = await coordinator.AcceptHandoffAsync(_key, initial.RunId, offer.HandoffId, offer.RecipientAgentId);
        var interrupting = new AgentRunCoordinator(new InterruptFinishStore(_store), TimeProvider.System);
        var model = new ScriptedModel(request => request.Round switch
        {
            1 => new(5, new(new("metadata.read", JsonSerializer.SerializeToElement(new { Private = "arguments" })), null)),
            2 => new(5, new(null, new(AgentResultKind.Returned, "PrivateReport", new("private-report", "hash")))),
            _ => throw new InvalidOperationException("Unexpected model turn.")
        });
        var tools = new ScriptedTools(_ => new("Success", true, JsonSerializer.SerializeToElement("private observation")));
        await Assert.ThrowsAsync<IOException>(() => new AgentReActRunner(interrupting, TimeProvider.System)
            .RunAsync(_key, owner, model, tools, 2, 10).AsTask());
        var ownerView = await coordinator.ReadRunAsync(_key, owner);
        Assert.Equal(3, ownerView.Reservations.Count);
        Assert.Equal("private observation", Assert.Single(ownerView.Reservations.Values,
            entry => entry.ToolObservation is not null).ToolObservation!.StructuredOutput?.GetString());

        var restarted = new AgentRunCoordinator(_store, TimeProvider.System);
        var runner = new AgentReActRunner(restarted, TimeProvider.System);
        if (handoff)
        {
            var nextOffer = new AgentHandoffOffer(Guid.NewGuid(), "final-review", [], []);
            await restarted.OfferHandoffAsync(_key, owner, nextOffer);
            var next = await restarted.AcceptHandoffAsync(_key, owner.RunId, nextOffer.HandoffId, nextOffer.RecipientAgentId);
            Assert.Empty((await restarted.ReadRunAsync(_key, next)).Reservations);
            var nextModel = new ScriptedModel(request =>
            {
                Assert.Empty(request.PreviousExchanges);
                return new(5, new(null, new(AgentResultKind.Returned, "FreshReport")));
            });
            Assert.Equal("FreshReport", (await runner.RunAsync(_key, next, nextModel, tools, 2, 10)).Code);
        }
        else
        {
            // Pre-upgrade records did not store the reservation epoch, even after a handoff.
            var stored = (await _store.FindAsync(_key, default))!;
            var legacy = ownerView.Reservations.ToImmutableDictionary(entry => entry.Key,
                entry => entry.Value with { OwnerEpoch = 0 });
            var runs = stored.Runs.SetItem(owner.RunId, stored.Runs[owner.RunId] with { Reservations = legacy });
            Assert.True(await _store.TryReplaceAsync(stored with { Revision = stored.Revision + 1, Runs = runs }, stored.Revision, default));
            Assert.Empty((await restarted.ReadRunAsync(_key, owner)).Reservations);
            Assert.Equal("PrivateReport", (await runner.RunAsync(_key, owner, model, tools, 2, 10)).Code);
        }

        Assert.Equal(2, model.Calls);
        Assert.Equal(1, tools.Calls);
    }

    private async Task<(AgentReActRunner Runner, AgentRunOwner Owner, AgentRunCoordinator Coordinator)> StartAsync()
    {
        var clock = TimeProvider.System;
        var coordinator = new AgentRunCoordinator(_store, clock);
        var owner = await coordinator.StartAsync(_key, "engineer", clock.GetUtcNow().AddMinutes(1),
            new(_key.RootRunId, "discovery", "Discover workflow and dependencies.", new(100, 3), ["metadata.read"], []));
        return (new(coordinator, clock), owner, coordinator);
    }

    private sealed class InterruptFinishStore(IAgentExecutionStore inner) : IAgentExecutionStore
    {
        public ValueTask<AgentExecutionSnapshot?> FindAsync(AgentExecutionKey key, CancellationToken cancellationToken) =>
            inner.FindAsync(key, cancellationToken);

        public ValueTask<bool> TryCreateAsync(AgentExecutionSnapshot execution, CancellationToken cancellationToken) =>
            inner.TryCreateAsync(execution, cancellationToken);

        public ValueTask<bool> TryReplaceAsync(AgentExecutionSnapshot execution, long expectedRevision, CancellationToken cancellationToken) =>
            execution.Runs[execution.Key.RootRunId].Result is not null
                ? throw new IOException("Interrupted before the terminal receipt was stored.")
                : inner.TryReplaceAsync(execution, expectedRevision, cancellationToken);
    }

    private sealed class ScriptedModel(Func<AgentModelTurnRequest, AgentModelTurnResponse> respond) : IAgentModelTurnExecutor
    {
        public int Calls { get; private set; }

        public ValueTask<AgentModelTurnResponse> ExecuteAsync(AgentModelTurnRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(respond(request));
        }
    }

    private sealed class ScriptedTools(Func<AgentToolSelection, AgentToolObservation> invoke) : IAgentRunToolExecutor
    {
        public int Calls { get; private set; }

        public ValueTask<AgentToolObservation> InvokeAsync(AgentRunContext context, Guid operationId,
            AgentToolSelection selection, CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(invoke(selection));
        }
    }
}
