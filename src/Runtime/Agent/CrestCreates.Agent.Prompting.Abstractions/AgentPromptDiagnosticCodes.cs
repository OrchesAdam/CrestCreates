namespace CrestCreates.Agent.Prompting.Abstractions;

public static class AgentPromptDiagnosticCodes
{
    public const string TemplateDescriptorMissing = "agent.prompt.template_descriptor_missing";
    public const string TemplateDescriptorPurposeMismatch = "agent.prompt.template_descriptor_purpose_mismatch";
    public const string InputHashProjectionFailed = "agent.prompt.input_hash_projection_failed";
    public const string OutputHashProjectionFailed = "agent.prompt.output_hash_projection_failed";
    public const string OutputHashUnavailable = "agent.prompt.output_hash_unavailable";
    public const string ProviderObservationUnavailable = "agent.prompt.provider_observation_unavailable";
    public const string PromptEvidenceCreated = "agent.prompt.evidence_created";
}
