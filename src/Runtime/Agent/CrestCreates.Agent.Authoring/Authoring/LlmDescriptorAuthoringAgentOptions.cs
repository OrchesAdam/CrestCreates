using CrestCreates.Agent.Authoring.Abstractions.Model;

namespace CrestCreates.Agent.Authoring.Authoring;

public sealed class LlmDescriptorAuthoringAgentOptions
{
    public const string DefaultAuthorId = "llm-descriptor-authoring-agent";

    public string AuthorId { get; set; } = DefaultAuthorId;

    public DescriptorAuthoringModelProfile ModelProfile { get; set; } = new()
    {
        ProfileName = "default",
        ProviderName = "unknown",
        ModelName = "unknown"
    };
}
