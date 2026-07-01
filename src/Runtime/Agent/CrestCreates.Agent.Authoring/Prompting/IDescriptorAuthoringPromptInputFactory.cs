using CrestCreates.Agent.Authoring.Abstractions.Prompting;
using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Authoring.Prompting;

public interface IDescriptorAuthoringPromptInputFactory
{
    DescriptorAuthoringPromptInput Create(AgentAuthoringContext context);
}
