using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace CrestCreates.Sample.AssetManagement.MicrosoftAgent.Tests;

internal sealed class ScriptedChatClient(
    Func<int, ChatMessage[], ChatOptions?, CancellationToken, Task<ChatResponse>> respond) : IChatClient
{
    private int _turn;

    public int Turns => Volatile.Read(ref _turn);
    public ConcurrentQueue<ScriptedChatCall> Calls { get; } = new();

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var request = messages.ToArray();
        var turn = Interlocked.Increment(ref _turn);
        Calls.Enqueue(new ScriptedChatCall(turn, request.Select(message => new ChatMessageEvidence(
            message.Role.Value, message.Text, string.Join(",", message.Contents.Select(content => content.GetType().Name)))).ToArray(),
            options?.ModelId, options?.MaxOutputTokens, options?.ToolMode?.ToString()));
        return respond(turn, request, options, cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("scripted-provider") : null;

    public void Dispose() { }
}

internal sealed record ScriptedChatCall(
    int Turn, IReadOnlyList<ChatMessageEvidence> Messages, string? ModelId, int? MaxOutputTokens, string? ToolMode);
