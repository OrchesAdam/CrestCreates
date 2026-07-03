using CrestCreates.Agent.Memory.Llm.Prompting;

namespace CrestCreates.Agent.Memory.Llm.Extraction;

public interface IAgentMemoryExtractionPromptBuilder
{
    string Build(AgentMemoryExtractionPromptInput input);
}
