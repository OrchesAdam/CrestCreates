using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Agent.Authoring.Abstractions.Prompting;

namespace CrestCreates.Agent.Authoring.Prompting;

public interface IDescriptorAuthoringPromptInputHashService
{
    CanonicalHash ComputeHash(
        DescriptorAuthoringPromptInput input,
        AgentPromptModelProfileRef modelProfileRef,
        AgentPromptProviderProfileRef providerProfileRef);
}
