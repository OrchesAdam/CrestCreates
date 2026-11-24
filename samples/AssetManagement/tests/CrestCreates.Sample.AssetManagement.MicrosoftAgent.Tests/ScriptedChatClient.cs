using Microsoft.Extensions.AI;

namespace CrestCreates.Sample.AssetManagement.MicrosoftAgent.Tests;

internal sealed class ScriptedChatClient(
    Func<int, ChatMessage[], ChatOptions?, CancellationToken, Task<ChatResponse>> respond) : IChatClient
{
    public int Turns { get; private set; }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default) => respond(++Turns, messages.ToArray(), options, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("scripted-provider") : null;

    public void Dispose() { }
}
