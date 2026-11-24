using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrestCreates.Agent.Orchestration;
using CrestCreates.Sample.AssetManagement.MicrosoftAgent;

namespace CrestCreates.Sample.AssetManagement.MicrosoftAgent.Tests;

internal sealed class LiveEvaluation : IAsyncDisposable
{
    private readonly DeepSeekLiveModel _deepSeek;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private Exception? _failure;

    public LiveEvaluation(string mode)
    {
        Mode = mode;
        _deepSeek = new DeepSeekLiveModel();
        Model = new MicrosoftAgentCoordinationModel(_deepSeek.Chat, _deepSeek.ModelId, _deepSeek.MaxOutputTokens);
        Coordinator = new AgentRunCoordinator(Store, TimeProvider.System);
        Key = new AgentExecutionKey("agent-live-eval", Guid.NewGuid());
    }

    public string Mode { get; }
    public AgentExecutionKey Key { get; }
    public InMemoryAgentExecutionStore Store { get; } = new();
    public AgentRunCoordinator Coordinator { get; }
    public MicrosoftAgentCoordinationModel Model { get; }
    public ConcurrentDictionary<string, string> Artifacts { get; } = new();
    public Dictionary<string, object?> Facts { get; } = new();
    public Exception? Failure { get => _failure; set => _failure = value; }
    public IReadOnlyList<HttpCallEvidence> HttpCalls => _deepSeek.Handler.Calls;
    public IReadOnlyList<ChatCallEvidence> ModelCalls => _deepSeek.Chat.Calls.ToArray();
    public int MaxConcurrentHttpRequests => _deepSeek.Handler.MaxActiveRequests;

    public void EnableConcurrencyBarrier(int count) => _deepSeek.Handler.EnableConcurrencyBarrier(count);

    public async ValueTask DisposeAsync()
    {
        try
        {
            var snapshot = await Store.FindAsync(Key, default);
            var evidence = new
            {
                Mode,
                RootRunId = Key.RootRunId,
                TenantId = Key.TenantId,
                StartedAt = _startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                Failure = _failure?.ToString(),
                RequestedModel = _deepSeek.ModelId,
                MaxOutputTokens = _deepSeek.MaxOutputTokens,
                HttpCalls = _deepSeek.Handler.Calls,
                MaxConcurrentHttpRequests = _deepSeek.Handler.MaxActiveRequests,
                ModelCalls = _deepSeek.Chat.Calls.ToArray(),
                Facts,
                Snapshot = snapshot
            };
            var directory = Environment.GetEnvironmentVariable("CREST_AGENT_LIVE_EVAL_ARTIFACT_DIR") ?? Path.GetTempPath();
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"crest-agent-live-eval-{Mode}-{Key.RootRunId:N}.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"AGENT_LIVE_EVAL_ARTIFACT {Mode} {path}");
        }
        finally
        {
            _deepSeek.Dispose();
        }
    }
}

internal static class LiveModelProtocol
{
    public static async Task<MicrosoftAgentCoordinationCompletion> CompleteMeteredAsync(
        MicrosoftAgentCoordinationModel model, AgentRunCoordinator coordinator, AgentRunContext context,
        string purpose, string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var current = await coordinator.ReadContextAsync(context.Execution, context.Owner, cancellationToken);
        var allocation = Math.Min(model.MaxOutputTokens, current.RemainingBudget.ModelTokens);
        if (allocation <= 0)
        {
            throw new InvalidOperationException("The live parent model token budget is exhausted.");
        }

        var operation = OperationId(context.Owner, purpose);
        if (!await coordinator.ReserveAsync(context.Execution, context.Owner, operation, new(allocation, 0), cancellationToken))
        {
            throw new InvalidOperationException("The live parent model operation has already been claimed.");
        }

        var completion = await model.CompleteAsync(systemPrompt, userPrompt, cancellationToken);
        if (!await coordinator.SettleAsync(context.Execution, context.Owner, operation,
                new(completion.Usage.TotalTokenCount!.Value, 0), cancellationToken))
        {
            throw new InvalidOperationException("The live parent model operation has already been settled.");
        }

        return completion;
    }

    public static AgentRunResult ReadResult(string text, Guid runId, string purpose)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("kind", out var kind) || kind.GetString() != "Returned" ||
            !result.TryGetProperty("code", out var code) || code.GetString() is not { } resultCode ||
            string.IsNullOrWhiteSpace(resultCode) ||
            !result.TryGetProperty("text", out var value) || value.GetString() is not { } resultText ||
            string.IsNullOrWhiteSpace(resultText))
        {
            throw new InvalidOperationException("The live model returned an invalid terminal result.");
        }

        return new(AgentResultKind.Returned, resultCode, Artifact($"{purpose}-{runId:N}", resultText));
    }

    public static AgentRunAssignment[] ReadPlan(string text, AgentRunAssignment[] templates)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("steps", out var steps) ||
            steps.ValueKind != JsonValueKind.Array || steps.GetArrayLength() != templates.Length)
        {
            throw new InvalidOperationException("The live planner returned the wrong number of steps.");
        }

        var result = new AgentRunAssignment[templates.Length];
        for (var index = 0; index < templates.Length; index++)
        {
            var step = steps[index];
            if (step.ValueKind != JsonValueKind.Object ||
                !step.TryGetProperty("index", out var position) || position.GetInt32() != index ||
                !step.TryGetProperty("task", out var task) || task.GetString() is not { } taskText ||
                string.IsNullOrWhiteSpace(taskText))
            {
                throw new InvalidOperationException("The live planner returned an invalid step.");
            }

            result[index] = templates[index] with { Task = taskText };
        }

        return result;
    }

    public static (string Member, string Task) ReadDelegation(string text)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("member", out var member) || member.GetString() is not { } memberId ||
            string.IsNullOrWhiteSpace(memberId) ||
            !root.TryGetProperty("task", out var task) || task.GetString() is not { } taskText ||
            string.IsNullOrWhiteSpace(taskText))
        {
            throw new InvalidOperationException("The live supervisor returned an invalid delegation.");
        }

        return (memberId, taskText);
    }

    public static (string Recipient, string Summary) ReadHandoff(string text)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("recipient", out var recipient) || recipient.GetString() is not { } recipientId ||
            string.IsNullOrWhiteSpace(recipientId) ||
            !root.TryGetProperty("summary", out var summary) || summary.GetString() is not { } summaryText ||
            string.IsNullOrWhiteSpace(summaryText))
        {
            throw new InvalidOperationException("The live triage model returned an invalid handoff.");
        }

        return (recipientId, summaryText);
    }

    public static AgentEventSelector ReadWait(string text)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("wait", out var wait) ||
            wait.ValueKind != JsonValueKind.Object ||
            !wait.TryGetProperty("source", out var source) || source.GetString() is not { } sourceId ||
            !wait.TryGetProperty("kind", out var kind) || kind.GetString() is not { } kindId ||
            !wait.TryGetProperty("subjectId", out var subject) || subject.GetString() is not { } subjectId ||
            string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(kindId) || string.IsNullOrWhiteSpace(subjectId))
        {
            throw new InvalidOperationException("The live reviewer returned an invalid event wait.");
        }

        return new(sourceId, kindId, subjectId);
    }

    public static AgentArtifactReference Artifact(string id, string text) =>
        new(id, "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant());

    private static Guid OperationId(AgentRunOwner owner, string purpose)
    {
        var material = $"{owner.RunId:N}:{owner.Epoch}:{purpose}";
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(material)).AsSpan(0, 16));
    }
}
