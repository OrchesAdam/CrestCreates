using System.Net;
using System.Text.Json;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

/// <summary>
/// Test-owned observation of provider response metadata. It intentionally
/// records no response text, reasoning text, headers, or credentials.
/// </summary>
public sealed class AssetLiveResponseObservation
{
    private readonly object _gate = new();
    private AssetLiveResponseMetadata _latest = AssetLiveResponseMetadata.Empty;

    public AssetLiveResponseMetadata Snapshot()
    {
        lock (_gate)
            return _latest;
    }

    internal void Capture(HttpStatusCode statusCode, string? responseBody)
    {
        var metadata = AssetLiveResponseMetadata.From(statusCode, responseBody);
        lock (_gate)
            _latest = metadata;
    }
}

public sealed record AssetLiveResponseMetadata(
    int HttpStatus,
    int ChoicesCount,
    string FinishReason,
    int ContentCharacterCount,
    int ReasoningContentCharacterCount,
    int? PromptTokens,
    int? CompletionTokens,
    int? ReasoningTokens)
{
    public static AssetLiveResponseMetadata Empty { get; } = new(
        0, 0, "unknown", 0, 0, null, null, null);

    internal static AssetLiveResponseMetadata From(HttpStatusCode statusCode, string? responseBody)
    {
        var metadata = new MutableMetadata { HttpStatus = (int)statusCode };
        if (string.IsNullOrWhiteSpace(responseBody))
            return metadata.ToRecord();

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return metadata.ToRecord();

            if (root.TryGetProperty("choices", out var choices)
                && choices.ValueKind == JsonValueKind.Array)
            {
                metadata.ChoicesCount = choices.GetArrayLength();
                foreach (var choice in choices.EnumerateArray())
                {
                    if (choice.ValueKind != JsonValueKind.Object)
                        continue;

                    if (choice.TryGetProperty("finish_reason", out var finishReason)
                        && finishReason.ValueKind == JsonValueKind.String)
                    {
                        metadata.FinishReason = NormalizeFinishReason(finishReason.GetString());
                    }

                    if (!choice.TryGetProperty("message", out var message)
                        || message.ValueKind != JsonValueKind.Object)
                        continue;

                    metadata.ContentCharacterCount += GetStringLength(message, "content");
                    metadata.ReasoningContentCharacterCount += GetStringLength(message, "reasoning_content");
                }
            }

            if (root.TryGetProperty("usage", out var usage)
                && usage.ValueKind == JsonValueKind.Object)
            {
                metadata.PromptTokens = GetInt32(usage, "prompt_tokens");
                metadata.CompletionTokens = GetInt32(usage, "completion_tokens");
                metadata.ReasoningTokens = GetInt32(usage, "reasoning_tokens");
                if (metadata.ReasoningTokens is null
                    && usage.TryGetProperty("completion_tokens_details", out var details)
                    && details.ValueKind == JsonValueKind.Object)
                {
                    metadata.ReasoningTokens = GetInt32(details, "reasoning_tokens");
                }
            }
        }
        catch (JsonException)
        {
            // The existing provider client owns response parsing. Observation
            // must remain best-effort and must not change that behavior.
        }

        return metadata.ToRecord();
    }

    private static string NormalizeFinishReason(string? value)
        => value is "stop" or "length" or "tool_calls" or "content_filter"
            ? value
            : "unknown";

    private static int GetStringLength(JsonElement parent, string propertyName)
        => parent.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Length ?? 0
            : 0;

    private static int? GetInt32(JsonElement parent, string propertyName)
        => parent.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
            ? number
            : null;

    private sealed class MutableMetadata
    {
        public int HttpStatus { get; init; }
        public int ChoicesCount { get; set; }
        public string FinishReason { get; set; } = "unknown";
        public int ContentCharacterCount { get; set; }
        public int ReasoningContentCharacterCount { get; set; }
        public int? PromptTokens { get; set; }
        public int? CompletionTokens { get; set; }
        public int? ReasoningTokens { get; set; }

        public AssetLiveResponseMetadata ToRecord() => new(
            HttpStatus,
            ChoicesCount,
            FinishReason,
            ContentCharacterCount,
            ReasoningContentCharacterCount,
            PromptTokens,
            CompletionTokens,
            ReasoningTokens);
    }
}

public sealed class AssetLiveResponseObservationHandler : DelegatingHandler
{
    private readonly AssetLiveResponseObservation _observation;

    public AssetLiveResponseObservationHandler(AssetLiveResponseObservation observation)
        => _observation = observation;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        _observation.Capture(response.StatusCode, responseBody);
        return response;
    }
}
