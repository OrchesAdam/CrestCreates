using System.Collections.Immutable;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrestCreates.Agent.Orchestration;
using CrestCreates.Sample.AssetManagement.MicrosoftAgent;
using Microsoft.Extensions.AI;
using Xunit;

namespace CrestCreates.Sample.AssetManagement.MicrosoftAgent.Tests;

[Collection(AgentLiveEvaluationCollection.Name)]
public sealed class AgentCoordinationLiveEvaluationTests
{
    [AgentLiveEvalFact]
    public Task ReAct_UsesRealModelToolLoopAndSeparatesAgentCompletion() =>
        RunAsync("react", async (evaluation, token) =>
        {
            var root = await StartRootAsync(evaluation, "engineer",
                "Call mes.read exactly once with {\"workOrderId\":\"WO-LIVE-7\"}, then return ReportReady with text containing MES_PASS.",
                new(100000, 3), ["mes.read"], [], token);
            var tools = new LiveToolExecutor();
            var result = await new AgentReActRunner(evaluation.Coordinator, TimeProvider.System).RunAsync(
                evaluation.Key, root, new MicrosoftAgentReActModelExecutor(evaluation.Model), tools, 3,
                evaluation.Model.MaxOutputTokens, token);

            Assert.Equal(AgentResultKind.Returned, result.Kind);
            Assert.Equal("ReportReady", result.Code);
            Assert.NotNull(result.Output);
            Assert.Equal(1, tools.Calls);
            Assert.Equal("mes.read", tools.Selection!.Name);
            Assert.Equal("WO-LIVE-7", tools.Selection.Arguments!.Value.GetProperty("workOrderId").GetString());
            Assert.Equal(2, evaluation.ModelCalls.Count);
            Assert.Contains("MES_PASS", evaluation.ModelCalls[1].Messages.Single(message => message.Role == "user").Text);
            var snapshot = (await evaluation.Store.FindAsync(evaluation.Key, default))!;
            var run = snapshot.Runs[root.RunId];
            Assert.Equal(3, run.Reservations.Count);
            Assert.All(run.Reservations.Values, receipt => Assert.NotNull(receipt.Used));
            evaluation.Facts["toolCalls"] = tools.Calls;
            evaluation.Facts["result"] = result;
        });

    [AgentLiveEvalFact]
    public Task PlanAndExecute_UsesRealModelPlanAndVersionedChildRuns() =>
        RunAsync("plan", async (evaluation, token) =>
        {
            var root = await StartRootAsync(evaluation, "planner",
                "Create and execute a two-step asset then quality review plan.", new(100000, 10), [], [], token);
            var templates = new[]
            {
                new AgentRunAssignment(Guid.NewGuid(), "asset", "Inspect asset metadata", new(30000, 1), [], []),
                new AgentRunAssignment(Guid.NewGuid(), "quality", "Review asset findings", new(30000, 1), [], [])
            };
            var context = await evaluation.Coordinator.ReadContextAsync(evaluation.Key, root, token);
            var completion = await LiveModelProtocol.CompleteMeteredAsync(evaluation.Model, evaluation.Coordinator, context,
                "plan", "You are a CrestCreates planner. Return strict JSON only.",
                string.Join("\n",
                    "Create exactly two ordered steps for agents asset then quality.",
                    "Return {\"steps\":[{\"index\":0,\"task\":\"...\"},{\"index\":1,\"task\":\"...\"}]}.",
                    "The asset task must request the marker ASSET_PLAN. The quality task must request QUALITY_PLAN."), token);
            var assignments = LiveModelProtocol.ReadPlan(completion.Text, templates);
            var plan = await evaluation.Coordinator.SetPlanAsync(evaluation.Key, root, 0, [.. assignments], token);
            var team = new AgentTeamRunner(evaluation.Coordinator,
            [
                new AgentTeamMember("asset", [], Worker(evaluation, "You are an asset reviewer. Return strict JSON.")),
                new AgentTeamMember("quality", [], Worker(evaluation, "You are a quality reviewer. Return strict JSON."))
            ]);

            var result = await team.RunPlanAsync(evaluation.Key, root, plan.Version, token);

            Assert.True(result.AllReturned);
            Assert.Equal(3, evaluation.ModelCalls.Count);
            var snapshot = (await evaluation.Store.FindAsync(evaluation.Key, default))!;
            var first = snapshot.Runs[assignments[0].RunId];
            var second = snapshot.Runs[assignments[1].RunId];
            Assert.Equal(root.RunId, first.ChildCall!.ParentRunId);
            Assert.Equal(root.RunId, second.ChildCall!.ParentRunId);
            Assert.Equal(first.Result!.Output, Assert.Single(second.Inputs));
            Assert.Contains(evaluation.Artifacts[first.Result.Output!.Id],
                evaluation.ModelCalls[2].Messages.Single(message => message.Role == "user").Text);
            Assert.Null(snapshot.Runs[root.RunId].Result);
            await evaluation.Coordinator.FinishAsync(evaluation.Key, root, new(AgentResultKind.Returned, "PlanCompleted"), token);
            evaluation.Facts["planVersion"] = plan.Version;
            evaluation.Facts["childRunIds"] = assignments.Select(step => step.RunId).ToArray();
            evaluation.Facts["result"] = result;
        });

