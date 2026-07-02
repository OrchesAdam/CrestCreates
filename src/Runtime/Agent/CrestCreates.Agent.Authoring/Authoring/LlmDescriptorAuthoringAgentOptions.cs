using CrestCreates.Agent.Authoring.Abstractions.Model;
using CrestCreates.Agent.Prompting.Abstractions;

namespace CrestCreates.Agent.Authoring.Authoring;

public sealed class LlmDescriptorAuthoringAgentOptions
{
    public const string DefaultAuthorId = "llm-descriptor-authoring-agent";

    public static readonly AgentPromptTemplateId DefaultPromptTemplateId = new("descriptor-authoring");
    public static readonly AgentPromptVersion DefaultPromptTemplateVersion = new("descriptor-authoring-prompt-template-v1");
    public static readonly AgentPromptContractVersion DefaultPromptContractVersion = new("7g.v1");

    public string AuthorId { get; set; } = DefaultAuthorId;

    public AgentPromptTemplateId PromptTemplateId { get; set; } = DefaultPromptTemplateId;
    public AgentPromptVersion PromptTemplateVersion { get; set; } = DefaultPromptTemplateVersion;
    public AgentPromptContractVersion PromptContractVersion { get; set; } = DefaultPromptContractVersion;
    public AgentPromptProviderProfileRef ProviderProfileRef { get; set; } = new("unknown");

    public DescriptorAuthoringModelProfile ModelProfile { get; set; } = new()
    {
        ProfileName = "default",
        ProviderName = "unknown",
        ModelName = "unknown"
    };
}
