namespace CrestCreates.Agent.Memory.Llm.Model;

public interface IAgentMemoryLlmModelClient
{
    Task<AgentMemoryLlmModelResponse> CompleteAsync(
        AgentMemoryLlmModelRequest request,
        CancellationToken cancellationToken = default);
}
