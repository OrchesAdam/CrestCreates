namespace CrestCreates.Agent.Prompting.Abstractions;

public sealed record AgentPromptTemplateDescriptor
{
    public required AgentPromptTemplateId TemplateId { get; init; }
    public required AgentPromptVersion Version { get; init; }
    public required AgentPromptPurpose Purpose { get; init; }
    public required AgentPromptContractVersion ContractVersion { get; init; }
    public string? Description { get; init; }
    public string? InputSchemaVersion { get; init; }
    public string? OutputSchemaVersion { get; init; }
    public bool ContainsSensitiveContent { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
