using CrestCreates.Agent.Authoring.Abstractions.Prompting;
using CrestCreates.Agent.Authoring.Authoring;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Authoring.Prompting;

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