    [AgentLiveEvalFact]
    public Task Sequential_PassesOnlyExplicitArtifactBetweenRealModelWorkers() =>
        RunAsync("sequential", async (evaluation, token) =>
        {
            var root = await StartRootAsync(evaluation, "lead",
                "Run asset review followed by quality review.", new(100000, 6), [], [], token);
            var assignments = new[]
            {
                new AgentRunAssignment(Guid.NewGuid(), "asset", "Return a concise asset finding containing ASSET_SEQUENTIAL.", new(30000, 1), [], []),
                new AgentRunAssignment(Guid.NewGuid(), "quality", "Review the explicit asset finding and return QUALITY_SEQUENTIAL.", new(30000, 1), [], [])
            };
            var team = new AgentTeamRunner(evaluation.Coordinator,
            [
                new AgentTeamMember("asset", [], Worker(evaluation, "You are an asset reviewer. Return strict JSON.")),
                new AgentTeamMember("quality", [], Worker(evaluation, "You are a quality reviewer. Return strict JSON."))
            ]);

            var result = await team.RunSequentialAsync(evaluation.Key, root, [.. assignments], token);

            Assert.True(result.AllReturned);
            Assert.Equal(2, evaluation.ModelCalls.Count);
            var snapshot = (await evaluation.Store.FindAsync(evaluation.Key, default))!;
            var first = snapshot.Runs[assignments[0].RunId];
            var second = snapshot.Runs[assignments[1].RunId];
            Assert.Empty(first.Inputs);
            Assert.Equal(first.Result!.Output, Assert.Single(second.Inputs));
            Assert.Contains(evaluation.Artifacts[first.Result.Output!.Id],
                evaluation.ModelCalls[1].Messages.Single(message => message.Role == "user").Text);
            await evaluation.Coordinator.FinishAsync(evaluation.Key, root, new(AgentResultKind.Returned, "SequentialCompleted"), token);
            evaluation.Facts["childRunIds"] = assignments.Select(step => step.RunId).ToArray();
            evaluation.Facts["result"] = result;
        });

