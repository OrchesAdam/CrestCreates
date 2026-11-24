using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrestCreates.Agent.Orchestration;

namespace CrestCreates.Sample.AssetManagement.MicrosoftAgent;

public sealed class MicrosoftAgentRunWorker(
    MicrosoftAgentCoordinationModel model,
    AgentRunCoordinator coordinator,
    string rolePrompt,
    ConcurrentDictionary<string, string>? artifacts = null) : IAgentRunWorker
{
    private readonly ConcurrentDictionary<string, string> _artifacts = artifacts ?? new();

    public async ValueTask<AgentRunResult> RunAsync(
        AgentRunContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var current = await coordinator.ReadContextAsync(context.Execution, context.Owner, cancellationToken);
        var allocation = Math.Min(model.MaxOutputTokens, current.RemainingBudget.ModelTokens);
        if (allocation <= 0)
        {
            throw new InvalidOperationException("The coordination worker's native model token budget is exhausted.");
        }

        var operation = OperationId(context.Owner);
        if (!await coordinator.ReserveAsync(context.Execution, context.Owner, operation, new(allocation, 0), cancellationToken))
        {
            throw new InvalidOperationException("The coordination worker's native operation has already been claimed.");
        }

        var input = current.Inputs.Select(reference => new CoordinationInputPrompt(
            reference.Id, reference.ContentHash,
            _artifacts.TryGetValue(reference.Id, out var value) ? value : null)).ToArray();
        var resumed = current.ResumedBy is null ? null : new CoordinationResumedPrompt(
            current.ResumedBy.EventId, current.ResumedBy.Selector.Source, current.ResumedBy.Selector.Kind,
            current.ResumedBy.Selector.SubjectId,
            _artifacts.TryGetValue(current.ResumedBy.Payload.Id, out var value) ? value : null);
        var prompt = $$"""
            Agent: {{current.Owner.AgentId}}
            Task: {{current.Task}}
            Available tools: {{string.Join(", ", current.Tools)}}
            Explicit input artifacts: {{JsonSerializer.Serialize(input, MicrosoftAgentCoordinationJsonContext.Default.CoordinationInputPromptArray)}}
            Resumed event: {{JsonSerializer.Serialize(resumed, MicrosoftAgentCoordinationJsonContext.Default.CoordinationResumedPrompt)}}

            Return exactly {"reply":"<concise answer>"}. Use only the task and explicit inputs above.
            Do not claim access to private history, other agents, tools, or events that are not present.
            """;
        var completion = await model.CompleteAsync(rolePrompt, prompt, cancellationToken);
        if (!await coordinator.SettleAsync(context.Execution, context.Owner, operation,
                new(completion.Usage.TotalTokenCount!.Value, 0), cancellationToken))
        {
            throw new InvalidOperationException("The coordination worker's native operation has already been settled.");
        }

        using var document = JsonDocument.Parse(completion.Text);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("reply", out var reply) || reply.GetString() is not { } text ||
            string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("The coordination worker returned an invalid reply.");
        }

        var artifact = new AgentArtifactReference(
            $"{current.Owner.AgentId}-{current.Owner.RunId:N}",
            "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant());
        _artifacts[artifact.Id] = text;
        return new(AgentResultKind.Returned, "ReportReady", artifact);
    }

    private static Guid OperationId(AgentRunOwner owner)
    {
        Span<byte> material = stackalloc byte[24];
        owner.RunId.TryWriteBytes(material);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(material[16..], owner.Epoch);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(material, hash);
        return new(hash[..16]);
    }
}
