using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Agent.Authoring.Abstractions.Prompting;

namespace CrestCreates.Agent.Authoring.Prompting;

/// <summary>
/// Compatibility service that computes descriptor-authoring prompt input hash
/// using the default prompt template identity from <see cref="Authoring.LlmDescriptorAuthoringAgentOptions"/>.
/// </summary>
/// <remarks>
/// This interface uses fixed <c>DefaultPromptTemplateId</c>, <c>DefaultPromptTemplateVersion</c>,
/// and <c>DefaultPromptContractVersion</c> from <see cref="Authoring.LlmDescriptorAuthoringAgentOptions"/>.
/// For real LLM authoring execution, use <see cref="IAgentPromptEvidenceFactory"/> with explicit
/// <see cref="LlmDescriptorAuthoringAgentOptions"/> values instead.
/// </remarks>
[Obsolete("Use IAgentPromptEvidenceFactory with explicit LlmDescriptorAuthoringAgentOptions for prompt evidence creation.")]
public interface IDescriptorAuthoringPromptInputHashService
{
    CanonicalHash ComputeHash(
        DescriptorAuthoringPromptInput input,
        AgentPromptModelProfileRef modelProfileRef,
        AgentPromptProviderProfileRef providerProfileRef);
}
