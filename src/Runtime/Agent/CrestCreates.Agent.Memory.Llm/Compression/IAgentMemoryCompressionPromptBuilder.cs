using CrestCreates.Agent.Memory.Llm.Prompting;

namespace CrestCreates.Agent.Memory.Llm.Compression;

public interface IAgentMemoryCompressionPromptBuilder
{
    string Build(AgentMemoryCompressionPromptInput input);
}
