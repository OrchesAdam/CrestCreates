using CrestCreates.Agent.Authoring.Abstractions.Prompting;

namespace CrestCreates.Agent.Authoring.Prompting;

public interface IDescriptorAuthoringPromptBuilder
{
    DescriptorAuthoringPromptOutput Build(DescriptorAuthoringPromptInput input);
}