    [AgentLiveEvalFact]
    public Task Parallel_RunsRealModelWorkersConcurrentlyWithIsolatedScopes() =>
        RunAsync("parallel", async (evaluation, token) =>
        {
            evaluation.EnableConcurrencyBarrier(2);
            var assetInput = LiveModelProtocol.Artifact("asset-input", "ASSET_PRIVATE_INPUT");
            var mesInput = LiveModelProtocol.Artifact("mes-input", "MES_PRIVATE_INPUT");
            evaluation.Artifacts[assetInput.Id] = "ASSET_PRIVATE_INPUT";
            evaluation.Artifacts[mesInput.Id] = "MES_PRIVATE_INPUT";
            var root = await StartRootAsync(evaluation, "lead",
                "Run independent asset and MES reviews in parallel.", new(100000, 8),
                ["asset.read", "mes.read"], [assetInput, mesInput], token);
            var assignments = new[]
            {
                new AgentRunAssignment(Guid.NewGuid(), "asset", "Review only the asset input and return ASSET_PARALLEL.", new(30000, 1), ["asset.read"], [assetInput]),
                new AgentRunAssignment(Guid.NewGuid(), "mes", "Review only the MES input and return MES_PARALLEL.", new(30000, 1), ["mes.read"], [mesInput])
            };
            var team = new AgentTeamRunner(evaluation.Coordinator,
            [
                new AgentTeamMember("asset", ["asset.read"], Worker(evaluation, "You are an asset reviewer. Return strict JSON.")),
                new AgentTeamMember("mes", ["mes.read"], Worker(evaluation, "You are a MES reviewer. Return strict JSON."))
            ]);

            var result = await team.RunParallelAsync(evaluation.Key, root, [.. assignments], token);

            Assert.True(result.AllReturned);
            Assert.Equal(new[] { "asset", "mes" }, result.Steps.Select(step => step.AgentId).ToArray());
            Assert.True(evaluation.MaxConcurrentHttpRequests >= 2);
            Assert.Equal(2, evaluation.ModelCalls.Count);
            var prompts = evaluation.ModelCalls.Select(call => call.Messages.Single(message => message.Role == "user").Text).ToArray();
            Assert.Contains(prompts, prompt => prompt.Contains("ASSET_PRIVATE_INPUT", StringComparison.Ordinal) &&
                !prompt.Contains("MES_PRIVATE_INPUT", StringComparison.Ordinal));
            Assert.Contains(prompts, prompt => prompt.Contains("MES_PRIVATE_INPUT", StringComparison.Ordinal) &&
                !prompt.Contains("ASSET_PRIVATE_INPUT", StringComparison.Ordinal));
            var snapshot = (await evaluation.Store.FindAsync(evaluation.Key, default))!;
            Assert.Equal(assetInput, Assert.Single(snapshot.Runs[assignments[0].RunId].Inputs));
            Assert.Equal(mesInput, Assert.Single(snapshot.Runs[assignments[1].RunId].Inputs));
            Assert.Null(snapshot.Runs[root.RunId].Result);
            await evaluation.Coordinator.FinishAsync(evaluation.Key, root, new(AgentResultKind.Returned, "ParallelCompleted"), token);
            evaluation.Facts["maxConcurrentHttpRequests"] = evaluation.MaxConcurrentHttpRequests;
            evaluation.Facts["childRunIds"] = assignments.Select(step => step.RunId).ToArray();
            evaluation.Facts["result"] = result;
        });

    [AgentLiveEvalFact]
    public Task Supervisor_DelegatesToDifferentRealModelWorker() =>
        RunAsync("supervisor", async (evaluation, token) =>
        {
            var root = await StartRootAsync(evaluation, "supervisor",
                "Delegate the quality review to the quality specialist.", new(100000, 6), [], [], token);
            var context = await evaluation.Coordinator.ReadContextAsync(evaluation.Key, root, token);
            var completion = await LiveModelProtocol.CompleteMeteredAsync(evaluation.Model, evaluation.Coordinator, context,
                "delegate", "You are a CrestCreates supervisor. Return strict JSON only.",
                string.Join("\n",
                    "Available member: quality.",
                    "Return exactly {\"member\":\"quality\",\"task\":\"Review the proposed maintenance release and return QUALITY_DELEGATED.\"}"), token);
            var (member, task) = LiveModelProtocol.ReadDelegation(completion.Text);
            var assignment = new AgentRunAssignment(Guid.NewGuid(), member, task, new(30000, 1), [], []);
            var team = new AgentTeamRunner(evaluation.Coordinator,
            [
                new AgentTeamMember("supervisor", [], Worker(evaluation, "You are a supervisor.")),
                new AgentTeamMember("quality", [], Worker(evaluation, "You are a quality specialist. Return strict JSON."))
            ]);

            var result = await team.DelegateAsync(evaluation.Key, root, [assignment], token);

            Assert.True(result.AllReturned);
            Assert.NotEqual(root.AgentId, member);
            Assert.Equal(2, evaluation.ModelCalls.Count);
            var snapshot = (await evaluation.Store.FindAsync(evaluation.Key, default))!;
            Assert.Equal(root.RunId, snapshot.Runs[assignment.RunId].ChildCall!.ParentRunId);
            Assert.Null(snapshot.Runs[root.RunId].Result);
            await evaluation.Coordinator.FinishAsync(evaluation.Key, root, new(AgentResultKind.Returned, "SupervisionCompleted"), token);
            evaluation.Facts["supervisorAgentId"] = root.AgentId;
            evaluation.Facts["delegatedAgentId"] = member;
            evaluation.Facts["childRunId"] = assignment.RunId;
            evaluation.Facts["result"] = result;
        });

