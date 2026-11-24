using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CrestCreates.Sample.AssetManagement.MicrosoftAgent;

internal static class SingleTurnAgent
{
    public static ChatClientAgent Create(IChatClient chatClient) => new(chatClient, new ChatClientAgentOptions
    {
        UseProvidedChatClientAsIs = true,
        ChatHistoryProvider = new NoHistoryProvider(),
        // Every call uses a fresh session, even when the provider returns a conversation ID.
        ThrowOnChatHistoryProviderConflict = false,
        WarnOnChatHistoryProviderConflict = false
    });

    private sealed class NoHistoryProvider : ChatHistoryProvider
    {
        protected override ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
            InvokingContext context, CancellationToken cancellationToken) => ValueTask.FromResult<IEnumerable<ChatMessage>>([]);

        protected override ValueTask StoreChatHistoryAsync(InvokedContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
