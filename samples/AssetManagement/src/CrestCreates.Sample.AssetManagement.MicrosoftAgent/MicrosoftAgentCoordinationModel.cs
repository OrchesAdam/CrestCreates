using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CrestCreates.Sample.AssetManagement.MicrosoftAgent;

public sealed record MicrosoftAgentCoordinationCompletion(
    string Text, string ModelId, ChatFinishReason? FinishReason, UsageDetails Usage);

/// <summary>Limits one coordination call by total input plus output token usage.</summary>
public sealed class MicrosoftAgentCoordinationCallOptions(long totalTokenLimit)
{
    private const long MessageOverheadReserve = 64;

    public long TotalTokenLimit { get; } = totalTokenLimit > 0
        ? totalTokenLimit
        : throw new ArgumentOutOfRangeException(nameof(totalTokenLimit));

    // One token per UTF-8 byte plus fixed message framing is deliberately more conservative than
    // character-based heuristics for JSON-heavy prompts. Callers still validate reported total usage.
    internal long EstimateInputTokens(string systemPrompt, string userPrompt) => checked(
        Encoding.UTF8.GetByteCount(systemPrompt) + Encoding.UTF8.GetByteCount(userPrompt) + MessageOverheadReserve);
}

public sealed class MicrosoftAgentCoordinationModel(
    IChatClient chatClient, string modelId, int maxOutputTokens = 4096)
{
    private readonly ChatClientAgent _agent = SingleTurnAgent.Create(chatClient);

    public string ModelId { get; } = modelId;
    public int MaxOutputTokens { get; } = maxOutputTokens > 0 ? maxOutputTokens :
        throw new ArgumentOutOfRangeException(nameof(maxOutputTokens));

    public Task<MicrosoftAgentCoordinationCompletion> CompleteAsync(
        string systemPrompt, string userPrompt, CancellationToken cancellationToken = default) =>
        CompleteAsync(systemPrompt, userPrompt, options: null, cancellationToken);

    public async Task<MicrosoftAgentCoordinationCompletion> CompleteAsync(
        string systemPrompt, string userPrompt, MicrosoftAgentCoordinationCallOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);
        var maxOutputTokens = MaxOutputTokens;
        if (options is not null)
        {
            var outputBudget = options.TotalTokenLimit - options.EstimateInputTokens(systemPrompt, userPrompt);
            if (outputBudget <= 0)
            {
                throw new InvalidOperationException(
                    "The coordination call has no safe output budget after reserving tokens for its input.");
            }

            maxOutputTokens = (int)Math.Min(maxOutputTokens, outputBudget);
        }

        var result = await _agent.RunAsync(
            [new(ChatRole.System, systemPrompt), new(ChatRole.User, userPrompt)],
            session: null,
            options: new ChatClientAgentRunOptions(new ChatOptions
            {
                ModelId = ModelId,
                MaxOutputTokens = maxOutputTokens,
                Temperature = 0,
                ToolMode = ChatToolMode.None,
                ResponseFormat = ChatResponseFormat.Json
            }),
            cancellationToken: cancellationToken).WaitAsync(cancellationToken);

        if (result.Messages.SelectMany(message => message.Contents).Any(content =>
                content is FunctionCallContent or FunctionResultContent))
        {
            throw new InvalidOperationException("The coordination model returned tool content while tools were disabled.");
        }

        if (string.IsNullOrWhiteSpace(result.Text))
        {
            throw new InvalidOperationException("The coordination model returned no text.");
        }

        if (result.Usage?.TotalTokenCount is not { } used || used <= 0)
        {
            throw new InvalidOperationException("The coordination model did not report valid total token usage.");
        }

        if (options is not null && used > options.TotalTokenLimit)
        {
            throw new InvalidOperationException("The coordination model exceeded its reserved total token budget.");
        }

        var observed = result.RawRepresentation as ChatResponse;
        return new(result.Text, observed?.ModelId ?? ModelId, observed?.FinishReason, result.Usage);
    }
}
