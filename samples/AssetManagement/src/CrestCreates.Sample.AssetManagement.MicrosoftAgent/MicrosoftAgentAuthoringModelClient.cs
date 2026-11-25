using System.Net;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CrestCreates.Sample.AssetManagement.MicrosoftAgent;

/// <summary>Runs the CrestCreates prompt against a stateless provider chat client.</summary>
public sealed class MicrosoftAgentAuthoringModelClient(IChatClient chatClient) : IDescriptorAuthoringModelClient
{
    private readonly ChatClientAgent _agent = SingleTurnAgent.Create(chatClient);

    public async Task<DescriptorAuthoringModelResponse> CompleteAsync(
        DescriptorAuthoringModelRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var profile = request.ModelProfile;
        var response = new DescriptorAuthoringModelResponse
        {
            ResponseText = string.Empty,
            ProviderName = chatClient.GetService<ChatClientMetadata>()?.ProviderName ?? profile.ProviderName,
            ModelName = profile.ModelName,
            PromptInputHash = request.Prompt.PromptInputHash
        };
        var options = new ChatClientAgentRunOptions(new ChatOptions
        {
            ModelId = profile.ModelName,
            MaxOutputTokens = profile.MaxOutputTokens,
            Temperature = 0,
            ToolMode = ChatToolMode.None,
            ResponseFormat = profile.SupportsJsonMode || profile.SupportsStructuredOutput ? ChatResponseFormat.Json : null
        });

        try
        {
            var result = await _agent.RunAsync(
                [new(ChatRole.System, request.Prompt.SystemPrompt), new(ChatRole.User, request.Prompt.UserPrompt)],
                session: null, options: options, cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var observed = result.RawRepresentation as ChatResponse;
            response = response with { ModelName = observed?.ModelId ?? profile.ModelName };
            if (result.Messages.SelectMany(message => message.Contents).Any(content =>
                    content is FunctionCallContent or FunctionResultContent))
            {
                return response with
                {
                    FailureKind = DescriptorAuthoringProviderFailureKind.Unknown,
                    FailureDetail = "The authoring model returned tool content with tools disabled."
                };
            }

            // CrestCreates owns parsing and validating the authoring wire contract.
            return response with { ResponseText = result.Text };
        }
        catch (HttpRequestException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return response with
            {
                FailureKind = exception.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => DescriptorAuthoringProviderFailureKind.Unauthorized,
                    HttpStatusCode.Forbidden => DescriptorAuthoringProviderFailureKind.CredentialRejected,
                    HttpStatusCode.TooManyRequests => DescriptorAuthoringProviderFailureKind.RateLimited,
                    HttpStatusCode.RequestTimeout => DescriptorAuthoringProviderFailureKind.Timeout,
                    _ => DescriptorAuthoringProviderFailureKind.NetworkError
                }
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return response with { FailureKind = DescriptorAuthoringProviderFailureKind.Timeout };
        }
    }
}
