using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CrestCreates.Sample.AssetManagement.MicrosoftAgent;

public sealed record MicrosoftAgentCoordinationCompletion(
    string Text, string ModelId, ChatFinishReason? FinishReason, UsageDetails Usage);

public sealed class MicrosoftAgentCoordinationModel(
    IChatClient chatClient, string modelId, int maxOutputTokens = 4096)
{
    private readonly ChatClientAgent _agent = SingleTurnAgent.Create(chatClient);

    public string ModelId { get; } = modelId;
    public int MaxOutputTokens { get; } = maxOutputTokens > 0 ? maxOutputTokens :
        throw new ArgumentOutOfRangeException(nameof(maxOutputTokens));

    public async Task<MicrosoftAgentCoordinationCompletion> CompleteAsync(
        string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);
        var result = await _agent.RunAsync(
            [new(ChatRole.System, systemPrompt), new(ChatRole.User, userPrompt)],
            session: null,
            options: new ChatClientAgentRunOptions(new ChatOptions
            {
                ModelId = ModelId,
                MaxOutputTokens = MaxOutputTokens,
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

        var observed = result.RawRepresentation as ChatResponse;
        return new(result.Text, observed?.ModelId ?? ModelId, observed?.FinishReason, result.Usage);
    }
}
