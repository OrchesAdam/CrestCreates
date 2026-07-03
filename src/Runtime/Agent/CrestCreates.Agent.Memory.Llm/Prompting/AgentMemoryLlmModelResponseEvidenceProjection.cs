namespace CrestCreates.Agent.Memory.Llm.Prompting;

public sealed record AgentMemoryLlmModelResponseEvidenceProjection
{
    public string? ProviderName { get; init; }
    public string? ModelName { get; init; }
    public string? PromptInputHash { get; init; }
    public string? FailureKind { get; init; }
    public string? FailureDetail { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
