using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrestCreates.Agent.Orchestration;

namespace CrestCreates.Sample.AssetManagement.MicrosoftAgent;

public sealed class MicrosoftAgentReActModelExecutor(MicrosoftAgentCoordinationModel model) : IAgentModelTurnExecutor
{
    public async ValueTask<AgentModelTurnResponse> ExecuteAsync(
        AgentModelTurnRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var exchanges = request.PreviousExchanges.Select(exchange => new CoordinationToolExchangePrompt(
            exchange.Selection.Name, exchange.Selection.Arguments, exchange.Observation.Code,
            exchange.Observation.Succeeded, exchange.Observation.StructuredOutput)).ToArray();
        var prompt = string.Join("\n",
            $"Task: {request.Context.Task}",
            $"Round: {request.Round}",
            $"Token limit: {request.TokenLimit}",
            $"Allowed tools: {string.Join(", ", request.Context.Tools)}",
            $"Previous tool exchanges: {JsonSerializer.Serialize(exchanges, MicrosoftAgentCoordinationJsonContext.Default.CoordinationToolExchangePromptArray)}",
            "",
            "Return exactly one JSON object using one of these shapes:",
            "{\"tool\":{\"name\":\"<allowed tool>\",\"arguments\":{...}}}",
            "{\"result\":{\"kind\":\"Returned\",\"code\":\"ReportReady\",\"text\":\"<concise evidence-based answer>\"}}",
            "",
            "Use a tool only when the task requires external evidence. After observing a tool result, return the result shape.",
            "Do not invent artifact IDs or hashes. CrestCreates creates the result artifact.");
        var completion = await model.CompleteAsync(
            "You are a CrestCreates ReAct worker. Return strict JSON only, with no prose and no tool-call envelope.",
            prompt, cancellationToken);
        using var document = JsonDocument.Parse(completion.Text);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("The ReAct model response must be a JSON object.");
        }

        var hasTool = root.TryGetProperty("tool", out var tool);
        var hasResult = root.TryGetProperty("result", out var result);
        if (hasTool == hasResult)
        {
            throw new InvalidOperationException("The ReAct model response must contain exactly one of tool or result.");
        }

        AgentModelDecision decision;
        if (hasResult)
        {
            decision = new(null, ReadResult(result, request.Context.Owner, request.Round));
        }
        else
        {
            if (tool.ValueKind != JsonValueKind.Object ||
                !tool.TryGetProperty("name", out var name) || name.GetString() is not { } toolName ||
                string.IsNullOrWhiteSpace(toolName) ||
                !tool.TryGetProperty("arguments", out var arguments) || arguments.ValueKind != JsonValueKind.Object ||
                !request.Context.Tools.Contains(toolName, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("The ReAct model requested an invalid or ungranted tool.");
            }

            decision = new(new(toolName, arguments.Clone()), null);
        }

        return new(completion.Usage.TotalTokenCount!.Value, decision);
    }

    private static AgentRunResult ReadResult(JsonElement result, AgentRunOwner owner, int round)
    {
        if (result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("kind", out var kind) || kind.GetString() is not { } resultKind ||
            !result.TryGetProperty("code", out var code) || code.GetString() is not { } resultCode ||
            string.IsNullOrWhiteSpace(resultCode) ||
            !result.TryGetProperty("text", out var text) || text.GetString() is not { } resultText ||
            string.IsNullOrWhiteSpace(resultText))
        {
            throw new InvalidOperationException("The ReAct model returned an invalid result.");
        }

        return resultKind switch
        {
            "Returned" => new(AgentResultKind.Returned, resultCode,
                new($"react-{owner.RunId:N}-{round}", Hash(resultText))),
            "Failed" => new(AgentResultKind.Failed, resultCode),
            _ => throw new InvalidOperationException($"The ReAct model returned unsupported result kind '{resultKind}'.")
        };
    }

    private static string Hash(string value) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
