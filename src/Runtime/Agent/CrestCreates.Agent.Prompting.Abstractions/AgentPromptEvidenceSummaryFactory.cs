using CrestCreates.Agent.Prompting.Abstractions;

namespace CrestCreates.Agent.Prompting.Abstractions;

public static class AgentPromptEvidenceSummaryFactory
{
    public static AgentPromptInputEvidenceSummary CreateInputSummary<TInput>(
        AgentPromptInputEvidence<TInput> evidence) => new()
    {
        TemplateId = evidence.TemplateId,
        TemplateVersion = evidence.TemplateVersion,
        Purpose = evidence.Purpose,
        ContractVersion = evidence.ContractVersion,
        ModelProfileRef = evidence.ModelProfileRef,
        ProviderProfileRef = evidence.ProviderProfileRef,
        InputHash = evidence.InputHash,
        CreatedAt = evidence.CreatedAt,
        Diagnostics = evidence.Diagnostics.ToArray()
    };

    public static AgentPromptOutputEvidenceSummary CreateOutputSummary<TOutput>(
        AgentPromptOutputEvidence<TOutput> evidence) => new()
    {
        TemplateId = evidence.TemplateId,
        TemplateVersion = evidence.TemplateVersion,
        Purpose = evidence.Purpose,
        ContractVersion = evidence.ContractVersion,
        ModelProfileRef = evidence.ModelProfileRef,
        ProviderProfileRef = evidence.ProviderProfileRef,
        InputHash = evidence.InputHash,
        OutputHash = evidence.OutputHash,
        ProviderObservation = evidence.ProviderObservation,
        CreatedAt = evidence.CreatedAt,
        Diagnostics = evidence.Diagnostics.ToArray()
    };
}