    [AgentLiveEvalFact]
    public Task Handoff_TransfersControlWithEpochFencingAndIsolatedContext() =>
        RunAsync("handoff", async (evaluation, token) =>
        {
            var privateInput = LiveModelProtocol.Artifact("triage-private", "TRIAGE_PRIVATE_INPUT");
            var summaryArtifact = LiveModelProtocol.Artifact("handoff-summary", "pending-summary");
            evaluation.Artifacts[privateInput.Id] = "TRIAGE_PRIVATE_INPUT";
            evaluation.Artifacts[summaryArtifact.Id] = "pending-summary";
            var root = await StartRootAsync(evaluation, "triage",
                "Triage the asset and hand off to quality with a concise summary.", new(100000, 6),
                ["asset.read"], [privateInput, summaryArtifact], token);
            var context = await evaluation.Coordinator.ReadContextAsync(evaluation.Key, root, token);
            var completion = await LiveModelProtocol.CompleteMeteredAsync(evaluation.Model, evaluation.Coordinator, context,
                "handoff", "You are a CrestCreates triage agent. Return strict JSON only.",
                string.Join("\n",
                    "Private triage input: Pump 7 vibration exceeds the approved threshold; quality review is required before release.",
                    "Return exactly {\"recipient\":\"quality\",\"summary\":\"<concise handoff summary>\"}."), token);
            var (recipient, summary) = LiveModelProtocol.ReadHandoff(completion.Text);
            evaluation.Artifacts[summaryArtifact.Id] = summary;
            var offer = new AgentHandoffOffer(Guid.NewGuid(), recipient, [], [summaryArtifact]);
            await evaluation.Coordinator.OfferHandoffAsync(evaluation.Key, root, offer, token);
            var accepted = await evaluation.Coordinator.AcceptHandoffAsync(evaluation.Key, root.RunId, offer.HandoffId, recipient, token);
            await Assert.ThrowsAsync<InvalidOperationException>(() => evaluation.Coordinator.ReadRunAsync(evaluation.Key, root, token).AsTask());
            var recipientContext = await evaluation.Coordinator.ReadContextAsync(evaluation.Key, accepted, token);
            var recipientView = await evaluation.Coordinator.ReadRunAsync(evaluation.Key, accepted, token);
            Assert.Equal(root.Epoch + 1, accepted.Epoch);
            Assert.Equal(root.RunId, accepted.RunId);
            Assert.Equal(summaryArtifact, Assert.Single(recipientContext.Inputs));
            Assert.Empty(recipientContext.Tools);
            Assert.Null(recipientContext.ResumedBy);
            Assert.Null(recipientView.ChildCall);
            Assert.Null(recipientView.OriginalAssignment);
            Assert.Empty(recipientView.PlanHistory);
            Assert.Empty(recipientView.Reservations);
            Assert.Empty(recipientView.Resumptions);
            var worker = Worker(evaluation, "You are the quality handoff recipient. Return strict JSON.");
            var result = await worker.RunAsync(recipientContext, token);
            await evaluation.Coordinator.FinishAsync(evaluation.Key, accepted, result, token);
            var durable = (await evaluation.Store.FindAsync(evaluation.Key, default))!;
            var durableRun = durable.Runs[root.RunId];
            Assert.Equal(2, durableRun.Reservations.Count);
            Assert.Contains(durableRun.Reservations.Values, receipt => receipt.OwnerEpoch == root.Epoch);
            Assert.Contains(durableRun.Reservations.Values, receipt => receipt.OwnerEpoch == accepted.Epoch);
            Assert.Equal(accepted, Assert.Single(durableRun.Handoffs).AcceptedOwner);
            Assert.Equal(2, evaluation.ModelCalls.Count);
            var recipientPrompt = evaluation.ModelCalls[1].Messages.Single(message => message.Role == "user").Text;
            Assert.Contains(summary, recipientPrompt);
            Assert.DoesNotContain(privateInput.Id, recipientPrompt);
            evaluation.Facts["previousOwner"] = root;
            evaluation.Facts["acceptedOwner"] = accepted;
            evaluation.Facts["offer"] = offer;
            evaluation.Facts["result"] = result;
        });

