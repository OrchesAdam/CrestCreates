using CrestCreates.Agent.Prompting.Abstractions;

namespace CrestCreates.Agent.Memory.Llm.Model;

public enum AgentMemoryLlmProviderFailureKind
{
    ProviderUnavailable = 1,
    CredentialUnavailable = 2,
    Unauthorized = 3,
    RateLimited = 4,
    Timeout = 5,
    NetworkError = 6,
    ParseFailed = 7,
    ValidationFailed = 8
}

public sealed record AgentMemoryLlmModelRequest
{
    public required string PromptText { get; init; }
    public required AgentPromptInputEvidenceSummary PromptInputEvidence { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record AgentMemoryLlmModelResponse
{
    public string? ResponseText { get; init; }
    public string? ProviderName { get; init; }
    public string? ModelName { get; init; }
    public AgentMemoryLlmProviderFailureKind? FailureKind { get; init; }
    public string? FailureDetail { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record RecordedAgentMemoryLlmFixture(
    string PromptInputHash,
    string TemplateId,
    string TemplateVersion,
    string ModelProfileRef,
    string ProviderProfileRef,
    string ResponseText,
    string? ProviderName,
    string? ModelName);
