using System.Text.Json;
using CrestCreates.Agent.Orchestration;
using CrestCreates.Agent.Tools;
using CrestCreates.Sample.AssetManagement.Contracts;
using Microsoft.Extensions.AI;

namespace CrestCreates.Sample.AssetManagement.MicrosoftAgent.Tests;

public sealed class AssetExpertTests
{
    private static readonly Guid AssetId = Guid.Parse("e2c839cd-85ec-43bf-9470-898a05506873");

    [Fact]
    public async Task RoundTripsSdkFunctionCallThroughCrestCreatesInvoker()
    {
        var invoker = new StrictInvoker();
        using var chat = new ScriptedChatClient((turn, messages, options, _) =>
        {
            Assert.Equal(AssetContractIds.GetTool, Assert.IsAssignableFrom<AIFunctionDeclaration>(Assert.Single(options!.Tools!)).Name);
            Assert.Null(options.ConversationId);
            if (turn == 1)
            {
                Assert.Equal(2, messages.Length);
                return Task.FromResult(Calls(Call("lookup", AssetContractIds.GetTool)));
            }

            Assert.Equal(2, turn);
            Assert.Equal("lookup", Assert.IsType<FunctionCallContent>(Assert.Single(messages[^2].Contents)).CallId);
            Assert.Equal(ChatRole.Tool, messages[^1].Role);
            var result = Assert.IsType<FunctionResultContent>(Assert.Single(messages[^1].Contents));
            Assert.Equal("lookup", result.CallId);
            Assert.Equal("Pump 8", Assert.IsType<JsonElement>(result.Result).GetProperty("name").GetString());
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "The asset is Pump 8.")));
        });

        Assert.Equal("The asset is Pump 8.", await new MicrosoftAgentAssetExpert(chat, invoker).RunAsync($"Find {AssetId}"));
        Assert.Equal(2, chat.Turns);
        Assert.Equal(1, invoker.Calls);
    }

    [Theory]
    [InlineData("asset_activate")]
    [InlineData("ASSET_GET")]
    public async Task DeniesToolsOutsideTheExactAllowlist(string toolName)
    {
        using var chat = new ScriptedChatClient((_, _, _, _) => Task.FromResult(Calls(Call("denied", toolName))));
        var invoker = new StrictInvoker();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new MicrosoftAgentAssetExpert(chat, invoker).RunAsync("Find this asset"));

        Assert.Contains("allowlist", exception.Message);
        Assert.Equal(0, invoker.Calls);
        Assert.Equal(1, chat.Turns);
    }

    [Fact]
    public async Task StopsOnCrestCreatesGovernanceDenial()
    {
        using var chat = new ScriptedChatClient((_, _, _, _) => Task.FromResult(Calls(Call("lookup", AssetContractIds.GetTool))));
        var invoker = new StrictInvoker(deny: true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new MicrosoftAgentAssetExpert(chat, invoker).RunAsync("Find this asset"));

        Assert.Contains("SCOPE_DENIED", exception.Message);
        Assert.Equal(1, invoker.Calls);
        Assert.Equal(1, chat.Turns);
    }

    [Fact]
    public async Task RejectsBatchedCallsBeforeInvokingAnyTool()
    {
        using var chat = new ScriptedChatClient((_, _, _, _) => Task.FromResult(Calls(
            Call("first", AssetContractIds.GetTool), Call("second", "asset_activate"))));
        var invoker = new StrictInvoker();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new MicrosoftAgentAssetExpert(chat, invoker).RunAsync("Find this asset"));

        Assert.Equal(0, invoker.Calls);
        Assert.Equal(1, chat.Turns);
    }

    [Fact]
    public async Task CapsProviderTurnsAndToolCallsEvenIfTheModelIgnoresToolMode()
    {
        using var chat = new ScriptedChatClient((turn, _, options, _) =>
        {
            if (turn == 3)
            {
                Assert.Same(ChatToolMode.None, options!.ToolMode);
                Assert.True(options.Tools is null or { Count: 0 });
            }

            return Task.FromResult(Calls(Call($"lookup-{turn}", AssetContractIds.GetTool)));
        });
        var invoker = new StrictInvoker();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new MicrosoftAgentAssetExpert(chat, invoker).RunAsync("Find this asset"));

        Assert.Equal(2, invoker.Calls);
        Assert.Equal(3, chat.Turns);
    }

    [Fact]
    public async Task CancellationPreventsAnInvocationAfterTheProviderReturns()
    {
        using var cancellation = new CancellationTokenSource();
        using var chat = new ScriptedChatClient((_, _, _, token) =>
        {
            Assert.True(token.CanBeCanceled);
            cancellation.Cancel();
            Assert.True(token.IsCancellationRequested);
            return Task.FromResult(Calls(Call("late", AssetContractIds.GetTool)));
        });
        var invoker = new StrictInvoker();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new MicrosoftAgentAssetExpert(chat, invoker).RunAsync("Find this asset", cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(0, invoker.Calls);
        Assert.Equal(1, chat.Turns);
    }

    [Fact]
    public async Task DeadlineEndsTheRunWhenTheProviderDoesNotObserveCancellation()
    {
        var stalled = new TaskCompletionSource<ChatResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var chat = new ScriptedChatClient((_, _, _, _) => stalled.Task);
        var invoker = new StrictInvoker();

        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() =>
                new MicrosoftAgentAssetExpert(chat, invoker).RunAsync("Find this asset", TimeSpan.FromMilliseconds(200)));
        }
        finally
        {
            stalled.TrySetResult(Calls(Call("late", AssetContractIds.GetTool)));
        }

        Assert.Equal(0, invoker.Calls);
    }

    [Fact]
    public async Task NativeChildSettlesExactModelAndToolUsageAndDebitsParent()
    {
        var (coordinator, root, child) = await StartNativeChildAsync(new(40, 2));
        var invoker = new StrictInvoker(onInvocation: async token =>
        {
            var snapshot = await coordinator.ReadRunAsync(child.Execution, child.Owner, token);
            Assert.Equal(new AgentBudget(29, 1), snapshot.RemainingBudget);
            Assert.Contains(snapshot.Reservations.Values, receipt => receipt.Used == new AgentBudget(11, 0));
            Assert.Contains(snapshot.Reservations.Values, receipt => receipt is { Reserved.ToolCalls: 1, Used: null });
        });
        using var chat = new ScriptedChatClient(async (turn, messages, options, token) =>
        {
            Assert.Equal(turn == 1 ? 40 : 29, options!.MaxOutputTokens);
            Assert.Null(options.ConversationId);
            var snapshot = await coordinator.ReadRunAsync(child.Execution, child.Owner, token);
            Assert.Equal(0, snapshot.RemainingBudget.ModelTokens);
            if (turn == 1)
            {
                var response = LookupWithUsage(11);
                response.ConversationId = "provider-conversation";
                return response;
            }

            Assert.Equal(1, invoker.Calls);
            Assert.Equal(ChatRole.Tool, messages[^1].Role);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "Pump 8"))
            {
                Usage = new UsageDetails { InputTokenCount = 5, OutputTokenCount = 2, TotalTokenCount = 7 }
            };
        });

        var expert = new MicrosoftAgentAssetExpert(chat, invoker);
        Assert.Equal("Pump 8", await expert.RunAsync(child.Task, coordinator, child));
        await Assert.ThrowsAsync<InvalidOperationException>(() => expert.RunAsync(child.Task, coordinator, child));
        Assert.Equal(2, chat.Turns);
        Assert.Equal(1, invoker.Calls);

        var completed = await coordinator.ReadRunAsync(child.Execution, child.Owner);
        Assert.Equal(new AgentBudget(22, 1), completed.RemainingBudget);
        Assert.Equal(3, completed.Reservations.Count);
        Assert.Contains(completed.Reservations.Values, receipt => receipt is { Reserved.ModelTokens: 40, Used.ModelTokens: 11 });
        Assert.Contains(completed.Reservations.Values, receipt => receipt is { Reserved.ModelTokens: 29, Used.ModelTokens: 7 });
        Assert.Contains(completed.Reservations.Values, receipt => receipt.Used == new AgentBudget(0, 1));

        await coordinator.FinishAsync(child.Execution, child.Owner, new(AgentResultKind.Returned, "ExpertReport"));
        Assert.Equal(new AgentBudget(82, 3), (await coordinator.ReadRunAsync(child.Execution, root)).RemainingBudget);
    }

    [Theory]
    [InlineData(false, 2, "did not grant")]
    [InlineData(true, 0, "tool call budget")]
    public async Task NativeToolDenialPrecedesInvokerEvenIfProviderIgnoresToolMode(bool grantTool, int toolBudget, string reason)
    {
        var (coordinator, _, child) = await StartNativeChildAsync(new(40, toolBudget), grantTool);
        using var chat = new ScriptedChatClient((_, _, options, _) =>
        {
            Assert.Same(ChatToolMode.None, options!.ToolMode);
            Assert.True(options.Tools is null or { Count: 0 });
            return Task.FromResult(LookupWithUsage(11));
        });
        var invoker = new StrictInvoker();
        var expert = new MicrosoftAgentAssetExpert(chat, invoker);
        var misleadingContext = child with { Tools = [AssetContractIds.GetTool], RemainingBudget = new(40, 2) };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => expert.RunAsync(child.Task, coordinator, misleadingContext));

        Assert.Contains(reason, exception.Message);
        Assert.Equal(1, chat.Turns);
        Assert.Equal(0, invoker.Calls);
        var snapshot = await coordinator.ReadRunAsync(child.Execution, child.Owner);
        Assert.Equal(new AgentBudget(29, toolBudget), snapshot.RemainingBudget);
        Assert.Equal(new AgentBudget(11, 0), Assert.Single(snapshot.Reservations.Values).Used);
    }

    [Fact]
    public async Task NativeToolReservationDeniesDispatchWhenBudgetIsConsumedDuringModelTurn()
    {
        var (coordinator, _, child) = await StartNativeChildAsync(new(40, 1));
        using var chat = new ScriptedChatClient(async (_, _, options, token) =>
        {
            Assert.Single(options!.Tools!);
            var competingOperation = Guid.NewGuid();
            Assert.True(await coordinator.ReserveAsync(child.Execution, child.Owner, competingOperation, new(0, 1), token));
            Assert.True(await coordinator.SettleAsync(child.Execution, child.Owner, competingOperation, new(0, 1), token));
            return LookupWithUsage(11);
        });
        var invoker = new StrictInvoker();
        var expert = new MicrosoftAgentAssetExpert(chat, invoker);

        await Assert.ThrowsAsync<InvalidOperationException>(() => expert.RunAsync(child.Task, coordinator, child));

        Assert.Equal(1, chat.Turns);
        Assert.Equal(0, invoker.Calls);
        var snapshot = await coordinator.ReadRunAsync(child.Execution, child.Owner);
        Assert.Equal(new AgentBudget(29, 0), snapshot.RemainingBudget);
        Assert.Equal(2, snapshot.Reservations.Count);
    }

    [Fact]
    public async Task NativeHandoffStartsFreshSdkHistoryAndOperationIdsForNewEpoch()
    {
        var (coordinator, _, child) = await StartNativeChildAsync(new(40, 2));
        using var chat = new ScriptedChatClient((turn, messages, options, _) =>
        {
            Assert.Equal(2, messages.Length);
            Assert.Equal($"Question {turn}", messages[^1].Text);
            Assert.Null(options!.ConversationId);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"Answer {turn}"))
            {
                ConversationId = $"provider-conversation-{turn}",
                Usage = new UsageDetails { TotalTokenCount = 11 }
            });
        });
        var expert = new MicrosoftAgentAssetExpert(chat, new StrictInvoker());
        Assert.Equal("Answer 1", await expert.RunAsync("Question 1", coordinator, child));
        var originalOperation = Assert.Single((await coordinator.ReadRunAsync(child.Execution, child.Owner)).Reservations).Key;
        var handoff = await coordinator.OfferHandoffAsync(child.Execution, child.Owner,
            new(Guid.NewGuid(), "next-expert", [], []));
        var nextOwner = await coordinator.AcceptHandoffAsync(child.Execution, child.Owner.RunId, handoff.Offer.HandoffId, "next-expert");
        var nextContext = await coordinator.ReadContextAsync(child.Execution, nextOwner);

        await Assert.ThrowsAsync<InvalidOperationException>(() => expert.RunAsync("Question 1", coordinator, child));
        Assert.Equal("Answer 2", await expert.RunAsync("Question 2", coordinator, nextContext));

        Assert.Equal(2, chat.Turns);
        var snapshot = await coordinator.ReadRunAsync(child.Execution, nextOwner);
        Assert.Equal(new AgentBudget(18, 2), snapshot.RemainingBudget);
        var newOperation = Assert.Single(snapshot.Reservations);
        Assert.NotEqual(originalOperation, newOperation.Key);
        Assert.Equal(new AgentBudget(11, 0), newOperation.Value.Used);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1L)]
    [InlineData(41L)]
    public async Task NativeModelUsageMustReportAValidTotalBeforeDispatch(long? total)
    {
        var (coordinator, _, child) = await StartNativeChildAsync(new(40, 2));
        using var chat = new ScriptedChatClient((_, _, _, _) => Task.FromResult(LookupWithUsage(total)));
        var invoker = new StrictInvoker();
        var expert = new MicrosoftAgentAssetExpert(chat, invoker);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => expert.RunAsync(child.Task, coordinator, child));
        Assert.Contains("total model token usage", exception.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => expert.RunAsync(child.Task, coordinator, child));

        Assert.Equal(1, chat.Turns);
        Assert.Equal(0, invoker.Calls);
        var snapshot = await coordinator.ReadRunAsync(child.Execution, child.Owner);
        Assert.Equal(new AgentBudget(0, 2), snapshot.RemainingBudget);
        var reservation = Assert.Single(snapshot.Reservations.Values);
        Assert.Equal(new AgentBudget(40, 0), reservation.Reserved);
        Assert.Null(reservation.Used);
    }

    [Fact]
    public async Task NativeModelBudgetDenialPrecedesSdkCall()
    {
        var (coordinator, _, child) = await StartNativeChildAsync(new(0, 2));
        using var chat = new ScriptedChatClient((_, _, _, _) => throw new Xunit.Sdk.XunitException("Unexpected provider call."));
        var invoker = new StrictInvoker();
        var expert = new MicrosoftAgentAssetExpert(chat, invoker);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => expert.RunAsync(child.Task, coordinator, child));

        Assert.Contains("model token budget", exception.Message);
        Assert.Equal(0, chat.Turns);
        Assert.Equal(0, invoker.Calls);
        Assert.Empty((await coordinator.ReadRunAsync(child.Execution, child.Owner)).Reservations);
    }

    [Fact]
    public async Task NativeDeadlineCancelsTheSdkAndBlocksLateToolRequests()
    {
        var (coordinator, _, child) = await StartNativeChildAsync(new(40, 2), duration: TimeSpan.FromSeconds(1));
        var stalled = new TaskCompletionSource<ChatResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerToken = CancellationToken.None;
        using var chat = new ScriptedChatClient((_, _, _, token) =>
        {
            providerToken = token;
            return stalled.Task;
        });
        var invoker = new StrictInvoker();
        var expert = new MicrosoftAgentAssetExpert(chat, invoker);
        var misleadingContext = child with { Deadline = child.Deadline.AddMinutes(1) };

        try
        {
            var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
                expert.RunAsync(child.Task, coordinator, misleadingContext).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Contains("asset expert", exception.Message);
            Assert.True(providerToken.IsCancellationRequested);
        }
        finally
        {
            stalled.TrySetResult(LookupWithUsage(11));
        }

        Assert.Equal(1, chat.Turns);
        Assert.Equal(0, invoker.Calls);
        var snapshot = await coordinator.ReadRunAsync(child.Execution, child.Owner);
        Assert.Null(Assert.Single(snapshot.Reservations.Values).Used);
    }

    private static async Task<(AgentRunCoordinator Coordinator, AgentRunOwner Root, AgentRunContext Child)> StartNativeChildAsync(
        AgentBudget budget, bool grantTool = true, TimeSpan? duration = null)
    {
        var coordinator = new AgentRunCoordinator(new InMemoryAgentExecutionStore(), TimeProvider.System);
        var key = new AgentExecutionKey("asset-tenant", Guid.NewGuid());
        var deadline = DateTimeOffset.UtcNow + (duration ?? TimeSpan.FromMinutes(1));
        var root = await coordinator.StartAsync(key, "asset-reader", deadline,
            new(key.RootRunId, "supervisor", "Read asset data", new(100, 4), [AssetContractIds.GetTool], []));
        var owners = await coordinator.StartChildrenAsync(key, root,
            [new(Guid.NewGuid(), "asset-expert", $"Find {AssetId:D}", budget, grantTool ? [AssetContractIds.GetTool] : [], [])]);
        return (coordinator, root, await coordinator.ReadContextAsync(key, Assert.Single(owners)));
    }

    private static ChatResponse LookupWithUsage(long? totalTokens)
    {
        var response = Calls(Call("lookup", AssetContractIds.GetTool));
        response.Usage = new UsageDetails { InputTokenCount = 8, OutputTokenCount = 3, TotalTokenCount = totalTokens };
        return response;
    }

    private static ChatResponse Calls(params AIContent[] calls) =>
        new(new ChatMessage(ChatRole.Assistant, calls)) { FinishReason = ChatFinishReason.ToolCalls };

    private static FunctionCallContent Call(string id, string name) =>
        new(id, name, new Dictionary<string, object?> { ["assetId"] = AssetId.ToString() });

    private sealed class StrictInvoker(bool deny = false, Func<CancellationToken, Task>? onInvocation = null) : IAgentToolInvoker
    {
        public int Calls { get; private set; }

        public async ValueTask<AgentToolInvocationOutcome> InvokeAsync(AgentToolInvocationRequest request, CancellationToken cancellationToken = default)
        {
            Assert.True(cancellationToken.CanBeCanceled);
            Assert.Equal(AssetContractIds.GetTool, request.ToolName);
            Assert.Null(request.ApprovalEvidence);
            var arguments = Assert.IsType<JsonElement>(request.Arguments);
            Assert.Equal(AssetId, Assert.Single(arguments.EnumerateObject()).Value.GetGuid());
            Assert.True(arguments.TryGetProperty("assetId", out _));
            Calls++;
            if (onInvocation is not null)
            {
                await onInvocation(cancellationToken);
            }

            using var output = JsonDocument.Parse("""{"name":"Pump 8"}""");
            return new AgentToolInvocationOutcome
            {
                Kind = deny ? AgentToolInvocationOutcomeKind.GovernanceDenied : AgentToolInvocationOutcomeKind.Succeeded,
                Code = deny ? "SCOPE_DENIED" : "OK",
                Message = deny ? "Out of scope" : "Found",
                StructuredOutput = deny ? null : output.RootElement.Clone()
            };
        }
    }
}
