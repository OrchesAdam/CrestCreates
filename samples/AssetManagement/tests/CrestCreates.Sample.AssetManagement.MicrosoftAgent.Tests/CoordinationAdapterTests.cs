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
        using var chat = new ScriptedChatClient((turn, messages, options, _) =>
        {
            Assert.Equal(ChatToolMode.None, options!.ToolMode);
            Assert.IsType<ChatResponseFormatJson>(options.ResponseFormat);
            var prompt = messages.Single(message => message.Role == ChatRole.User).Text;
            if (turn == 1)
            {
                return Task.FromResult(Response("""
                    {"tool":{"name":"mes.read","arguments":{"workOrderId":"WO-7"}}}
                    """, 11));
            }

            Assert.Contains("MES_PASS", prompt);
            Assert.Contains("WO-7", prompt);
            return Task.FromResult(Response("""
                {"result":{"kind":"Returned","code":"ReportReady","text":"MES evidence passed."}}
                """, 7));
        });
        var model = new MicrosoftAgentCoordinationModel(chat, "scripted-model", 512);
        var executor = new MicrosoftAgentReActModelExecutor(model);
        var owner = new AgentRunOwner(Guid.NewGuid(), "engineer", 1);
        var context = new AgentRunContext(new("tenant", owner.RunId), owner, "user", "Inspect MES evidence",
            DateTimeOffset.UtcNow.AddMinutes(1), new(100, 2), ["mes.read"], [], null);
        using var observation = JsonDocument.Parse("""{"code":"MES_PASS"}""");

        var first = await executor.ExecuteAsync(new(context, 1, 40, []));
        Assert.Equal(11, first.UsedTokens);
        Assert.Equal("mes.read", first.Decision.Tool!.Name);
        Assert.Equal("WO-7", first.Decision.Tool.Arguments!.Value.GetProperty("workOrderId").GetString());

        var second = await executor.ExecuteAsync(new(context, 2, 29,
            [new(first.Decision.Tool, new("MES_PASS", true, observation.RootElement.Clone()))]));
        Assert.Equal(7, second.UsedTokens);
        Assert.Equal(AgentResultKind.Returned, second.Decision.Result!.Kind);
        Assert.Equal("ReportReady", second.Decision.Result.Code);
        Assert.Equal(Hash("MES evidence passed."), second.Decision.Result.Output!.ContentHash);
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
            DateTimeOffset.UtcNow.AddMinutes(1), new(100, 1), ["mes.read"], [], null);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(new(context, 1, 40, [])).AsTask());

        Assert.Contains("invalid or ungranted", failure.Message);
        Assert.Equal(1, chat.Turns);
    }

    [Fact]
    public async Task TeamWorkerMetersUsageAndReadsOnlyExplicitInputArtifacts()
    {
        var store = new InMemoryAgentExecutionStore();
        var coordinator = new AgentRunCoordinator(store, TimeProvider.System);
        var key = new AgentExecutionKey("tenant", Guid.NewGuid());
        var shared = new AgentArtifactReference("shared", "hash-shared");
        var root = await coordinator.StartAsync(key, "user", TimeProvider.System.GetUtcNow().AddMinutes(1),
            new(key.RootRunId, "root", "Review", new(100, 2), [], [shared]));
        var child = Assert.Single(await coordinator.StartChildrenAsync(key, root,
            [new(Guid.NewGuid(), "quality", "Review shared evidence", new(40, 1), [], [shared])]));
        var artifacts = new ConcurrentDictionary<string, string>
        {
            ["shared"] = "MES_PASS",
            ["private"] = "DO_NOT_SHARE"
        };
        using var chat = new ScriptedChatClient((_, messages, _, _) =>
        {
            var prompt = messages.Single(message => message.Role == ChatRole.User).Text;
            Assert.Contains("MES_PASS", prompt);
            Assert.DoesNotContain("DO_NOT_SHARE", prompt);
            return Task.FromResult(Response("""{"reply":"QUALITY_OK"}""", 9));
        });
        var worker = new MicrosoftAgentRunWorker(
            new MicrosoftAgentCoordinationModel(chat, "scripted-model", 512), coordinator,
            "You are a quality reviewer.", artifacts);

        var result = await worker.RunAsync(await coordinator.ReadContextAsync(key, child));

        Assert.Equal(AgentResultKind.Returned, result.Kind);
        Assert.Equal(Hash("QUALITY_OK"), result.Output!.ContentHash);
        Assert.Equal("QUALITY_OK", artifacts[result.Output.Id]);
        var snapshot = await coordinator.ReadRunAsync(key, child);
        Assert.Equal(new AgentBudget(31, 1), snapshot.RemainingBudget);
        Assert.Equal(new AgentBudget(9, 0), Assert.Single(snapshot.Reservations.Values).Used);
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

    private static ChatResponse Response(string text, long tokens) => new(new ChatMessage(ChatRole.Assistant, text))
    {
        ModelId = "observed-model",
        FinishReason = ChatFinishReason.Stop,
        Usage = new UsageDetails { InputTokenCount = tokens - 1, OutputTokenCount = 1, TotalTokenCount = tokens }
    };

    private static string Hash(string value) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
