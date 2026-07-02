using CrestCreates.Agent.Authoring.Abstractions.Prompting;
using CrestCreates.Agent.Authoring.Authoring;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Authoring.Prompting;

/// <summary>
/// Compatibility adapter that computes descriptor-authoring prompt input hash
/// using the default prompt template identity from <see cref="LlmDescriptorAuthoringAgentOptions"/>.
/// </summary>
/// <remarks>
/// This adapter uses fixed <c>DefaultPromptTemplateId</c>, <c>DefaultPromptTemplateVersion</c>,
/// and <c>DefaultPromptContractVersion</c> from <see cref="LlmDescriptorAuthoringAgentOptions"/>.
/// For real LLM authoring execution, use <see cref="IAgentPromptEvidenceFactory"/> with explicit
/// <see cref="LlmDescriptorAuthoringAgentOptions"/> values instead.
/// </remarks>
[Obsolete("Use IAgentPromptEvidenceFactory with explicit LlmDescriptorAuthoringAgentOptions for prompt evidence creation.")]
public sealed class DefaultDescriptorAuthoringPromptInputHashService : IDescriptorAuthoringPromptInputHashService
{
    private readonly IAgentPromptHashService _promptHashService;

    public DefaultDescriptorAuthoringPromptInputHashService(IAgentPromptHashService promptHashService)
    {
        _promptHashService = promptHashService;
    }

    public CanonicalHash ComputeHash(
        DescriptorAuthoringPromptInput input,
        AgentPromptModelProfileRef modelProfileRef,
        AgentPromptProviderProfileRef providerProfileRef)
    {
        return _promptHashService.ComputeInputHash(new AgentPromptEvidenceCreationRequest<DescriptorAuthoringPromptInput>
        {
            TemplateId = LlmDescriptorAuthoringAgentOptions.DefaultPromptTemplateId,
            TemplateVersion = LlmDescriptorAuthoringAgentOptions.DefaultPromptTemplateVersion,
            Purpose = AgentPromptPurpose.DescriptorAuthoring,
            ContractVersion = LlmDescriptorAuthoringAgentOptions.DefaultPromptContractVersion,
            ModelProfileRef = modelProfileRef,
            ProviderProfileRef = providerProfileRef,
            Payload = input,
            TenantId = input.TenantId
        });
    }
}
