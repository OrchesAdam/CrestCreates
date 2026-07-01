using System.Text.Json.Serialization;

namespace CrestCreates.Agent.Authoring.Http.OpenAICompatible;

public sealed class OpenAICompatibleChatResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("choices")]
    public List<OpenAICompatibleChatChoice>? Choices { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }
}

public sealed class OpenAICompatibleChatChoice
{
    [JsonPropertyName("message")]
    public OpenAICompatibleChatMessage? Message { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}
