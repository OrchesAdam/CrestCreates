using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace CrestCreates.Sample.AssetManagement.MicrosoftAgent.Tests;

internal sealed class DeepSeekLiveModel : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IChatClient _chatClient;

    public DeepSeekLiveModel()
    {
        var apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("DEEPSEEK_API_KEY is required for the live coordination evaluation.");
        }

        ModelId = Environment.GetEnvironmentVariable("CREST_AGENT_LIVE_EVAL_MODEL") ?? "deepseek-v4-flash";
        MaxOutputTokens = ReadBudget();
        var endpoint = Environment.GetEnvironmentVariable("CREST_AGENT_LIVE_EVAL_ENDPOINT") ?? "https://api.deepseek.com/v1";
        Handler = new RecordingHttpHandler(new HttpClientHandler());
        _httpClient = new HttpClient(Handler) { Timeout = TimeSpan.FromMinutes(4) };
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint, UriKind.Absolute),
            Transport = new HttpClientPipelineTransport(_httpClient),
            RetryPolicy = new ClientRetryPolicy(0),
            NetworkTimeout = TimeSpan.FromMinutes(4)
        };
        _chatClient = new OpenAI.Chat.ChatClient(ModelId, new ApiKeyCredential(apiKey), options).AsIChatClient();
        Chat = new RecordingChatClient(_chatClient);
    }

    public string ModelId { get; }
    public int MaxOutputTokens { get; }
    public RecordingHttpHandler Handler { get; }
    public RecordingChatClient Chat { get; }

    public void Dispose()
    {
        Chat.Dispose();
        _httpClient.Dispose();
    }

    private static int ReadBudget()
    {
        var raw = Environment.GetEnvironmentVariable("CREST_AGENT_LIVE_EVAL_MAX_OUTPUT_TOKENS");
        var value = string.IsNullOrWhiteSpace(raw) ? 4096 : int.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        if (value is < 256 or > 16384)
        {
            throw new InvalidOperationException("CREST_AGENT_LIVE_EVAL_MAX_OUTPUT_TOKENS must be between 256 and 16384.");
        }

        return value;
    }
}

internal sealed class RecordingChatClient(IChatClient inner) : IChatClient
{
    private int _turn;

    public ConcurrentQueue<ChatCallEvidence> Calls { get; } = new();

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var original = messages.ToArray();
        var requestMessages = original.Select(message => new ChatMessageEvidence(
            message.Role.Value, message.Text, string.Join(",", message.Contents.Select(content => content.GetType().Name)))).ToArray();
        return CaptureAsync(original, requestMessages, options, cancellationToken);
    }

    private async Task<ChatResponse> CaptureAsync(
        ChatMessage[] original, ChatMessageEvidence[] messages, ChatOptions? options, CancellationToken cancellationToken)
    {
        var response = await inner.GetResponseAsync(original, options, cancellationToken);
        var text = response.Text;
        Calls.Enqueue(new ChatCallEvidence(
            Interlocked.Increment(ref _turn), messages, options?.ModelId, options?.MaxOutputTokens,
            options?.ResponseFormat?.ToString(), options?.ToolMode?.ToString(), response.ModelId,
            response.FinishReason.ToString(), response.Usage is null ? null : new TokenUsageEvidence(
                response.Usage.InputTokenCount, response.Usage.OutputTokenCount, response.Usage.TotalTokenCount),
            text, Hash(text), string.Join(",", response.Messages.SelectMany(message => message.Contents)
                .Select(content => content.GetType().Name))));
        return response;
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => inner.GetService(serviceType, serviceKey);

    public void Dispose() => inner.Dispose();

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

internal sealed record ChatMessageEvidence(string Role, string Text, string ContentTypes);

internal sealed record TokenUsageEvidence(long? Input, long? Output, long? Total);

internal sealed record ChatCallEvidence(
    int Turn, IReadOnlyList<ChatMessageEvidence> Messages, string? RequestedModel, int? MaxOutputTokens,
    string? ResponseFormat, string? ToolMode, string? ObservedModel, string? FinishReason,
    TokenUsageEvidence? Usage, string ResponseText, string ResponseHash, string ResponseContentTypes);

internal sealed record HttpCallEvidence(string Method, string Uri, HttpStatusCode Status, DateTimeOffset ObservedAt);

internal sealed class RecordingHttpHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private readonly ConcurrentQueue<HttpCallEvidence> _calls = new();
    private readonly object _barrierGate = new();
    private TaskCompletionSource? _barrier;
    private int _barrierCount;
    private int _active;
    private int _maxActive;

    public IReadOnlyList<HttpCallEvidence> Calls => _calls.ToArray();
    public int MaxActiveRequests => Volatile.Read(ref _maxActive);

    public void EnableConcurrencyBarrier(int count)
    {
        lock (_barrierGate)
        {
            _barrierCount = count;
            _barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var active = Interlocked.Increment(ref _active);
        while (true)
        {
            var max = Volatile.Read(ref _maxActive);
            if (active <= max || Interlocked.CompareExchange(ref _maxActive, active, max) == max)
            {
                break;
            }
        }

        TaskCompletionSource? barrier;
        int barrierCount;
        lock (_barrierGate)
        {
            barrier = _barrier;
            barrierCount = _barrierCount;
        }

        if (barrier is not null && active >= barrierCount)
        {
            barrier.TrySetResult();
        }

        try
        {
            if (barrier is not null)
            {
                await barrier.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }

            var response = await base.SendAsync(request, cancellationToken);
            _calls.Enqueue(new HttpCallEvidence(request.Method.Method, request.RequestUri!.ToString(), response.StatusCode, DateTimeOffset.UtcNow));
            return response;
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }
}
