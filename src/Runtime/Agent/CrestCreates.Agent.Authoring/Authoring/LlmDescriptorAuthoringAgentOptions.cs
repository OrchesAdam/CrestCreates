namespace CrestCreates.Agent.Authoring.Authoring;

public sealed class LlmDescriptorAuthoringAgentOptions
{
    public const string DefaultAuthorId = "llm-descriptor-authoring-agent";

    public string AuthorId { get; set; } = DefaultAuthorId;
}