    [AgentLiveEvalFact]
    public Task GroupChat_PublishesOnlyRealModelRepliesInTurnOrder() =>
        RunAsync("group-chat", async (evaluation, token) =>
        {
            evaluation.Artifacts["private-unpublished"] = "PRIVATE_UNPUBLISHED";
            var root = await StartRootAsync(evaluation, "moderator",
                "Moderate a two-turn maintenance and quality discussion.", new(100000, 8), [], [], token);
            var participants = new[]
            {
                new AgentRunAssignment(Guid.NewGuid(), "maintenance", "Return a maintenance reply containing MAINTENANCE_CHAT.", new(30000, 1), [], []),
                new AgentRunAssignment(Guid.NewGuid(), "quality", "Read the published maintenance reply and return QUALITY_CHAT.", new(30000, 1), [], [])
            };
            var team = new AgentTeamRunner(evaluation.Coordinator,
            [
                new AgentTeamMember("maintenance", [], Worker(evaluation, "You are a maintenance discussant. Return strict JSON.")),
                new AgentTeamMember("quality", [], Worker(evaluation, "You are a quality discussant. Return strict JSON."))
            ]);
            var discussionId = Guid.NewGuid();

            var discussion = await team.RunDiscussionAsync(evaluation.Key, root, discussionId, 2, [.. participants], token);

            Assert.True(discussion.Closed);
            Assert.Equal(new[] { "maintenance", "quality" }, discussion.Messages.Select(message => message.AgentId).ToArray());
            Assert.Equal(2, evaluation.ModelCalls.Count);
            var firstArtifact = discussion.Messages[0].Artifact;
            Assert.Contains(evaluation.Artifacts[firstArtifact.Id],
                evaluation.ModelCalls[1].Messages.Single(message => message.Role == "user").Text);
            Assert.All(evaluation.ModelCalls, call => Assert.DoesNotContain("PRIVATE_UNPUBLISHED",
                call.Messages.Single(message => message.Role == "user").Text));
            await evaluation.Coordinator.FinishAsync(evaluation.Key, root, new(AgentResultKind.Returned, "DiscussionCompleted"), token);
            evaluation.Facts["discussion"] = discussion;
        });

    [AgentLiveEvalFact]
    public Task HumanInTheLoop_WaitsForDurableEventThenResumesRealModel() =>
        RunAsync("human-in-the-loop", async (evaluation, token) =>
        {
            var root = await StartRootAsync(evaluation, "reviewer",
                "Wait for human approval before finalizing the release.", new(100000, 6), [], [], token);
            var context = await evaluation.Coordinator.ReadContextAsync(evaluation.Key, root, token);
            var first = await LiveModelProtocol.CompleteMeteredAsync(evaluation.Model, evaluation.Coordinator, context,
                "wait", "You are a CrestCreates human-in-the-loop reviewer. Return strict JSON only.",
                string.Join("\n",
                    "Return exactly {\"wait\":{\"source\":\"human-task\",\"kind\":\"decided\",\"subjectId\":\"approval-live-1\"}}."), token);
            var selector = LiveModelProtocol.ReadWait(first.Text);
            var wait = new AgentEventWait(Guid.NewGuid(), selector);
            await evaluation.Coordinator.WaitForEventAsync(evaluation.Key, root, wait, token);
            var waiting = (await evaluation.Store.FindAsync(evaluation.Key, default))!;
            Assert.Null(waiting.Runs[root.RunId].Result);
            Assert.Equal(wait, waiting.Runs[root.RunId].Wait);
            var payload = LiveModelProtocol.Artifact("human-decision", "APPROVED_BY_HUMAN");
            evaluation.Artifacts[payload.Id] = "APPROVED_BY_HUMAN";
            var delivery = new AgentEventDelivery("human-decision-1", evaluation.Key.TenantId, selector, payload);
            Assert.True(await evaluation.Coordinator.ResumeAsync(evaluation.Key, root.RunId, wait.WaitId, delivery, token));
            Assert.False(await evaluation.Coordinator.ResumeAsync(evaluation.Key, root.RunId, wait.WaitId, delivery, token));
            var resumed = await evaluation.Coordinator.ReadContextAsync(evaluation.Key, root, token);
            Assert.Equal(delivery, resumed.ResumedBy);
            var second = await LiveModelProtocol.CompleteMeteredAsync(evaluation.Model, evaluation.Coordinator, resumed,
                "resume", "You are a CrestCreates human-in-the-loop reviewer. Return strict JSON only.",
                string.Join("\n",
                    "The durable human event payload is APPROVED_BY_HUMAN.",
                    "Return {\"result\":{\"kind\":\"Returned\",\"code\":\"HumanDecisionApplied\",\"text\":\"<answer containing APPROVED_BY_HUMAN>\"}}."), token);
            var result = LiveModelProtocol.ReadResult(second.Text, root.RunId, "human-decision");
            await evaluation.Coordinator.FinishAsync(evaluation.Key, root, result, token);
            var snapshot = (await evaluation.Store.FindAsync(evaluation.Key, default))!;
            Assert.Equal(delivery, Assert.Single(snapshot.Runs[root.RunId].Resumptions).Delivery);
            Assert.Equal(2, evaluation.ModelCalls.Count);
            Assert.Contains("APPROVED_BY_HUMAN", evaluation.ModelCalls[1].Messages.Single(message => message.Role == "user").Text);
            evaluation.Facts["wait"] = wait;
            evaluation.Facts["delivery"] = delivery;
            evaluation.Facts["result"] = result;
        });

