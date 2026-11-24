using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using CrestCreates.Agent.Orchestration;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

internal static class AgentOrchestrationNativeScenario
{
    internal static async Task RunAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        await RunReActAsync(options);
        await RunPlanAndExecuteAsync(options);
        await RunSequentialAsync(options);
        await RunParallelAsync(options);
        await RunSupervisorAsync(options);
        await RunHandoffAsync(options);
        await RunGroupChatAsync(options);
        await RunHumanInTheLoopAsync(options);
        await RunFreshProviderReplaysAsync(options);
        await RunOrchestrationNegativeControlsAsync(options);
        await RunCrashWindowScenariosAsync(options);
        Console.WriteLine("CRESTCREATES_AGENT_ORCHESTRATION_NATIVE_OK");
    }

    private static async Task RunReActAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        await using var first = BuildProvider(options);
        var coordinator = Coordinator(first);
        var key = new AgentExecutionKey("native-agent-tenant", Guid.NewGuid());
        var owner = await CreateAsync(coordinator, key);
        var model = new ScriptedModel();
        var tools = new ScriptedTools();
        var result = await new AgentReActRunner(coordinator, TimeProvider.System)
            .RunAsync(key, owner, model, tools, 3, 30);
        if (result.Kind != AgentResultKind.Returned || result.Output?.Id != "mes-report" || model.Calls != 2 || tools.Calls != 1)
        {
            throw new InvalidOperationException("Native ReAct did not consume its tool observation.");
        }

        await using var second = BuildProvider(options);
        var replay = await new AgentReActRunner(Coordinator(second), TimeProvider.System)
            .RunAsync(key, owner, model, tools, 3, 30);
        if (replay != result || model.Calls != 2 || tools.Calls != 1)
        {
            throw new InvalidOperationException("Native recovery repeated a completed model or tool action.");
        }

        Console.WriteLine("CRESTCREATES_AGENT_MODE_REACT_NATIVE_OK");
    }

    private static async Task RunPlanAndExecuteAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        await using var provider = BuildProvider(options);
        var coordinator = Coordinator(provider);
        var key = new AgentExecutionKey("native-agent-tenant", Guid.NewGuid());
        var owner = await CreateAsync(coordinator, key, new(100, 4), []);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var assignments = ImmutableArray.Create(
            Assignment(firstId, "planner", "Prepare evidence", new(20, 1), []),
            Assignment(secondId, "reviewer", "Review evidence", new(20, 1), []));
        await coordinator.SetPlanAsync(key, owner, 0, assignments);
        var firstOutput = Output(firstId);
        var first = new ScriptedWorker(firstOutput, context => Assert.Empty(context.Inputs));
        var second = new ScriptedWorker(Output(secondId), context =>
            Assert.Equal(firstOutput, context.Inputs.Single()));
        var team = new AgentTeamRunner(coordinator,
            [new AgentTeamMember("planner", [], first), new AgentTeamMember("reviewer", [], second)]);
        var result = await team.RunPlanAsync(key, owner, 1);
        if (!result.AllReturned || result.Steps.Length != 2 || first.Calls != 1 || second.Calls != 1)
        {
            throw new InvalidOperationException("Native Plan-and-Execute did not run its versioned plan.");
        }

        var plan = (await coordinator.ReadRunAsync(key, owner)).PlanHistory.Single();
        if (plan.Version != 1 || !plan.Steps.SequenceEqual(assignments))
        {
            throw new InvalidOperationException("Native Plan-and-Execute lost its immutable plan identity.");
        }

        await coordinator.FinishAsync(key, owner, new(AgentResultKind.Returned, "PlanCompleted"));
        Console.WriteLine("CRESTCREATES_AGENT_MODE_PLAN_AND_EXECUTE_NATIVE_OK");
    }

    private static async Task RunSequentialAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        await using var provider = BuildProvider(options);
        var coordinator = Coordinator(provider);
        var key = new AgentExecutionKey("native-agent-tenant", Guid.NewGuid());
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var initial = new AgentArtifactReference("initial-evidence", "initial-hash");
        var owner = await CreateAsync(coordinator, key, new(100, 4), [], [initial]);
        var firstOutput = Output(firstId);
        var first = new ScriptedWorker(firstOutput, context => Assert.Equal(initial, context.Inputs.Single()));
        var second = new ScriptedWorker(Output(secondId), context =>
            Assert.Equal(firstOutput, context.Inputs.Single()));
        var team = new AgentTeamRunner(coordinator,
            [new AgentTeamMember("first", [], first), new AgentTeamMember("second", [], second)]);
        var result = await team.RunSequentialAsync(key, owner, ImmutableArray.Create(
            Assignment(firstId, "first", "Produce evidence", new(20, 1), [], [initial]),
            Assignment(secondId, "second", "Consume evidence", new(20, 1), [])));
        if (!result.AllReturned || result.Steps.Length != 2 || first.Calls != 1 || second.Calls != 1)
        {
            throw new InvalidOperationException("Native Sequential did not pass only the preceding artifact.");
        }

        await coordinator.FinishAsync(key, owner, new(AgentResultKind.Returned, "SequentialCompleted"));
        Console.WriteLine("CRESTCREATES_AGENT_MODE_SEQUENTIAL_NATIVE_OK");
    }

    private static async Task RunParallelAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        await using var provider = BuildProvider(options);
        var coordinator = Coordinator(provider);
        var key = new AgentExecutionKey("native-agent-tenant", Guid.NewGuid());
        var barrier = new ParallelBarrier();
        var firstInput = new AgentArtifactReference("parallel-first-input", "first-hash");
        var secondInput = new AgentArtifactReference("parallel-second-input", "second-hash");
        var owner = await CreateAsync(coordinator, key, new(100, 4), [], [firstInput, secondInput]);
        var first = new BarrierWorker(barrier, firstInput);
        var second = new BarrierWorker(barrier, secondInput);
        var team = new AgentTeamRunner(coordinator,
            [new AgentTeamMember("first", [], first), new AgentTeamMember("second", [], second)]);
        var result = await team.RunParallelAsync(key, owner, ImmutableArray.Create(
            Assignment(Guid.NewGuid(), "first", "Inspect first scope", new(20, 1), [], [firstInput]),
            Assignment(Guid.NewGuid(), "second", "Inspect second scope", new(20, 1), [], [secondInput])));
        if (!result.AllReturned || result.Steps.Length != 2 || barrier.MaximumActive != 2 ||
            result.Steps[0].RunId == result.Steps[1].RunId)
        {
            throw new InvalidOperationException("Native Parallel did not execute isolated child scopes concurrently.");
        }

        await coordinator.FinishAsync(key, owner, new(AgentResultKind.Returned, "ParallelCompleted"));
        Console.WriteLine("CRESTCREATES_AGENT_MODE_PARALLEL_NATIVE_OK");
    }

    private static async Task RunSupervisorAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        await using var provider = BuildProvider(options);
        var coordinator = Coordinator(provider);
        var key = new AgentExecutionKey("native-agent-tenant", Guid.NewGuid());
        var owner = await CreateAsync(coordinator, key, new(100, 2), []);
        var worker = new ScriptedWorker(Output(Guid.NewGuid()));
        var team = new AgentTeamRunner(coordinator, [new AgentTeamMember("specialist", [], worker)]);
        var result = await team.DelegateAsync(key, owner, ImmutableArray.Create(
            Assignment(Guid.NewGuid(), "specialist", "Review delegated evidence", new(20, 1), [])));
        var step = AssertSingle(result.Steps);
        if (!result.AllReturned || step.AgentId == owner.AgentId || worker.Calls != 1)
        {
            throw new InvalidOperationException("Native Supervisor did not delegate to a distinct worker.");
        }

        await coordinator.FinishAsync(key, owner, new(AgentResultKind.Returned, "SupervisorCompleted"));
        Console.WriteLine("CRESTCREATES_AGENT_MODE_SUPERVISOR_NATIVE_OK");
    }

    private static async Task RunHandoffAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        await using var first = BuildProvider(options);
        var coordinator = Coordinator(first);
        var key = new AgentExecutionKey("native-agent-tenant", Guid.NewGuid());
        var handoffInput = new AgentArtifactReference("handoff-input", "handoff-hash");
        var owner = await CreateAsync(coordinator, key, new(100, 2), ["mes.read"], [handoffInput]);
        var child = AssertSingle(await coordinator.StartChildrenAsync(key, owner,
            [Assignment(Guid.NewGuid(), "quality", "Review quality", new(20, 1), ["mes.read"])]));
        await coordinator.FinishAsync(key, child,
            new(AgentResultKind.Returned, "QualityReport", new("quality-report", "quality-hash")));
        var waitId = Guid.NewGuid();
        var selector = new AgentEventSelector("human-task", "decided", key.RootRunId.ToString("N"));
        await coordinator.WaitForEventAsync(key, owner, new(waitId, selector));
        await coordinator.ResumeAsync(key, owner.RunId, waitId,
            new("decision", key.TenantId, selector, new("decision-doc", "decision-hash")));
        var offer = new AgentHandoffOffer(Guid.NewGuid(), "quality", ["mes.read"], [handoffInput]);
        await coordinator.OfferHandoffAsync(key, owner, offer);
        var accepted = await coordinator.AcceptHandoffAsync(key, owner.RunId, offer.HandoffId, "quality");
        await RequireDenial(() => coordinator.ReserveAsync(key, owner, Guid.NewGuid(), new(1, 0)));
        var context = await coordinator.ReadContextAsync(key, accepted);
        if (accepted.Epoch != owner.Epoch + 1 || context.ResumedBy is not null ||
            !context.Inputs.SequenceEqual([handoffInput]))
        {
            throw new InvalidOperationException("Native handoff did not fence the prior ownership scope.");
        }

        await RequireDenial(() => coordinator.RecordCapabilityFactAsync(key,
            new("release", "production-release", AgentCapabilityCommit.Committed, null)));
        await coordinator.RecordWorkflowFactAsync(key,
            new("workflow", "maintenance", 3, "hash", AgentWorkflowOutcome.Completed));
        await coordinator.RecordCapabilityFactAsync(key,
            new("release", "production-release", AgentCapabilityCommit.Rejected, null));
        await coordinator.FinishAsync(key, accepted, new(AgentResultKind.Returned, "Reviewed"));
        await using var recoveredProvider = BuildProvider(options);
        var recoveredStore = recoveredProvider.GetRequiredService<IAgentExecutionStore>();
        var state = await new AgentRunCoordinator(recoveredStore, TimeProvider.System).ReadRunAsync(key, accepted);
        var durable = (await recoveredStore.FindAsync(key, default))!;
        var rootReceipts = durable.Runs[owner.RunId];
        if (state.Result?.Kind != AgentResultKind.Returned || state.Resumptions.Length != 0 ||
            rootReceipts.Resumptions.Length != 1 || rootReceipts.Handoffs.Length != 1 ||
            durable.Workflows.Count != 1 || durable.Capabilities.Count != 1)
        {
            throw new InvalidOperationException("Native handoff lost receipts or distinct completion facts.");
        }

        Console.WriteLine("CRESTCREATES_AGENT_MODE_HANDOFF_NATIVE_OK");
    }

    private static async Task RunGroupChatAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        await using var provider = BuildProvider(options);
        var coordinator = Coordinator(provider);
        var key = new AgentExecutionKey("native-agent-tenant", Guid.NewGuid());
        var owner = await CreateAsync(coordinator, key, new(100, 4), []);
        var first = new ScriptedWorker(Output(Guid.NewGuid()));
        var second = new ScriptedWorker(Output(Guid.NewGuid()));
        var team = new AgentTeamRunner(coordinator,
            [new AgentTeamMember("analyst", [], first), new AgentTeamMember("reviewer", [], second)]);
        var discussion = await team.RunDiscussionAsync(key, owner, Guid.NewGuid(), 4, ImmutableArray.Create(
            Assignment(Guid.NewGuid(), "analyst", "Analyze", new(20, 1), []),
            Assignment(Guid.NewGuid(), "reviewer", "Challenge", new(20, 1), [])));
        if (!discussion.Closed || discussion.Messages.Length != 4 || first.Calls != 2 || second.Calls != 2 ||
            !discussion.Messages.Select(message => message.AgentId).SequenceEqual(["analyst", "reviewer", "analyst", "reviewer"]))
        {
            throw new InvalidOperationException("Native GroupChat did not preserve round-robin replies.");
        }

        var analystContexts = first.Contexts.ToArray();
        var reviewerContexts = second.Contexts.ToArray();
        if (!analystContexts[1].Inputs.Contains(discussion.Messages[1].Artifact) ||
            !reviewerContexts[1].Inputs.Contains(discussion.Messages[2].Artifact))
        {
            throw new InvalidOperationException("Native GroupChat did not expose prior published replies explicitly.");
        }

        await coordinator.FinishAsync(key, owner, new(AgentResultKind.Returned, "DiscussionCompleted"));
        Console.WriteLine("CRESTCREATES_AGENT_MODE_GROUP_CHAT_NATIVE_OK");
    }

    private static async Task RunHumanInTheLoopAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        await using var waitingProvider = BuildProvider(options);
        var coordinator = Coordinator(waitingProvider);
        var key = new AgentExecutionKey("native-agent-tenant", Guid.NewGuid());
        var owner = await CreateAsync(coordinator, key);
        var waitId = Guid.NewGuid();
        var selector = new AgentEventSelector("human-task", "approved", key.RootRunId.ToString("N"));
        await coordinator.WaitForEventAsync(key, owner, new(waitId, selector));
        var output = Output(Guid.NewGuid());
        var worker = new ScriptedWorker(output, context =>
        {
            Assert.Equal("approval", context.ResumedBy?.EventId);
            Assert.Equal(selector, context.ResumedBy?.Selector);
        });
        await using var resumedProvider = BuildProvider(options);
        var resumedCoordinator = Coordinator(resumedProvider);
        var delivery = new AgentEventDelivery("approval", key.TenantId, selector,
            new("approval-doc", "approval-hash"));
        if (!await resumedCoordinator.ResumeAsync(key, owner.RunId, waitId, delivery))
        {
            throw new InvalidOperationException("Native HITL did not durably register its first event.");
        }

        var context = await resumedCoordinator.ReadContextAsync(key, owner);
        var result = await worker.RunAsync(context, default);
        await resumedCoordinator.FinishAsync(key, owner, result);
        var snapshot = await resumedCoordinator.ReadRunAsync(key, owner);
        var resumption = snapshot.Resumptions.Single();
        if (snapshot.Result != result || resumption.Delivery != delivery || worker.Calls != 1)
        {
            throw new InvalidOperationException("Native HITL did not resume from durable human evidence.");
        }

        Console.WriteLine("CRESTCREATES_AGENT_MODE_HUMAN_IN_THE_LOOP_NATIVE_OK");
    }

    private static async Task RunFreshProviderReplaysAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        await ReplayPlanAcrossProvidersAsync(options);
        await ReplayParallelAcrossProvidersAsync(options);
        await ReplayHandoffAcrossProvidersAsync(options);
        await ReplayHumanInTheLoopAcrossProvidersAsync(options);
    }

    private static async Task ReplayPlanAcrossProvidersAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        var key = new AgentExecutionKey("native-agent-tenant", Guid.NewGuid());
        AgentRunOwner owner;
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var assignments = ImmutableArray.Create(
            Assignment(firstId, "planner", "Prepare", new(20, 1), []),
            Assignment(secondId, "reviewer", "Review", new(20, 1), []));
        await using (var planning = BuildProvider(options))
        {
            owner = await CreateAsync(Coordinator(planning), key, new(100, 4), []);
            await Coordinator(planning).SetPlanAsync(key, owner, 0, assignments);
        }

        AgentTeamResult initial;
        await using (var execution = BuildProvider(options))
        {
            initial = await new AgentTeamRunner(Coordinator(execution),
                [new AgentTeamMember("planner", [], new ScriptedWorker(Output(firstId))),
                 new AgentTeamMember("reviewer", [], new ScriptedWorker(Output(secondId)))])
                .RunPlanAsync(key, owner, 1);
        }

        var replayWorker = new ScriptedWorker(Output(firstId));
        var replayReviewer = new ScriptedWorker(Output(secondId));
        await using (var recovery = BuildProvider(options))
        {
            var replay = await new AgentTeamRunner(Coordinator(recovery),
                [new AgentTeamMember("planner", [], replayWorker),
                 new AgentTeamMember("reviewer", [], replayReviewer)])
                .RunPlanAsync(key, owner, 1);
            if (!replay.Steps.SequenceEqual(initial.Steps) || replayWorker.Calls != 0 || replayReviewer.Calls != 0)
            {
                throw new InvalidOperationException("Native Plan replay dispatched a completed step again.");
            }
        }

        Console.WriteLine("CRESTCREATES_AGENT_REPLAY_PLAN_NATIVE_OK");
    }

    private static async Task ReplayParallelAcrossProvidersAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        var key = new AgentExecutionKey("native-agent-tenant", Guid.NewGuid());
        AgentRunOwner owner;
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var assignments = ImmutableArray.Create(
            Assignment(firstId, "first", "First", new(20, 1), []),
            Assignment(secondId, "second", "Second", new(20, 1), []));
        await using (var planning = BuildProvider(options))
        {
            owner = await CreateAsync(Coordinator(planning), key, new(100, 4), []);
        }

        AgentTeamResult initial;
        await using (var execution = BuildProvider(options))
        {
            initial = await new AgentTeamRunner(Coordinator(execution),
                [new AgentTeamMember("first", [], new ScriptedWorker(Output(firstId))),
                 new AgentTeamMember("second", [], new ScriptedWorker(Output(secondId)))])
                .RunParallelAsync(key, owner, assignments);
        }

        var firstReplay = new ScriptedWorker(Output(firstId));
        var secondReplay = new ScriptedWorker(Output(secondId));
        await using (var recovery = BuildProvider(options))
        {
            var replay = await new AgentTeamRunner(Coordinator(recovery),
                [new AgentTeamMember("first", [], firstReplay), new AgentTeamMember("second", [], secondReplay)])
                .RunParallelAsync(key, owner, assignments);
            if (!replay.Steps.SequenceEqual(initial.Steps) || firstReplay.Calls != 0 || secondReplay.Calls != 0)
            {
                throw new InvalidOperationException("Native Parallel replay dispatched a completed child again.");
            }
        }

        Console.WriteLine("CRESTCREATES_AGENT_REPLAY_PARALLEL_NATIVE_OK");
    }

    private static async Task ReplayHandoffAcrossProvidersAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        var key = new AgentExecutionKey("native-agent-tenant", Guid.NewGuid());
        AgentRunOwner original;
        var offer = new AgentHandoffOffer(Guid.NewGuid(), "recipient", [], []);
        await using (var offering = BuildProvider(options))
        {
            original = await CreateAsync(Coordinator(offering), key, new(100, 2), []);
            await Coordinator(offering).OfferHandoffAsync(key, original, offer);
        }

        AgentRunOwner accepted;
        await using (var accepting = BuildProvider(options))
        {
            accepted = await Coordinator(accepting).AcceptHandoffAsync(
                key, original.RunId, offer.HandoffId, offer.RecipientAgentId);
        }

        await using (var continuing = BuildProvider(options))
        {
            var coordinator = Coordinator(continuing);
            await RequireDenial(async () =>
            {
                await coordinator.AcceptHandoffAsync(key, original.RunId, offer.HandoffId, offer.RecipientAgentId);
                return false;
            });
            await RequireDenial(() => coordinator.ReserveAsync(key, original, Guid.NewGuid(), new(1, 0)));
            await coordinator.FinishAsync(key, accepted, new(AgentResultKind.Returned, "HandoffCompleted"));
        }

        await using (var recovery = BuildProvider(options))
        {
            var store = recovery.GetRequiredService<IAgentExecutionStore>();
            var durable = (await store.FindAsync(key, default))!;
            var root = durable.Runs[original.RunId];
            if (root.Result?.Kind != AgentResultKind.Returned || root.Owner != accepted || root.Handoffs.Length != 1)
            {
                throw new InvalidOperationException("Native Handoff replay lost its accepted ownership epoch.");
            }
        }

        Console.WriteLine("CRESTCREATES_AGENT_REPLAY_HANDOFF_NATIVE_OK");
    }

    private static async Task ReplayHumanInTheLoopAcrossProvidersAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        var key = new AgentExecutionKey("native-agent-tenant", Guid.NewGuid());
        AgentRunOwner owner;
        var waitId = Guid.NewGuid();
        var selector = new AgentEventSelector("human-task", "approved", "replay-subject");
        await using (var waiting = BuildProvider(options))
        {
            owner = await CreateAsync(Coordinator(waiting), key);
            await Coordinator(waiting).WaitForEventAsync(key, owner, new(waitId, selector));
        }

        var delivery = new AgentEventDelivery("approval", key.TenantId, selector,
            new("approval-doc", "approval-hash"));
        await using (var resuming = BuildProvider(options))
        {
            if (!await Coordinator(resuming).ResumeAsync(key, owner.RunId, waitId, delivery))
            {
                throw new InvalidOperationException("Native HITL replay did not register the first delivery.");
            }
        }

        var worker = new ScriptedWorker(Output(owner.RunId), context => Assert.Equal(delivery, context.ResumedBy));
        await using (var completing = BuildProvider(options))
        {
            var coordinator = Coordinator(completing);
            if (await coordinator.ResumeAsync(key, owner.RunId, waitId, delivery))
            {
                throw new InvalidOperationException("Native HITL replay duplicated a durable delivery.");
            }

            var result = await worker.RunAsync(await coordinator.ReadContextAsync(key, owner), default);
            await coordinator.FinishAsync(key, owner, result);
        }

        await using (var recovery = BuildProvider(options))
        {
            var snapshot = await Coordinator(recovery).ReadRunAsync(key, owner);
            if (snapshot.Resumptions.Length != 1 || snapshot.Result?.Kind != AgentResultKind.Returned || worker.Calls != 1)
            {
                throw new InvalidOperationException("Native HITL replay duplicated work or lost its receipt.");
            }
        }

        Console.WriteLine("CRESTCREATES_AGENT_REPLAY_HUMAN_IN_THE_LOOP_NATIVE_OK");
    }

    private static async Task RunCrashWindowScenariosAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        (string Scenario, string Sentinel, string OutputSentinel)[] scenarios =
        [
            ("orchestration-crash-handoff-accept", "CRESTCREATES_AGENT_CRASH_HANDOFF_ACCEPT_COMMITTED", "CRESTCREATES_AGENT_CRASH_HANDOFF_ACCEPT_OK"),
            ("orchestration-crash-event-resume", "CRESTCREATES_AGENT_CRASH_EVENT_RESUME_COMMITTED", "CRESTCREATES_AGENT_CRASH_EVENT_RESUME_OK"),
            ("orchestration-crash-parallel-claim", "CRESTCREATES_AGENT_CRASH_PARALLEL_CLAIM_COMMITTED", "CRESTCREATES_AGENT_CRASH_PARALLEL_CLAIM_OK")
        ];
        foreach (var scenario in scenarios)
        {
            var applicationName = "aot-orchestration-crash-" + Guid.NewGuid().ToString("N");
            var connectionBuilder = new NpgsqlConnectionStringBuilder(options.ConnectionString)
            {
                ApplicationName = applicationName
            };
            var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("ProcessPath unavailable");
            var hostPrefix = string.Equals(Path.GetFileName(processPath), "dotnet", StringComparison.OrdinalIgnoreCase)
                ? $"\"{Environment.GetCommandLineArgs()[0]}\" "
                : string.Empty;
            using var child = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = processPath,
                    Arguments = $"{hostPrefix}\"{connectionBuilder.ConnectionString}\" {options.Schema} {scenario.Scenario}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            child.Start();
            try
            {
                using var readyTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                var stderrTask = child.StandardError.ReadToEndAsync(readyTimeout.Token);
                string? identity = null;
                while (await child.StandardOutput.ReadLineAsync(readyTimeout.Token) is { } line)
                {
                    if (line.StartsWith(scenario.Sentinel + " ", StringComparison.Ordinal))
                    {
                        identity = line[(scenario.Sentinel.Length + 1)..];
                        break;
                    }
                }

                if (identity is null)
                {
                    var stderr = await stderrTask;
                    throw new InvalidOperationException($"[{scenario.Scenario}] Crash worker produced no sentinel. Stderr: {stderr}");
                }

                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync();
                await WaitForBackendExitAsync(options.ConnectionString, applicationName);
                var fields = identity.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                await using (var recovery = BuildProvider(options))
                {
                    switch (scenario.Scenario)
                    {
                        case "orchestration-crash-handoff-accept":
                            await RecoverHandoffAcceptCrashAsync(recovery, fields);
                            break;
                        case "orchestration-crash-event-resume":
                            await RecoverEventResumeCrashAsync(recovery, fields);
                            break;
                        case "orchestration-crash-parallel-claim":
                            await RecoverParallelClaimCrashAsync(recovery, fields);
                            break;
                    }
                }

                Console.WriteLine(scenario.OutputSentinel);
            }
            finally
            {
                if (!child.HasExited)
                {
                    child.Kill(entireProcessTree: true);
                    await child.WaitForExitAsync();
                }
            }
        }

        Console.WriteLine("CRESTCREATES_AGENT_ORCHESTRATION_CRASH_WINDOWS_OK");
    }

    internal static async Task<int> RunCrashChildAsync(PostgreSqlRuntimePersistenceOptions options, string scenario)
    {
        switch (scenario)
        {
            case "orchestration-crash-handoff-accept":
                await RunHandoffAcceptCrashChildAsync(options);
                break;
            case "orchestration-crash-event-resume":
                await RunEventResumeCrashChildAsync(options);
                break;
            case "orchestration-crash-parallel-claim":
                await RunParallelClaimCrashChildAsync(options);
                break;
            default:
                Console.Error.WriteLine($"Unknown orchestration crash scenario '{scenario}'.");
                return 2;
        }

        return 0;
    }

    private static async Task RunHandoffAcceptCrashChildAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        await using var provider = BuildProvider(options);
        var coordinator = Coordinator(provider);
        var key = new AgentExecutionKey("native-agent-tenant", Guid.NewGuid());
        var original = await CreateAsync(coordinator, key, new(100, 2), []);
        var offer = new AgentHandoffOffer(Guid.NewGuid(), "recipient", [], []);
        await coordinator.OfferHandoffAsync(key, original, offer);
        await coordinator.AcceptHandoffAsync(key, original.RunId, offer.HandoffId, offer.RecipientAgentId);
        Console.WriteLine($"CRESTCREATES_AGENT_CRASH_HANDOFF_ACCEPT_COMMITTED {original.RunId:N} {offer.HandoffId:N}");
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }

    private static async Task RunEventResumeCrashChildAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        await using var provider = BuildProvider(options);
        var coordinator = Coordinator(provider);
        var key = new AgentExecutionKey("native-agent-tenant", Guid.NewGuid());
        var owner = await CreateAsync(coordinator, key);
        var waitId = Guid.NewGuid();
        var selector = CrashSelector;
        await coordinator.WaitForEventAsync(key, owner, new(waitId, selector));
        await coordinator.ResumeAsync(key, owner.RunId, waitId, CrashDelivery(key.TenantId));
        Console.WriteLine($"CRESTCREATES_AGENT_CRASH_EVENT_RESUME_COMMITTED {owner.RunId:N} {waitId:N}");
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }

    private static async Task RunParallelClaimCrashChildAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        await using var provider = BuildProvider(options);
        var coordinator = Coordinator(provider);
        var key = new AgentExecutionKey("native-agent-tenant", Guid.NewGuid());
        var owner = await CreateAsync(coordinator, key, new(100, 2), []);
        var childId = Guid.NewGuid();
        var worker = new CrashDispatchWorker();
        var team = new AgentTeamRunner(coordinator, [new AgentTeamMember("worker", [], worker)]);
        await team.RunParallelAsync(key, owner, [CrashParallelAssignment(childId)]);
    }

    private static async Task RecoverHandoffAcceptCrashAsync(IServiceProvider provider, string[] fields)
    {
        var rootId = Guid.Parse(fields[0]);
        var handoffId = Guid.Parse(fields[1]);
        var key = new AgentExecutionKey("native-agent-tenant", rootId);
        var coordinator = Coordinator(provider);
        var store = provider.GetRequiredService<IAgentExecutionStore>();
        var root = (await store.FindAsync(key, default))!.Runs[rootId];
        var accepted = root.Owner;
        var original = new AgentRunOwner(rootId, "discovery", 1);
        if (accepted != new AgentRunOwner(rootId, "recipient", 2) || root.PendingHandoff is not null ||
            AssertSingle(root.Handoffs).AcceptedOwner != accepted)
        {
            throw new InvalidOperationException("The committed handoff acceptance did not survive the crash.");
        }

        await RequireDenial(async () =>
        {
            await coordinator.AcceptHandoffAsync(key, rootId, handoffId, "recipient");
            return false;
        });
        await RequireDenial(() => coordinator.ReserveAsync(key, original, Guid.NewGuid(), new(1, 0)));
        await coordinator.FinishAsync(key, accepted, new(AgentResultKind.Returned, "HandoffRecovered"));
    }

    private static async Task RecoverEventResumeCrashAsync(IServiceProvider provider, string[] fields)
    {
        var rootId = Guid.Parse(fields[0]);
        var waitId = Guid.Parse(fields[1]);
        var key = new AgentExecutionKey("native-agent-tenant", rootId);
        var owner = new AgentRunOwner(rootId, "discovery", 1);
        var coordinator = Coordinator(provider);
        var context = await coordinator.ReadContextAsync(key, owner);
        if (context.ResumedBy != CrashDelivery(key.TenantId) ||
            await coordinator.ResumeAsync(key, rootId, waitId, CrashDelivery(key.TenantId)))
        {
            throw new InvalidOperationException("The committed event resumption did not replay exactly once after the crash.");
        }

        var worker = new ScriptedWorker(Output(rootId), current => Assert.Equal(CrashDelivery(key.TenantId), current.ResumedBy));
        var result = await worker.RunAsync(context, default);
        await coordinator.FinishAsync(key, owner, result);
        if ((await coordinator.ReadRunAsync(key, owner)).Resumptions.Length != 1 || worker.Calls != 1)
        {
            throw new InvalidOperationException("The recovered event resumption lost its durable receipt.");
        }
    }

    private static async Task RecoverParallelClaimCrashAsync(IServiceProvider provider, string[] fields)
    {
        var rootId = Guid.Parse(fields[0]);
        var childId = Guid.Parse(fields[1]);
        var key = new AgentExecutionKey("native-agent-tenant", rootId);
        var owner = new AgentRunOwner(rootId, "discovery", 1);
        var child = new AgentRunOwner(childId, "worker", 1);
        var coordinator = Coordinator(provider);
        var worker = new ScriptedWorker(Output(childId));
        var team = new AgentTeamRunner(coordinator, [new AgentTeamMember("worker", [], worker)]);
        try
        {
            await team.RunParallelAsync(key, owner, [CrashParallelAssignment(childId)]);
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("requires reconciliation", StringComparison.Ordinal))
        {
        }

        var store = provider.GetRequiredService<IAgentExecutionStore>();
        var claimedSnapshot = (await store.FindAsync(key, default))!;
        var durableChild = claimedSnapshot.Runs[childId];
        if (!durableChild.TeamDispatchClaimed || durableChild.Result is not null || worker.Calls != 0)
        {
            throw new InvalidOperationException("The crashed Parallel claim was not recovered fail-closed.");
        }

        var rejectedSnapshot = (await store.FindAsync(key, default))!;
        if (rejectedSnapshot.Revision != claimedSnapshot.Revision)
        {
            throw new InvalidOperationException("The fail-closed Parallel retry changed durable execution state.");
        }

        var confirmed = new AgentRunResult(AgentResultKind.Returned, "ClaimRecovered", Output(childId));
        await coordinator.FinishAsync(key, child, confirmed);
        var confirmedSnapshot = (await store.FindAsync(key, default))!;
        if (confirmedSnapshot.Revision <= claimedSnapshot.Revision || confirmedSnapshot.Runs[childId].Result != confirmed)
            throw new InvalidOperationException("The reconciled Parallel result did not advance durable execution state.");
        var confirmedRevision = confirmedSnapshot.Revision;
        var replay = AssertSingle((await team.RunParallelAsync(key, owner, [CrashParallelAssignment(childId)])).Steps);
        var replayedSnapshot = (await store.FindAsync(key, default))!;
        if (replay.Result != confirmed || worker.Calls != 0 || replayedSnapshot.Revision != confirmedRevision)
        {
            throw new InvalidOperationException("The reconciled Parallel claim did not replay without redispatch or child writes.");
        }

        await coordinator.FinishAsync(key, owner, new(AgentResultKind.Returned, "ParallelRecovered"));
    }

    private static AgentEventSelector CrashSelector { get; } = new("human-task", "approved", "crash-subject");

    private static AgentEventDelivery CrashDelivery(string? tenantId) =>
        new("crash-approval", tenantId, CrashSelector, new("crash-approval-doc", "crash-approval-hash"));

    private static AgentRunAssignment CrashParallelAssignment(Guid childId) =>
        Assignment(childId, "worker", "Crash after claim", new(20, 1), []);

    private static async Task WaitForBackendExitAsync(string connectionString, string applicationName)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "select count(*) from pg_stat_activity where application_name=@application;", connection);
            command.Parameters.AddWithValue("application", applicationName);
            if ((long)(await command.ExecuteScalarAsync())! == 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException("The orchestration crash worker PostgreSQL backend did not exit.");
    }

    private static async Task RunOrchestrationNegativeControlsAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        string[] controls =
        [
            "old-epoch", "cross-tenant-event", "duplicate-dispatch", "late-result",
            "invalid-json-identity", "invalid-json-revision", "unknown-selector"
        ];
        foreach (var control in controls)
        {
            var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("ProcessPath unavailable");
            var hostPrefix = string.Equals(Path.GetFileName(processPath), "dotnet", StringComparison.OrdinalIgnoreCase)
                ? $"\"{Environment.GetCommandLineArgs()[0]}\" "
                : string.Empty;
            var startInfo = new ProcessStartInfo
            {
                FileName = processPath,
                Arguments = $"{hostPrefix}\"{options.ConnectionString}\" {options.Schema} orchestration-negative {control}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var child = Process.Start(startInfo) ?? throw new InvalidOperationException("Negative control process did not start.");
            try
            {
                var stdoutTask = child.StandardOutput.ReadToEndAsync();
                var stderrTask = child.StandardError.ReadToEndAsync();
                using var exitTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                await child.WaitForExitAsync(exitTimeout.Token);
                var stdout = await stdoutTask;
                var stderr = await stderrTask;
                if (child.ExitCode == 0 || !stdout.Contains($"CRESTCREATES_AGENT_ORCHESTRATION_NEGATIVE_CAUGHT {control}:", StringComparison.Ordinal) ||
                    stdout.Contains("CRESTCREATES_AGENT_ORCHESTRATION_NATIVE_OK", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"[{control}] Negative control exit={child.ExitCode}; stdout={stdout}; stderr={stderr}");
                }

                Console.WriteLine($"CRESTCREATES_AGENT_ORCHESTRATION_NEGATIVE_{control.Replace('-', '_').ToUpperInvariant()}_OK");
            }
            finally
            {
                if (!child.HasExited)
                {
                    child.Kill(entireProcessTree: true);
                    await child.WaitForExitAsync();
                }
            }
        }

        Console.WriteLine("CRESTCREATES_AGENT_ORCHESTRATION_NEGATIVE_CONTROLS_OK");
    }

    internal static async Task<int> RunNegativeControlAsync(PostgreSqlRuntimePersistenceOptions options, string control)
    {
        try
        {
            switch (control)
            {
                case "old-epoch":
                    await RunOldEpochNegativeAsync(options);
                    break;
                case "cross-tenant-event":
                    await RunCrossTenantEventNegativeAsync(options);
                    break;
                case "duplicate-dispatch":
                    await RunDuplicateDispatchNegativeAsync(options);
                    break;
                case "late-result":
                    await RunLateResultNegativeAsync(options);
                    break;
                case "invalid-json-identity":
                    await RunPersistedSnapshotNegativeAsync(options, corruptIdentity: true);
                    break;
                case "invalid-json-revision":
                    await RunPersistedSnapshotNegativeAsync(options, corruptIdentity: false);
                    break;
                case "unknown-selector":
                    await RunUnknownSelectorNegativeAsync(options);
                    break;
                default:
                    Console.Error.WriteLine($"Unknown orchestration negative control '{control}'.");
                    return 2;
            }
        }
        catch (ExpectedNegativeControlException exception)
        {
            Console.WriteLine($"CRESTCREATES_AGENT_ORCHESTRATION_NEGATIVE_CAUGHT {control}: {exception.Message}");
            return 31;
        }
        catch (OperationCanceledException exception) when (control == "late-result")
        {
            Console.WriteLine($"CRESTCREATES_AGENT_ORCHESTRATION_NEGATIVE_CAUGHT {control}: {exception.Message}");
            return 31;
        }

        Console.WriteLine($"CRESTCREATES_AGENT_ORCHESTRATION_NEGATIVE_NOT_DETECTED {control}");
        return 32;
    }

    private static async Task RunOldEpochNegativeAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        await using var provider = BuildProvider(options);
        var coordinator = Coordinator(provider);
        var key = new AgentExecutionKey("native-agent-tenant", Guid.NewGuid());
        var original = await CreateAsync(coordinator, key, new(100, 2), []);
        var offer = new AgentHandoffOffer(Guid.NewGuid(), "recipient", [], []);
        await coordinator.OfferHandoffAsync(key, original, offer);
        await coordinator.AcceptHandoffAsync(key, original.RunId, offer.HandoffId, offer.RecipientAgentId);
        var before = (await provider.GetRequiredService<IAgentExecutionStore>().FindAsync(key, default))!;
        await RequireFailure<InvalidOperationException>(
            async () => await coordinator.ReserveAsync(key, original, Guid.NewGuid(), new(1, 0)),
            "The prior ownership epoch was accepted after handoff.");
        var after = (await provider.GetRequiredService<IAgentExecutionStore>().FindAsync(key, default))!;
        if (after.Revision != before.Revision || after.Runs[original.RunId].Reservations.Count != 0)
            throw new InvalidOperationException("The old-epoch attempt changed durable state.");
    }

    private static async Task RunCrossTenantEventNegativeAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        await using var provider = BuildProvider(options);
        var coordinator = Coordinator(provider);
        var key = new AgentExecutionKey("native-agent-tenant", Guid.NewGuid());
        var owner = await CreateAsync(coordinator, key);
        var waitId = Guid.NewGuid();
        var selector = new AgentEventSelector("human-task", "approved", key.RootRunId.ToString("N"));
        await coordinator.WaitForEventAsync(key, owner, new(waitId, selector));
        await RequireFailure<InvalidOperationException>(
            async () => await coordinator.ResumeAsync(key, owner.RunId, waitId,
                new("approval", "other-tenant", selector, new("approval-doc", "approval-hash"))),
            "A cross-tenant event was accepted.");
        if ((await coordinator.ReadRunAsync(key, owner)).Resumptions.Length != 0)
            throw new InvalidOperationException("A cross-tenant event became durable.");
    }

    private static async Task RunDuplicateDispatchNegativeAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        await using var provider = BuildProvider(options);
        var coordinator = Coordinator(provider);
        var key = new AgentExecutionKey("native-agent-tenant", Guid.NewGuid());
        var owner = await CreateAsync(coordinator, key, new(100, 2), []);
        var assignment = Assignment(Guid.NewGuid(), "worker", "Work", new(20, 1), []);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstWorker = new BlockingWorker(entered, release, Output(assignment.RunId));
        var duplicateWorker = new ScriptedWorker(Output(assignment.RunId));
        var first = new AgentTeamRunner(coordinator, [new AgentTeamMember("worker", [], firstWorker)])
            .RunParallelAsync(key, owner, [assignment]).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var store = provider.GetRequiredService<IAgentExecutionStore>();
        var beforeDuplicate = (await store.FindAsync(key, default))!;
        try
        {
            try
            {
                await new AgentTeamRunner(coordinator, [new AgentTeamMember("worker", [], duplicateWorker)])
                    .RunParallelAsync(key, owner, [assignment]);
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("requires reconciliation", StringComparison.Ordinal))
            {
                var afterDuplicate = (await store.FindAsync(key, default))!;
                if (afterDuplicate.Revision != beforeDuplicate.Revision)
                    throw new InvalidOperationException("A rejected duplicate dispatch changed durable state.");
                throw new ExpectedNegativeControlException("A concurrent duplicate team dispatch was rejected.", exception);
            }
        }
        finally
        {
            release.TrySetResult();
            await first;
        }

        if (duplicateWorker.Calls != 0)
        {
            throw new InvalidOperationException("A concurrent duplicate team dispatch reached the worker.");
        }

        throw new InvalidOperationException("A concurrent duplicate team dispatch was not rejected.");
    }

    private static async Task RunLateResultNegativeAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        await using var provider = BuildProvider(options);
        var coordinator = Coordinator(provider);
        var key = new AgentExecutionKey("native-agent-tenant", Guid.NewGuid());
        var owner = await coordinator.StartAsync(key, "engineer", TimeProvider.System.GetUtcNow().AddSeconds(3),
            new(key.RootRunId, "discovery", "Late result", new(100, 2), [], []));
        var assignment = Assignment(Guid.NewGuid(), "worker", "Wait past deadline", new(20, 1), []);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var team = new AgentTeamRunner(coordinator, [new AgentTeamMember("worker", [], new LateWorker(entered))]);
        var pending = team.RunParallelAsync(key, owner, [assignment]).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await pending;
        }
        catch (OperationCanceledException)
        {
            if (AssertSingle(await coordinator.ReadChildResultsAsync(key, owner, [assignment.RunId])) is not null)
            {
                throw new InvalidOperationException("A late worker result became durable.");
            }

            throw;
        }

        throw new InvalidOperationException("A worker result after its deadline was accepted.");
    }

    private static async Task RunPersistedSnapshotNegativeAsync(PostgreSqlRuntimePersistenceOptions options, bool corruptIdentity)
    {
        await using var provider = BuildProvider(options);
        var coordinator = Coordinator(provider);
        var key = new AgentExecutionKey("native-agent-tenant", Guid.NewGuid());
        await CreateAsync(coordinator, key);
        var table = $"\"{options.Schema.Replace("\"", "\"\"")}\".agent_executions";
        await using var command = provider.GetRequiredService<NpgsqlDataSource>().CreateCommand(corruptIdentity
            ? $"update {table} set state_json = to_jsonb(jsonb_set((state_json #>> '{{}}')::jsonb, '{{key,tenantId}}', @tenant::jsonb)::text) where root_run_id = @root;"
            : $"update {table} set revision = revision + 1 where root_run_id = @root;");
        command.Parameters.AddWithValue("root", key.RootRunId);
        if (corruptIdentity)
        {
            command.Parameters.AddWithValue("tenant", "\"other-tenant\"");
        }

        if (await command.ExecuteNonQueryAsync() != 1)
        {
            throw new InvalidOperationException("The persisted snapshot mutation did not affect one row.");
        }

        await RequireFailure<RuntimePersistenceContractException>(
            async () => await provider.GetRequiredService<IAgentExecutionStore>().FindAsync(key, default),
            "A persisted snapshot with a mismatched identity or revision was accepted.");
    }

    private static async Task RunUnknownSelectorNegativeAsync(PostgreSqlRuntimePersistenceOptions options)
    {
        await using var provider = BuildProvider(options);
        var coordinator = Coordinator(provider);
        var key = new AgentExecutionKey("native-agent-tenant", Guid.NewGuid());
        var owner = await CreateAsync(coordinator, key);
        var before = (await provider.GetRequiredService<IAgentExecutionStore>().FindAsync(key, default))!;
        await RequireFailure<ArgumentException>(
            async () => await coordinator.WaitForEventAsync(key, owner,
                new(Guid.NewGuid(), new AgentEventSelector("", "approved", "subject"))),
            "An unknown event selector was accepted.");
        if ((await provider.GetRequiredService<IAgentExecutionStore>().FindAsync(key, default))!.Revision != before.Revision)
            throw new InvalidOperationException("An unknown selector changed durable state.");
    }

    private static async Task RequireFailure<TException>(Func<Task> action, string message) where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            throw new ExpectedNegativeControlException(message, exception);
        }

        throw new InvalidOperationException(message);
    }

    private sealed class ExpectedNegativeControlException(string message, Exception? innerException = null)
        : InvalidOperationException(message, innerException);

    private static ServiceProvider BuildProvider(PostgreSqlRuntimePersistenceOptions options) =>
        new ServiceCollection().AddCrestCreatesPostgreSqlRuntimePersistence(options).BuildServiceProvider();

    private static AgentRunCoordinator Coordinator(IServiceProvider provider) =>
        new(provider.GetRequiredService<IAgentExecutionStore>(), TimeProvider.System);

    private static AgentRunAssignment Assignment(
        Guid runId, string agentId, string task, AgentBudget budget, ImmutableArray<string> tools,
        ImmutableArray<AgentArtifactReference> inputs = default) =>
        new(runId, agentId, task, budget, tools, inputs.IsDefault ? [] : inputs);

    private static AgentArtifactReference Output(Guid runId) =>
        new($"native-output-{runId:N}", $"native-hash-{runId:N}");

    private static ValueTask<AgentRunOwner> CreateAsync(
        AgentRunCoordinator coordinator, AgentExecutionKey key, AgentBudget? budget = null,
        ImmutableArray<string> tools = default, ImmutableArray<AgentArtifactReference> inputs = default) =>
        coordinator.StartAsync(key, "engineer", TimeProvider.System.GetUtcNow().AddMinutes(2),
            new(key.RootRunId, "discovery", "Find MES evidence", budget ?? new AgentBudget(100, 5),
                tools.IsDefault ? ["mes.read"] : tools, inputs.IsDefault ? [] : inputs));

    private static T AssertSingle<T>(IReadOnlyList<T> values) => values.Count == 1 ? values[0] : throw new InvalidOperationException("Expected one value.");

    private static class Assert
    {
        internal static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException("Native scenario values did not match.");
            }
        }

        internal static void Empty<T>(IEnumerable<T> values)
        {
            if (values.Any())
            {
                throw new InvalidOperationException("Native scenario expected an empty collection.");
            }
        }

        internal static void Contains<T>(T expected, IEnumerable<T> values)
        {
            if (!values.Contains(expected, EqualityComparer<T>.Default))
            {
                throw new InvalidOperationException("Native scenario expected an explicit collection member.");
            }
        }
    }

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

    private sealed class ScriptedWorker(AgentArtifactReference output, Action<AgentRunContext>? inspect = null) : IAgentRunWorker
    {
        private int _calls;

        internal int Calls => Volatile.Read(ref _calls);
        internal ConcurrentQueue<AgentRunContext> Contexts { get; } = new();

        public ValueTask<AgentRunResult> RunAsync(AgentRunContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Contexts.Enqueue(context);
            inspect?.Invoke(context);
            return ValueTask.FromResult(new AgentRunResult(AgentResultKind.Returned, "Ready", output));
        }
    }

    private sealed class BarrierWorker(ParallelBarrier barrier, AgentArtifactReference expectedInput) : IAgentRunWorker
    {
        public async ValueTask<AgentRunResult> RunAsync(AgentRunContext context, CancellationToken cancellationToken)
        {
            if (!context.Inputs.SequenceEqual([expectedInput]))
            {
                throw new InvalidOperationException("A parallel worker observed another child's input scope.");
            }

            await barrier.ArriveAsync(cancellationToken);
            return new(AgentResultKind.Returned, "Ready", Output(context.Owner.RunId));
        }
    }

    private sealed class ParallelBarrier
    {
        private readonly TaskCompletionSource _bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;
        private int _active;
        private int _maximumActive;

        internal int MaximumActive => Volatile.Read(ref _maximumActive);

        internal async ValueTask ArriveAsync(CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active);
            int maximum;
            while (true)
            {
                maximum = Volatile.Read(ref _maximumActive);
                if (active <= maximum || Interlocked.CompareExchange(ref _maximumActive, active, maximum) == maximum)
                {
                    break;
                }
            }

            try
            {
                if (Interlocked.Increment(ref _arrived) == 2)
                {
                    _bothArrived.TrySetResult();
                }

                await _bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private sealed class CrashDispatchWorker : IAgentRunWorker
    {
        public async ValueTask<AgentRunResult> RunAsync(AgentRunContext context, CancellationToken cancellationToken)
        {
            Console.WriteLine($"CRESTCREATES_AGENT_CRASH_PARALLEL_CLAIM_COMMITTED {context.Execution.RootRunId:N} {context.Owner.RunId:N}");
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The crash worker was not terminated.");
        }
    }

    private sealed class BlockingWorker(
        TaskCompletionSource entered, TaskCompletionSource release, AgentArtifactReference output) : IAgentRunWorker
    {
        public async ValueTask<AgentRunResult> RunAsync(AgentRunContext context, CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return new(AgentResultKind.Returned, "Ready", output);
        }
    }

    private sealed class LateWorker(TaskCompletionSource entered) : IAgentRunWorker
    {
        public async ValueTask<AgentRunResult> RunAsync(AgentRunContext context, CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new(AgentResultKind.Returned, "TooLate", Output(context.Owner.RunId));
            }

            throw new InvalidOperationException("The late-result worker was not cancelled.");
        }
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
