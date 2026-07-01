using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using CrestCreates.Agent.Authoring.Http.Credentials;
using Microsoft.Extensions.Options;

namespace CrestCreates.Agent.Authoring.Http.OpenAICompatible;

public sealed class OpenAICompatibleDescriptorAuthoringModelClient : IDescriptorAuthoringModelClient
{
    private readonly HttpClient _httpClient;
    private readonly IDescriptorAuthoringCredentialProvider _credentialProvider;
    private readonly DescriptorAuthoringProviderProfile _providerProfile;

    public OpenAICompatibleDescriptorAuthoringModelClient(
        HttpClient httpClient,
        IDescriptorAuthoringCredentialProvider credentialProvider,
        IOptions<DescriptorAuthoringProviderProfile> providerProfile)
    {
        _httpClient = httpClient;
        _credentialProvider = credentialProvider;
        _providerProfile = providerProfile.Value;
    }

    public async Task<DescriptorAuthoringModelResponse> CompleteAsync(
        DescriptorAuthoringModelRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Resolve credentials
        string apiKey;
        try
        {
            apiKey = await _credentialProvider.GetApiKeyAsync(
                _providerProfile.CredentialReference ?? "Authoring:Llm:ApiKey",
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DescriptorAuthoringModelResponse
            {
                ResponseText = string.Empty,
                ProviderName = _providerProfile.ProviderName,
                ModelName = request.ModelProfile.ModelName,
                PromptInputHash = request.Prompt.PromptInputHash,
                FailureKind = DescriptorAuthoringProviderFailureKind.CredentialUnavailable,
                FailureDetail = ex.Message
            };
        }

        // 2. Build OpenAI-compatible request
        var chatRequest = new OpenAICompatibleChatRequest
        {
            Model = request.ModelProfile.ModelName,
            Temperature = 0.0,
            MaxTokens = request.ModelProfile.MaxOutputTokens,
            Messages = new List<OpenAICompatibleChatMessage>
            {
                new() { Role = "system", Content = request.Prompt.SystemPrompt },
                new() { Role = "user", Content = request.Prompt.UserPrompt }
            }
        };

        if (request.ModelProfile.SupportsJsonMode)
        {
            chatRequest.ResponseFormat = new OpenAICompatibleResponseFormat { Type = "json_object" };
        }

        // 3. Send request - set authorization on each request, NOT on DefaultRequestHeaders
        var endpoint = _providerProfile.Endpoint ?? new Uri("https://api.openai.com/v1/chat/completions");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(chatRequest),
            Encoding.UTF8,
            "application/json");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            using var response = await _httpClient.SendAsync(
                httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            // 4. Handle error responses
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new DescriptorAuthoringModelResponse
                {
                    ResponseText = string.Empty,
                    ProviderName = _providerProfile.ProviderName,
                    ModelName = request.ModelProfile.ModelName,
                    PromptInputHash = request.Prompt.PromptInputHash,
                    FailureKind = DescriptorAuthoringProviderFailureKind.Unauthorized,
                    FailureDetail = "HTTP 401 Unauthorized"
                };
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return new DescriptorAuthoringModelResponse
                {
                    ResponseText = string.Empty,
                    ProviderName = _providerProfile.ProviderName,
                    ModelName = request.ModelProfile.ModelName,
                    PromptInputHash = request.Prompt.PromptInputHash,
                    FailureKind = DescriptorAuthoringProviderFailureKind.CredentialRejected,
                    FailureDetail = "Provider rejected credentials (HTTP 403 Forbidden)."
                };
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new DescriptorAuthoringModelResponse
                {
                    ResponseText = string.Empty,
                    ProviderName = _providerProfile.ProviderName,
                    ModelName = request.ModelProfile.ModelName,
                    PromptInputHash = request.Prompt.PromptInputHash,
                    FailureKind = DescriptorAuthoringProviderFailureKind.RateLimited,
                    FailureDetail = "HTTP 429 Too Many Requests"
                };
            }

            response.EnsureSuccessStatusCode();

            // 5. Parse response
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var chatResponse = JsonSerializer.Deserialize<OpenAICompatibleChatResponse>(
                responseBody, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            var responseText = chatResponse?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;

            return new DescriptorAuthoringModelResponse
            {
                ResponseText = responseText,
                ProviderName = _providerProfile.ProviderName,
                ModelName = chatResponse?.Model ?? request.ModelProfile.ModelName,
                PromptInputHash = request.Prompt.PromptInputHash
            };
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return new DescriptorAuthoringModelResponse
            {
                ResponseText = string.Empty,
                ProviderName = _providerProfile.ProviderName,
                ModelName = request.ModelProfile.ModelName,
                PromptInputHash = request.Prompt.PromptInputHash,
                FailureKind = DescriptorAuthoringProviderFailureKind.NetworkError,
                FailureDetail = ex.Message
            };
        }
        catch (TaskCanceledException)
        {
            // Timeout
            return new DescriptorAuthoringModelResponse
            {
                ResponseText = string.Empty,
                ProviderName = _providerProfile.ProviderName,
                ModelName = request.ModelProfile.ModelName,
                PromptInputHash = request.Prompt.PromptInputHash,
                FailureKind = DescriptorAuthoringProviderFailureKind.Timeout,
                FailureDetail = "Request timed out"
            };
        }
    }
}
