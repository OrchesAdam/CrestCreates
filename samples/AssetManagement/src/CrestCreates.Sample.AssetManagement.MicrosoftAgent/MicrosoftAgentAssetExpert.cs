using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CrestCreates.Agent.Orchestration;
using CrestCreates.Agent.Tools;
using CrestCreates.Sample.AssetManagement.Contracts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using AgentRunContext = CrestCreates.Agent.Orchestration.AgentRunContext;

namespace CrestCreates.Sample.AssetManagement.MicrosoftAgent;

/// <summary>
/// Runs a read-only asset lookup using a stateless provider client and a caller-scoped tool invoker.
/// </summary>
public sealed class MicrosoftAgentAssetExpert(
    IChatClient chatClient, IAgentToolInvoker toolInvoker, TimeProvider? timeProvider = null)
{
    private const int MaxToolCalls = 2;
    private static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(30);
    private readonly ChatClientAgent _agent = SingleTurnAgent.Create(chatClient);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public Task<string> RunAsync(string question, CancellationToken cancellationToken = default) =>
        RunAsync(question, MaxDuration, cancellationToken);

    public async Task<string> RunAsync(string question, TimeSpan timeLimit, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        if (timeLimit <= TimeSpan.Zero || timeLimit > MaxDuration)
        {
            throw new ArgumentOutOfRangeException(nameof(timeLimit));
        }

        return await RunWithDeadlineAsync(question, timeLimit, null, cancellationToken);
    }

    /// <summary>
    /// Meters model turns and tool calls against the supplied CrestCreates run. The caller manages
    /// tool invocation identity and run completion. Invalid model usage leaves its reservation pending.
    /// </summary>
    public async Task<string> RunAsync(string question, AgentRunCoordinator coordinator, AgentRunContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(context);
        var run = new NativeRun(coordinator, context.Execution, context.Owner);
        var current = await run.ReadContextAsync(cancellationToken);
        var remaining = current.Deadline - _clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            throw new TimeoutException("The asset expert's native run deadline elapsed.");
        }

        var timeLimit = remaining < MaxDuration ? remaining : MaxDuration;
        return await RunWithDeadlineAsync(question, timeLimit, run, cancellationToken);
    }

    private async Task<string> RunWithDeadlineAsync(string question, TimeSpan timeLimit, NativeRun? run,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(timeLimit, _clock);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            return await RunTurnsAsync(question, run, stop.Token).WaitAsync(stop.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException("The asset expert exceeded its deadline.", exception);
        }
    }

    private async Task<string> RunTurnsAsync(string question, NativeRun? run, CancellationToken cancellationToken)
    {
        var callIds = new HashSet<string>(StringComparer.Ordinal);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "Answer asset questions using the read-only lookup when you have an asset ID. Do not invent asset data."),
            new(ChatRole.User, question)
        };

        // Each follow-up requires exactly one tool result. Reserve the last turn for the answer.
        for (var turn = 0; turn <= MaxToolCalls; turn++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = run is null ? null : await run.ReadContextAsync(cancellationToken);
            var toolGranted = context is null || context.Tools.Contains(AssetContractIds.GetTool, StringComparer.Ordinal);
            var toolBudgetAvailable = context is null || context.RemainingBudget.ToolCalls > 0;
            var allowTool = turn < MaxToolCalls && toolGranted && toolBudgetAvailable;
            var options = new ChatOptions
            {
                Tools = allowTool ? [new GetAssetDeclaration()] : null,
                ToolMode = allowTool ? ChatToolMode.Auto : ChatToolMode.None,
                AllowMultipleToolCalls = false
            };
            var modelOperation = Guid.Empty;
            var tokenLimit = context?.RemainingBudget.ModelTokens ?? 0;
            if (run is not null)
            {
                if (tokenLimit <= 0)
                {
                    throw new InvalidOperationException("The asset expert's native model token budget is exhausted.");
                }

                modelOperation = await run.ReserveAsync(turn, "model", new(tokenLimit, 0), cancellationToken);
                options.MaxOutputTokens = (int)Math.Min(tokenLimit, int.MaxValue);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var response = await _agent.RunAsync(messages, session: null,
                options: new ChatClientAgentRunOptions(options), cancellationToken: cancellationToken).WaitAsync(cancellationToken);
            if (run is not null)
            {
                if (response.Usage?.TotalTokenCount is not { } used || used < 0 || used > tokenLimit)
                {
                    // Unverifiable usage cannot produce an exact receipt. Keep the allocation held.
                    throw new InvalidOperationException("The asset expert requires valid total model token usage within its reservation.");
                }

                await run.SettleAsync(modelOperation, new(used, 0));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var contents = response.Messages.SelectMany(message => message.Contents).ToArray();
            var calls = contents.OfType<FunctionCallContent>().ToArray();
            if (contents.Any(content => content is FunctionResultContent) ||
                response.Messages.Any(message => message.Role != ChatRole.Assistant))
            {
                throw new InvalidOperationException("The asset expert received unexpected model content.");
            }

            if (calls.Length == 0)
            {
                return response.Text;
            }

            if (turn >= MaxToolCalls || calls.Length != 1)
            {
                throw new InvalidOperationException("The asset expert's tool call limit was exceeded.");
            }

            var call = calls[0];
            if (call.Name != AssetContractIds.GetTool)
            {
                throw new InvalidOperationException("The model requested a tool outside the asset expert's allowlist.");
            }

            if (string.IsNullOrWhiteSpace(call.CallId) || !callIds.Add(call.CallId) ||
                !TryReadAssetId(call.Arguments, out var assetId))
            {
                throw new InvalidOperationException("The asset lookup requires a new call ID and a valid assetId argument.");
            }

            if (!toolGranted)
            {
                throw new InvalidOperationException("The native run did not grant the asset lookup tool.");
            }

            if (!toolBudgetAvailable)
            {
                throw new InvalidOperationException("The asset expert's native tool call budget is exhausted.");
            }

            var arguments = new JsonObject { ["assetId"] = JsonValue.Create(assetId) };
            var payload = JsonElement.Parse(arguments.ToJsonString());
            var toolOperation = run is null ? Guid.Empty : await run.ReserveAsync(turn, "tool", new(0, 1), cancellationToken);
            AgentToolInvocationOutcome outcome;
            var toolUsage = default(AgentBudget);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                toolUsage = new(0, 1);
                outcome = await toolInvoker.InvokeAsync(new(AssetContractIds.GetTool, payload), cancellationToken);
            }
            finally
            {
                if (run is not null)
                {
                    await run.SettleAsync(toolOperation, toolUsage);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!outcome.IsSuccess)
            {
                throw new InvalidOperationException($"CrestCreates rejected the asset lookup ({outcome.Code}).");
            }

            messages.AddRange(response.Messages);
            messages.Add(new(ChatRole.Tool, [new FunctionResultContent(call.CallId, outcome.StructuredOutput?.Clone())]));
        }

        throw new InvalidOperationException("The asset expert's model turn limit was exceeded.");
    }

    private static bool TryReadAssetId(IDictionary<string, object?>? arguments, out Guid assetId)
    {
        assetId = Guid.Empty;
        if (arguments is not { Count: 1 } || !arguments.TryGetValue("assetId", out var value))
        {
            return false;
        }

        assetId = value switch
        {
            JsonElement { ValueKind: JsonValueKind.String } json when json.TryGetGuid(out var parsed) => parsed,
            string text when Guid.TryParseExact(text, "D", out var parsed) => parsed,
            Guid guid => guid,
            _ => Guid.Empty
        };
        return assetId != Guid.Empty;
    }

    private sealed class NativeRun(AgentRunCoordinator coordinator, AgentExecutionKey key, AgentRunOwner owner)
    {
        public ValueTask<AgentRunContext> ReadContextAsync(CancellationToken cancellationToken) =>
            coordinator.ReadContextAsync(key, owner, cancellationToken);

        public async ValueTask<Guid> ReserveAsync(int turn, string action, AgentBudget amount, CancellationToken cancellationToken)
        {
            var material = FormattableString.Invariant($"asset-maf-expert:{owner.RunId:N}:{owner.Epoch}:{turn}:{action}");
            var operationId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(material)).AsSpan(0, 16));
            if (!await coordinator.ReserveAsync(key, owner, operationId, amount, cancellationToken))
            {
                throw new InvalidOperationException("The asset expert's native operation has already been claimed.");
            }

            return operationId;
        }

        // Account for known usage even if the deadline or caller cancellation has arrived.
        public async ValueTask SettleAsync(Guid operationId, AgentBudget used)
        {
            if (!await coordinator.SettleAsync(key, owner, operationId, used))
            {
                throw new InvalidOperationException("The asset expert's native operation has already been settled.");
            }
        }
    }

    private sealed class GetAssetDeclaration : AIFunctionDeclaration
    {
        private static readonly JsonElement Schema = JsonElement.Parse("""
            {"type":"object","properties":{"assetId":{"type":"string","format":"uuid"}},"required":["assetId"],"additionalProperties":false}
            """);

        public override string Name => AssetContractIds.GetTool;
        public override string Description => "Gets one asset by ID within the current tenant and organization scope.";
        public override JsonElement JsonSchema => Schema;
    }
}
