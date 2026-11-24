using System.Text.Json;
using CrestCreates.Agent.Orchestration;
using CrestCreates.Runtime.Persistence.PostgreSql;
using Microsoft.Extensions.DependencyInjection;

internal static class AgentOrchestrationNativeScenario
{
    internal static async Task RunAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        var services = new ServiceCollection().AddCrestCreatesPostgreSqlRuntimePersistence(options);
        await using var first = services.BuildServiceProvider();
        var coordinator = new AgentRunCoordinator(first.GetRequiredService<IAgentExecutionStore>(), TimeProvider.System);
        var discovery = new AgentExecutionKey("native-agent-tenant", Guid.NewGuid());
        var discoveryOwner = await CreateAsync(coordinator, discovery);
        var model = new ScriptedModel();
        var tools = new ScriptedTools();
        var result = await new AgentReActRunner(coordinator, TimeProvider.System)
            .RunAsync(discovery, discoveryOwner, model, tools, 3, 30);
        if (result.Kind != AgentResultKind.Returned || result.Output?.Id != "mes-report" || model.Calls != 2 || tools.Calls != 1)
        {
            throw new InvalidOperationException("Native ReAct did not consume its tool observation.");
        }

        await using var second = services.BuildServiceProvider();
        var freshCoordinator = new AgentRunCoordinator(second.GetRequiredService<IAgentExecutionStore>(), TimeProvider.System);
        var replay = await new AgentReActRunner(freshCoordinator, TimeProvider.System)
            .RunAsync(discovery, discoveryOwner, model, tools, 3, 30);
        if (replay != result || model.Calls != 2 || tools.Calls != 1)
        {
            throw new InvalidOperationException("Native recovery repeated a completed model or tool action.");
        }

        var key = new AgentExecutionKey("native-agent-tenant", Guid.NewGuid());
        var owner = await CreateAsync(freshCoordinator, key);
        var child = AssertSingle(await freshCoordinator.StartChildrenAsync(key, owner,
            [new(Guid.NewGuid(), "quality", "Review quality", new(20, 1), ["mes.read"], [])]));
        await freshCoordinator.FinishAsync(key, child, new(AgentResultKind.Returned, "QualityReport", new("quality-report", "quality-hash")));
        var waitId = Guid.NewGuid();
        var selector = new AgentEventSelector("human-task", "decided", key.RootRunId.ToString("N"));
        await freshCoordinator.WaitForEventAsync(key, owner, new(waitId, selector));
        await freshCoordinator.ResumeAsync(key, owner.RunId, waitId,
            new("decision", key.TenantId, selector, new("decision-doc", "decision-hash")));
        var offer = new AgentHandoffOffer(Guid.NewGuid(), "quality", ["mes.read"], []);
        await freshCoordinator.OfferHandoffAsync(key, owner, offer);
        var accepted = await freshCoordinator.AcceptHandoffAsync(key, owner.RunId, offer.HandoffId, "quality");
        await RequireDenial(() => freshCoordinator.ReserveAsync(key, owner, Guid.NewGuid(), new(1, 0)));
        if (accepted.Epoch != owner.Epoch + 1 || (await freshCoordinator.ReadContextAsync(key, accepted)).ResumedBy is not null)
        {
            throw new InvalidOperationException("Native handoff did not fence the prior ownership scope.");
        }

        await RequireDenial(() => freshCoordinator.RecordCapabilityFactAsync(key,
            new("release", "production-release", AgentCapabilityCommit.Committed, null)));
        await freshCoordinator.RecordWorkflowFactAsync(key, new("workflow", "maintenance", 3, "hash", AgentWorkflowOutcome.Completed));
        await freshCoordinator.RecordCapabilityFactAsync(key, new("release", "production-release", AgentCapabilityCommit.Rejected, null));
        await freshCoordinator.FinishAsync(key, accepted, new(AgentResultKind.Returned, "Reviewed"));
        await using var third = services.BuildServiceProvider();
        var recovered = new AgentRunCoordinator(third.GetRequiredService<IAgentExecutionStore>(), TimeProvider.System);
        var state = await recovered.ReadRunAsync(key, accepted, default);
        var durable = (await third.GetRequiredService<IAgentExecutionStore>().FindAsync(key, default))!;
        var rootReceipts = durable.Runs[owner.RunId];
        if (state.Result?.Kind != AgentResultKind.Returned
            || state.Resumptions.Length != 0
            || rootReceipts.Resumptions.Length != 1
            || rootReceipts.Handoffs.Length != 1)
        {
            throw new InvalidOperationException("Native run receipts were not retained across ownership epochs.");
        }

        Console.WriteLine("CRESTCREATES_AGENT_ORCHESTRATION_NATIVE_OK");
    }

    private static ValueTask<AgentRunOwner> CreateAsync(AgentRunCoordinator coordinator, AgentExecutionKey key) =>
        coordinator.StartAsync(key, "engineer", TimeProvider.System.GetUtcNow().AddMinutes(2),
            new(key.RootRunId, "discovery", "Find MES evidence", new(100, 5), ["mes.read"], []));

    private static T AssertSingle<T>(IReadOnlyList<T> values) => values.Count == 1 ? values[0] : throw new InvalidOperationException("Expected one value.");

    private static async Task RequireDenial(Func<ValueTask<bool>> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return;
        }

        throw new InvalidOperationException("A native negative control unexpectedly succeeded.");
    }

    private sealed class ScriptedModel : IAgentModelTurnExecutor
    {
        internal int Calls { get; private set; }

        public ValueTask<AgentModelTurnResponse> ExecuteAsync(AgentModelTurnRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            if (request.Round == 1)
            {
                return ValueTask.FromResult(new AgentModelTurnResponse(10, new(new("mes.read", null), null)));
            }

            if (request.PreviousExchanges.Length != 1 || request.PreviousExchanges[0].Observation.StructuredOutput?.GetString() != "fresh-evidence")
            {
                throw new InvalidOperationException("The native observation was unavailable.");
            }

            return ValueTask.FromResult(new AgentModelTurnResponse(10,
                new(null, new(AgentResultKind.Returned, "ReportReady", new("mes-report", "report-hash")))));
        }
    }

    private sealed class ScriptedTools : IAgentRunToolExecutor
    {
        internal int Calls { get; private set; }

        public ValueTask<AgentToolObservation> InvokeAsync(AgentRunContext context, Guid operationId,
            AgentToolSelection selection, CancellationToken cancellationToken)
        {
            Calls++;
            using var json = JsonDocument.Parse("\"fresh-evidence\"");
            return ValueTask.FromResult(new AgentToolObservation("Success", true, json.RootElement.Clone()));
        }
    }
}
