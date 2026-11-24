using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrestCreates.Agent.Orchestration;
using CrestCreates.Sample.AssetManagement.MicrosoftAgent;
using Microsoft.Extensions.AI;

namespace CrestCreates.Sample.AssetManagement.MicrosoftAgent.Tests;

public sealed class CoordinationAdapterTests
{
    [Fact]
    public async Task ReActAdapterUsesObservationAndComputesTheResultArtifact()
    {
        const string observationMarker = "MES_PASS";
        string? terminalResponse = null;
        using var chat = new ScriptedChatClient((turn, messages, options, _) =>
        {
            Assert.Equal(ChatToolMode.None, options!.ToolMode);
            Assert.IsType<ChatResponseFormatJson>(options.ResponseFormat);
            var system = messages.Single(message => message.Role == ChatRole.System).Text;
            var prompt = messages.Single(message => message.Role == ChatRole.User).Text;
            var tokenLimit = turn == 1 ? 1024L : 1013L;
            Assert.Equal((int)Math.Min(512, tokenLimit - InputReserve(system, prompt)), options.MaxOutputTokens);
            if (turn == 1)
            {
                Assert.DoesNotContain(observationMarker, system, StringComparison.Ordinal);
                Assert.DoesNotContain(observationMarker, prompt, StringComparison.Ordinal);
                return Task.FromResult(Response("""
                    {"tool":{"name":"mes.read","arguments":{"workOrderId":"WO-7"}}}
                    """, 11));
            }

            Assert.Contains(observationMarker, prompt, StringComparison.Ordinal);
            Assert.Contains("WO-7", prompt, StringComparison.Ordinal);
            terminalResponse = """
                {"result":{"kind":"Returned","code":"ReportReady","text":"MES evidence passed: MES_PASS."}}
                """;
            return Task.FromResult(Response(terminalResponse, 7));
        });
        var model = new MicrosoftAgentCoordinationModel(chat, "scripted-model", 512);
        var executor = new MicrosoftAgentReActModelExecutor(model);
        var owner = new AgentRunOwner(Guid.NewGuid(), "engineer", 1);
        var context = new AgentRunContext(new("tenant", owner.RunId), owner, "user", "Inspect MES evidence",
            DateTimeOffset.UtcNow.AddMinutes(1), new(2048, 2), ["mes.read"], [], null);
        using var observation = JsonDocument.Parse("""{"code":"MES_PASS"}""");

        var first = await executor.ExecuteAsync(new(context, 1, 1024, []));
        Assert.Equal(11, first.UsedTokens);
        Assert.Equal("mes.read", first.Decision.Tool!.Name);
        Assert.Equal("WO-7", first.Decision.Tool.Arguments!.Value.GetProperty("workOrderId").GetString());

        var second = await executor.ExecuteAsync(new(context, 2, 1013,
            [new(first.Decision.Tool, new(observationMarker, true, observation.RootElement.Clone()))]));
        Assert.Equal(7, second.UsedTokens);
        Assert.Equal(AgentResultKind.Returned, second.Decision.Result!.Kind);
        Assert.Equal("ReportReady", second.Decision.Result.Code);
        Assert.Equal(Hash("MES evidence passed: MES_PASS."), second.Decision.Result.Output!.ContentHash);
        var observedResult = LiveModelProtocol.ReadResult(
            terminalResponse!, owner.RunId, "react", "ReportReady", observationMarker);
        Assert.Equal(observedResult.Output!.ContentHash, second.Decision.Result.Output!.ContentHash);
        Assert.All(chat.Calls.First().Messages, message =>
            Assert.DoesNotContain(observationMarker, message.Text, StringComparison.Ordinal));
        Assert.Equal(2, chat.Turns);
    }

