using System.Text.Json.Serialization;

namespace CrestCreates.Agent.Authoring.Http.OpenAICompatible;

public sealed class OpenAICompatibleChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<OpenAICompatibleChatMessage> Messages { get; set; } = new();

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.0;

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; set; }

    [JsonPropertyName("response_format")]
    public OpenAICompatibleResponseFormat? ResponseFormat { get; set; }
}

public sealed class OpenAICompatibleChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

public sealed class OpenAICompatibleResponseFormat
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "json_object";
}