    private static async Task RunAsync(string mode, Func<LiveEvaluation, CancellationToken, Task> run)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        await using var evaluation = new LiveEvaluation(mode);
        try
        {
            await run(evaluation, timeout.Token);
            await AssertCommonEvidenceAsync(evaluation);
        }
        catch (Exception exception)
        {
            evaluation.Failure = exception;
            throw;
        }
    }

    private static async Task AssertCommonEvidenceAsync(LiveEvaluation evaluation)
    {
        Assert.NotEmpty(evaluation.HttpCalls);
        Assert.All(evaluation.HttpCalls, call =>
        {
            Assert.Equal(HttpStatusCode.OK, call.Status);
            Assert.EndsWith("/v1/chat/completions", new Uri(call.Uri).AbsolutePath, StringComparison.Ordinal);
        });
        Assert.Equal(evaluation.HttpCalls.Count, evaluation.ModelCalls.Count);
        Assert.All(evaluation.ModelCalls, call =>
        {
            Assert.Contains("NoneChatToolMode", call.ToolMode ?? string.Empty, StringComparison.Ordinal);
            Assert.True(string.Equals(call.FinishReason, "stop", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(call.Usage);
            Assert.True(call.Usage.Total is > 0);
            Assert.False(call.ObservedModel is null);
            Assert.DoesNotContain("FunctionCallContent", call.ResponseContentTypes, StringComparison.Ordinal);
            Assert.DoesNotContain("FunctionResultContent", call.ResponseContentTypes, StringComparison.Ordinal);
            Assert.Equal(Hash(call.ResponseText), call.ResponseHash);
        });
        var snapshot = (await evaluation.Store.FindAsync(evaluation.Key, default))!;
        Assert.Equal(AgentResultKind.Returned, snapshot.Runs[evaluation.Key.RootRunId].Result!.Kind);
        Assert.Empty(snapshot.Workflows);
        Assert.Empty(snapshot.Capabilities);
        evaluation.Facts["httpCallCount"] = evaluation.HttpCalls.Count;
        evaluation.Facts["modelCallCount"] = evaluation.ModelCalls.Count;
        evaluation.Facts["observedModels"] = evaluation.ModelCalls.Select(call => call.ObservedModel).Distinct().ToArray();
        evaluation.Facts["totalTokens"] = evaluation.ModelCalls.Sum(call => call.Usage!.Total!.Value);
    }

    private static ValueTask<AgentRunOwner> StartRootAsync(
        LiveEvaluation evaluation, string agentId, string task, AgentBudget budget,
        ImmutableArray<string> tools, ImmutableArray<AgentArtifactReference> inputs, CancellationToken token) =>
        evaluation.Coordinator.StartAsync(evaluation.Key, "live-evaluator", TimeProvider.System.GetUtcNow().AddMinutes(10),
            new(evaluation.Key.RootRunId, agentId, task, budget, tools, inputs), token);

    private static MicrosoftAgentRunWorker Worker(LiveEvaluation evaluation, string role) =>
        new(evaluation.Model, evaluation.Coordinator, role, evaluation.Artifacts);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class LiveToolExecutor : IAgentRunToolExecutor
    {
        public int Calls { get; private set; }
        public AgentToolSelection? Selection { get; private set; }

        public ValueTask<AgentToolObservation> InvokeAsync(
            AgentRunContext context, Guid operationId, AgentToolSelection selection, CancellationToken cancellationToken)
        {
            Calls++;
            Selection = selection;
            using var document = JsonDocument.Parse("""{"workOrderId":"WO-LIVE-7","outcome":"Passed","code":"MES_PASS"}""");
            return ValueTask.FromResult(new AgentToolObservation("MES_PASS", true, document.RootElement.Clone()));
        }
    }
}