    [Fact]
    public async Task ReActAdapterRejectsAnUngrantedToolWithoutCallingTheProviderAgain()
    {
        using var chat = new ScriptedChatClient((_, _, _, _) => Task.FromResult(Response("""
            {"tool":{"name":"private.delete","arguments":{}}}
            """, 5)));
        var executor = new MicrosoftAgentReActModelExecutor(new MicrosoftAgentCoordinationModel(chat, "scripted-model"));
        var owner = new AgentRunOwner(Guid.NewGuid(), "engineer", 1);
        var context = new AgentRunContext(new("tenant", owner.RunId), owner, "user", "Inspect",
            DateTimeOffset.UtcNow.AddMinutes(1), new(2048, 1), ["mes.read"], [], null);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(new(context, 1, 1024, [])).AsTask());

        Assert.Contains("invalid or ungranted", failure.Message);
        Assert.Equal(1, chat.Turns);
    }

    [Fact]
    public async Task TeamWorkerMetersUsageAndReadsOnlyExplicitInputArtifacts()
    {
        var store = new InMemoryAgentExecutionStore();
        var coordinator = new AgentRunCoordinator(store, TimeProvider.System);
        var key = new AgentExecutionKey("tenant", Guid.NewGuid());
        var shared = new AgentArtifactReference("shared", Hash("MES_PASS"));
        var root = await coordinator.StartAsync(key, "user", TimeProvider.System.GetUtcNow().AddMinutes(1),
            new(key.RootRunId, "root", "Review", new(8192, 2), [], [shared]));
        var child = Assert.Single(await coordinator.StartChildrenAsync(key, root,
            [new(Guid.NewGuid(), "quality", "Review shared evidence", new(4096, 1), [], [shared])]));
        var artifacts = new ConcurrentDictionary<string, string>
        {
            ["shared"] = "MES_PASS",
            ["private"] = "DO_NOT_SHARE"
        };
        using var chat = new ScriptedChatClient((_, messages, options, _) =>
        {
            var system = messages.Single(message => message.Role == ChatRole.System).Text;
            var prompt = messages.Single(message => message.Role == ChatRole.User).Text;
            Assert.Equal((int)Math.Min(2048, 2048 - InputReserve(system, prompt)), options!.MaxOutputTokens);
            Assert.Contains("MES_PASS", prompt, StringComparison.Ordinal);
            Assert.DoesNotContain("DO_NOT_SHARE", prompt, StringComparison.Ordinal);
            return Task.FromResult(Response("""{"reply":"QUALITY_OK"}""", 9));
        });
        var worker = new MicrosoftAgentRunWorker(
            new MicrosoftAgentCoordinationModel(chat, "scripted-model", 2048), coordinator,
            "You are a quality reviewer.", artifacts);

        var result = await worker.RunAsync(await coordinator.ReadContextAsync(key, child));

        Assert.Equal(AgentResultKind.Returned, result.Kind);
        Assert.Equal(Hash("QUALITY_OK"), result.Output!.ContentHash);
        Assert.Equal("QUALITY_OK", artifacts[result.Output.Id]);
        var snapshot = await coordinator.ReadRunAsync(key, child);
        Assert.Equal(new AgentBudget(4087, 1), snapshot.RemainingBudget);
        Assert.Equal(new AgentBudget(9, 0), Assert.Single(snapshot.Reservations.Values).Used);
    }

    [Fact]
    public async Task ReActAdapterRejectsInsufficientTotalBudgetBeforeProviderCall()
    {
        using var chat = new ScriptedChatClient((_, _, _, _) =>
            throw new Xunit.Sdk.XunitException("Unexpected provider call."));
        var executor = new MicrosoftAgentReActModelExecutor(new MicrosoftAgentCoordinationModel(chat, "scripted-model"));
        var owner = new AgentRunOwner(Guid.NewGuid(), "engineer", 1);
        var context = new AgentRunContext(new("tenant", owner.RunId), owner, "user", "Inspect",
            DateTimeOffset.UtcNow.AddMinutes(1), new(1, 0), ["mes.read"], [], null);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(new(context, 1, 1, [])).AsTask());

        Assert.Contains("no safe output budget", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, chat.Turns);
    }

    [Fact]
    public async Task CoordinationModelDefaultCallKeepsConfiguredOutputLimit()
    {
        using var chat = new ScriptedChatClient((_, _, options, _) =>
        {
            Assert.Equal(123, options!.MaxOutputTokens);
            return Task.FromResult(Response("""{"reply":"ok"}""", 1));
        });
        var model = new MicrosoftAgentCoordinationModel(chat, "scripted-model", 123);

        var completion = await model.CompleteAsync("Return JSON.", "Return a reply.");

        Assert.Equal(1, completion.Usage.TotalTokenCount);
        Assert.Equal(1, chat.Turns);
    }

    [Fact]
    public async Task CoordinationModelRejectsReportedUsageAboveReservedTotal()
    {
        using var chat = new ScriptedChatClient((_, _, _, _) => Task.FromResult(Response("""{"reply":"ok"}""", 2048)));
        var model = new MicrosoftAgentCoordinationModel(chat, "scripted-model");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            model.CompleteAsync("Return JSON.", "Return a reply.", new MicrosoftAgentCoordinationCallOptions(1024)));

        Assert.Contains("exceeded its reserved total token budget", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, chat.Turns);
    }

    [Theory]
    [InlineData("missing", "missing explicit artifact")]
    [InlineData("mismatch", "content hash mismatch")]
    [InlineData("malformed", "invalid SHA-256 reference")]
    public async Task TeamWorkerRejectsUnverifiableExplicitInputBeforeProviderCall(string failureKind, string reason)
    {
        var contentHash = failureKind switch
        {
            "missing" => Hash("MES_PASS"),
            "mismatch" => Hash("DIFFERENT_CONTENT"),
            _ => "sha256:not-hex"
        };
        var reference = new AgentArtifactReference("shared", contentHash);
        var (coordinator, key, _, child) = await StartWorkerChildAsync(4096, [reference]);
        var artifacts = new ConcurrentDictionary<string, string>();
        if (failureKind != "missing")
        {
            artifacts[reference.Id] = "MES_PASS";
        }

        using var chat = new ScriptedChatClient((_, _, _, _) =>
            throw new Xunit.Sdk.XunitException("Unexpected provider call."));
        var worker = new MicrosoftAgentRunWorker(
            new MicrosoftAgentCoordinationModel(chat, "scripted-model"), coordinator, "Review evidence.", artifacts);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await worker.RunAsync(await coordinator.ReadContextAsync(key, child)).AsTask());

        Assert.Contains(reason, failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, chat.Turns);
        Assert.Empty((await coordinator.ReadRunAsync(key, child)).Reservations);
    }

    [Fact]
    public async Task TeamWorkerRejectsInsufficientTotalBudgetBeforeProviderCall()
    {
        var (coordinator, key, _, child) = await StartWorkerChildAsync(64, []);
        using var chat = new ScriptedChatClient((_, _, _, _) =>
            throw new Xunit.Sdk.XunitException("Unexpected provider call."));
        var worker = new MicrosoftAgentRunWorker(
            new MicrosoftAgentCoordinationModel(chat, "scripted-model"), coordinator, "Return JSON.");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await worker.RunAsync(await coordinator.ReadContextAsync(key, child)).AsTask());

        Assert.Contains("no safe output budget", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, chat.Turns);
        var snapshot = await coordinator.ReadRunAsync(key, child);
        Assert.Equal(new AgentBudget(64, 1), snapshot.RemainingBudget);
        Assert.Empty(snapshot.Reservations);
    }

    [Fact]
    public async Task TeamWorkerBuildsResumedPromptFromVerifiedDurablePayload()
    {
        var coordinator = new AgentRunCoordinator(new InMemoryAgentExecutionStore(), TimeProvider.System);
        var key = new AgentExecutionKey("tenant", Guid.NewGuid());
        var root = await coordinator.StartAsync(key, "user", TimeProvider.System.GetUtcNow().AddMinutes(1),
            new(key.RootRunId, "reviewer", "Apply the human decision", new(4096, 1), [], []));
        var selector = new AgentEventSelector("human-task", "decided", "approval-1");
        var wait = new AgentEventWait(Guid.NewGuid(), selector);
        await coordinator.WaitForEventAsync(key, root, wait);
        var payload = new AgentArtifactReference("human-decision", Hash("APPROVED_BY_HUMAN"));
        var artifacts = new ConcurrentDictionary<string, string> { [payload.Id] = "APPROVED_BY_HUMAN" };
        var delivery = new AgentEventDelivery("human-decision-1", key.TenantId, selector, payload);
        Assert.True(await coordinator.ResumeAsync(key, root.RunId, wait.WaitId, delivery));
        var resumed = await coordinator.ReadContextAsync(key, root);
        using var chat = new ScriptedChatClient((_, messages, _, _) =>
        {
            var prompt = messages.Single(message => message.Role == ChatRole.User).Text;
            Assert.Contains(delivery.EventId, prompt, StringComparison.Ordinal);
            Assert.Contains(payload.ContentHash, prompt, StringComparison.Ordinal);
            Assert.Contains("APPROVED_BY_HUMAN", prompt, StringComparison.Ordinal);
            return Task.FromResult(Response("""{"reply":"HumanDecisionApplied: APPROVED_BY_HUMAN"}""", 9));
        });
        var worker = new MicrosoftAgentRunWorker(
            new MicrosoftAgentCoordinationModel(chat, "scripted-model"), coordinator, "Apply the durable event.", artifacts);

        var result = await worker.RunAsync(resumed);

        Assert.Equal("ReportReady", result.Code);
        Assert.Equal(Hash("HumanDecisionApplied: APPROVED_BY_HUMAN"), result.Output!.ContentHash);
        Assert.Equal(1, chat.Turns);
    }

    [Fact]
    public async Task TeamWorkerRejectsMismatchedResumedPayloadBeforeProviderCall()
    {
        var coordinator = new AgentRunCoordinator(new InMemoryAgentExecutionStore(), TimeProvider.System);
        var key = new AgentExecutionKey("tenant", Guid.NewGuid());
        var root = await coordinator.StartAsync(key, "user", TimeProvider.System.GetUtcNow().AddMinutes(1),
            new(key.RootRunId, "reviewer", "Apply the human decision", new(4096, 1), [], []));
        var selector = new AgentEventSelector("human-task", "decided", "approval-1");
        var wait = new AgentEventWait(Guid.NewGuid(), selector);
        await coordinator.WaitForEventAsync(key, root, wait);
        var payload = new AgentArtifactReference("human-decision", Hash("APPROVED_BY_HUMAN"));
        var artifacts = new ConcurrentDictionary<string, string> { [payload.Id] = "REJECTED_BY_HUMAN" };
        var delivery = new AgentEventDelivery("human-decision-1", key.TenantId, selector, payload);
        Assert.True(await coordinator.ResumeAsync(key, root.RunId, wait.WaitId, delivery));
        var resumed = await coordinator.ReadContextAsync(key, root);
        using var chat = new ScriptedChatClient((_, _, _, _) =>
            throw new Xunit.Sdk.XunitException("Unexpected provider call."));
        var worker = new MicrosoftAgentRunWorker(
            new MicrosoftAgentCoordinationModel(chat, "scripted-model"), coordinator, "Apply the durable event.", artifacts);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => worker.RunAsync(resumed).AsTask());

        Assert.Contains("content hash mismatch", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, chat.Turns);
        Assert.Empty((await coordinator.ReadRunAsync(key, root)).Reservations);
    }

    [Theory]
    [InlineData("human-task", "decided", "approval-other")]
    [InlineData("other-source", "decided", "approval-1")]
    [InlineData("human-task", "other-kind", "approval-1")]
    public async Task HitlOracleRejectsSelectorNotOwnedByFixture(string source, string kind, string subjectId)
    {
        var expected = new AgentEventSelector("human-task", "decided", "approval-1");
        using var chat = new ScriptedChatClient((_, _, _, _) => Task.FromResult(Response($$"""
            {"wait":{"source":"{{source}}","kind":"{{kind}}","subjectId":"{{subjectId}}"} }
            """, 1)));
        var model = new MicrosoftAgentCoordinationModel(chat, "scripted-model");
        var completion = await model.CompleteAsync("Return JSON.", "Wait for the fixture event.");

        var failure = Assert.Throws<InvalidOperationException>(() =>
            LiveModelProtocol.ReadWait(completion.Text, expected));

        Assert.Contains("not owned by the fixture", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, chat.Turns);
    }

    [Theory]
    [InlineData("ReportReady", "The review is complete.")]
    [InlineData("WrongCode", "MES evidence passed: MES_PASS.")]
    public async Task TerminalResultOracleRejectsIgnoredEvidenceAndWrongCode(string code, string text)
    {
        using var chat = new ScriptedChatClient((_, _, _, _) => Task.FromResult(Response($$"""
            {"result":{"kind":"Returned","code":"{{code}}","text":"{{text}}"} }
            """, 1)));
        var model = new MicrosoftAgentCoordinationModel(chat, "scripted-model");
        var completion = await model.CompleteAsync("Return JSON.", "Return the observed evidence.");

        var failure = Assert.Throws<InvalidOperationException>(() =>
            LiveModelProtocol.ReadResult(completion.Text, Guid.NewGuid(), "offline", "ReportReady", "MES_PASS"));

        Assert.Contains(code == "ReportReady" ? "required evidence" : "instead of", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, chat.Turns);
    }

    [Fact]
    public async Task CoordinationModelRejectsMissingUsageAndLeavesNoFalseReceipt()
    {
        using var chat = new ScriptedChatClient((_, _, _, _) => Task.FromResult(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"reply":"ok"}"""))));
        var model = new MicrosoftAgentCoordinationModel(chat, "scripted-model");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            model.CompleteAsync("Return JSON.", "Return a reply."));

        Assert.Contains("usage", failure.Message);
        Assert.Equal(1, chat.Turns);
    }

    private static async Task<(AgentRunCoordinator Coordinator, AgentExecutionKey Key, AgentRunOwner Root, AgentRunOwner Child)>
        StartWorkerChildAsync(long modelTokens, ImmutableArray<AgentArtifactReference> inputs)
    {
        var coordinator = new AgentRunCoordinator(new InMemoryAgentExecutionStore(), TimeProvider.System);
        var key = new AgentExecutionKey("tenant", Guid.NewGuid());
        var root = await coordinator.StartAsync(key, "user", TimeProvider.System.GetUtcNow().AddMinutes(1),
            new(key.RootRunId, "root", "Review", new(modelTokens + 128, 2), [], inputs));
        var child = Assert.Single(await coordinator.StartChildrenAsync(key, root,
            [new(Guid.NewGuid(), "quality", "Review evidence", new(modelTokens, 1), [], inputs)]));
        return (coordinator, key, root, child);
    }

    private static long InputReserve(string systemPrompt, string userPrompt) =>
        Encoding.UTF8.GetByteCount(systemPrompt) + Encoding.UTF8.GetByteCount(userPrompt) + 64;

    private static ChatResponse Response(string text, long tokens) => new(new ChatMessage(ChatRole.Assistant, text))
    {
        ModelId = "observed-model",
        FinishReason = ChatFinishReason.Stop,
        Usage = new UsageDetails { InputTokenCount = tokens - 1, OutputTokenCount = 1, TotalTokenCount = tokens }
    };

    private static string Hash(string value) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
