using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace CrestCreates.Agent.Orchestration;

public sealed record AgentToolExchange(AgentToolSelection Selection, AgentToolObservation Observation);

public sealed record AgentModelTurnRequest(
    AgentRunContext Context, int Round, long TokenLimit, ImmutableArray<AgentToolExchange> PreviousExchanges);

public sealed record AgentModelTurnResponse(long UsedTokens, AgentModelDecision Decision);

public interface IAgentModelTurnExecutor
{
    ValueTask<AgentModelTurnResponse> ExecuteAsync(AgentModelTurnRequest request, CancellationToken cancellationToken);
}

public interface IAgentRunToolExecutor
{
    ValueTask<AgentToolObservation> InvokeAsync(
        AgentRunContext context, Guid operationId, AgentToolSelection selection, CancellationToken cancellationToken);
}

public sealed class AgentReActRunner(AgentRunCoordinator coordinator, TimeProvider clock)
{
    public async ValueTask<AgentRunResult> RunAsync(
        AgentExecutionKey key, AgentRunOwner owner, IAgentModelTurnExecutor model, IAgentRunToolExecutor tools,
        int maxRounds, long maxTokensPerRound, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRounds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTokensPerRound);
        var initial = await coordinator.ReadRunStateAsync(key, owner, cancellationToken);
        if (initial.Result is { } previousResult)
        {
            return previousResult;
        }

        var context = await coordinator.ReadContextAsync(key, owner, cancellationToken);
        var remainingTime = context.Deadline - clock.GetUtcNow();
        if (remainingTime <= TimeSpan.Zero)
        {
            throw new TimeoutException("The agent run deadline elapsed.");
        }

        using var timeout = new CancellationTokenSource(remainingTime, clock);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var transcript = ImmutableArray.CreateBuilder<AgentToolExchange>();
        for (var index = 0; index < maxRounds; index++)
        {
            var token = stop.Token;
            context = await coordinator.ReadContextAsync(key, owner, token);
            var snapshot = await coordinator.ReadRunStateAsync(key, owner, token);
            var modelOperation = OperationId(owner, index, 0);
            var toolOperation = OperationId(owner, index, 1);
            var modelReceipt = ReadReceipt(snapshot, owner, modelOperation);
            var decision = modelReceipt?.ModelDecision;
            if (decision is null)
            {
                var allocation = Math.Min(maxTokensPerRound, context.RemainingBudget.ModelTokens);
                if (allocation <= 0 && modelReceipt is null)
                {
                    return await coordinator.FinishAsync(key, owner, new(AgentResultKind.Failed, "ModelBudgetExhausted"), token);
                }

                await ClaimAsync(key, owner, modelOperation, new(allocation, 0), modelReceipt, token);
                var response = await model.ExecuteAsync(new(context, index + 1, allocation, transcript.ToImmutable()), token);
                decision = CheckDecision(response.Decision, context);
                await coordinator.RecordModelDecisionAsync(key, owner, modelOperation, response.UsedTokens, decision, cancellationToken);
            }

            if (decision.Result is { } result)
            {
                return await coordinator.FinishAsync(key, owner, result, token);
            }

            var selection = decision.Tool!;
            var toolReceipt = ReadReceipt(snapshot, owner, toolOperation);
            var observation = toolReceipt?.ToolObservation;
            if (observation is null)
            {
                context = await coordinator.ReadContextAsync(key, owner, token);
                if (context.RemainingBudget.ToolCalls <= 0 && toolReceipt is null)
                {
                    return await coordinator.FinishAsync(key, owner, new(AgentResultKind.Failed, "ToolBudgetExhausted"), token);
                }

                await ClaimAsync(key, owner, toolOperation, new(0, 1), toolReceipt, token);
                observation = await tools.InvokeAsync(context, toolOperation, selection, token);
                ArgumentException.ThrowIfNullOrWhiteSpace(observation.Code);
                observation = observation with { StructuredOutput = observation.StructuredOutput?.Clone() };
                await coordinator.RecordToolObservationAsync(key, owner, toolOperation, observation, cancellationToken);
            }

            transcript.Add(new(selection, observation));
        }

        return await coordinator.FinishAsync(key, owner, new(AgentResultKind.Failed, "RoundLimitReached"), stop.Token);
    }

    private static AgentBudgetReservation? ReadReceipt(AgentRunSnapshot snapshot, AgentRunOwner owner, Guid operation)
    {
        // Legacy receipts can be recovered by ReAct's operation ID, which already incorporates the owner epoch.
        return snapshot.Reservations.TryGetValue(operation, out var receipt) &&
            (receipt.OwnerEpoch == owner.Epoch || receipt.OwnerEpoch == 0) ? receipt : null;
    }

    private async ValueTask ClaimAsync(AgentExecutionKey key, AgentRunOwner owner, Guid operation,
        AgentBudget budget, AgentBudgetReservation? receipt, CancellationToken cancellationToken)
    {
        if (receipt is not null || !await coordinator.ReserveAsync(key, owner, operation, budget, cancellationToken))
        {
            throw new InvalidOperationException($"Agent operation {operation:N} has no confirmed receipt and requires reconciliation.");
        }
    }

    private static AgentModelDecision CheckDecision(AgentModelDecision decision, AgentRunContext context)
    {
        if (decision is { Tool: null, Result.Kind: AgentResultKind.Returned or AgentResultKind.Failed })
        {
            return decision;
        }

        if (decision is not { Tool: { } tool, Result: null } || string.IsNullOrWhiteSpace(tool.Name) ||
            tool.Arguments is { ValueKind: not JsonValueKind.Object })
        {
            return new(null, new(AgentResultKind.Failed, "InvalidModelDecision"));
        }

        if (!context.Tools.Contains(tool.Name, StringComparer.Ordinal))
        {
            return new(null, new(AgentResultKind.Failed, "ToolNotGranted"));
        }

        return decision with { Tool = tool with { Arguments = tool.Arguments?.Clone() } };
    }

    private static Guid OperationId(AgentRunOwner owner, int round, byte action)
    {
        Span<byte> material = stackalloc byte[29];
        owner.RunId.TryWriteBytes(material);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(material[16..], owner.Epoch);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(material[24..], round);
        material[28] = action;
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(material, hash);
        return new(hash[..16]);
    }
}
