using CrestCreates.Agent.Memory.Llm.Model;

namespace CrestCreates.Agent.Memory.Llm.Clients;

public sealed class FakeAgentMemoryLlmModelClient : IAgentMemoryLlmModelClient
{
    private readonly Queue<AgentMemoryLlmModelResponse> _responses = new();
    public IReadOnlyList<AgentMemoryLlmModelRequest> Requests => _requests;
    private readonly List<AgentMemoryLlmModelRequest> _requests = new();

    public FakeAgentMemoryLlmModelClient(params AgentMemoryLlmModelResponse[] responses)
    {
        foreach (var response in responses)
        {
            _responses.Enqueue(response);
        }
    }

    public Task<AgentMemoryLlmModelResponse> CompleteAsync(
        AgentMemoryLlmModelRequest request,
        CancellationToken cancellationToken = default)
    {
        _requests.Add(request);
        if (_responses.Count == 0)
        {
            return Task.FromResult(new AgentMemoryLlmModelResponse
            {
                FailureKind = AgentMemoryLlmProviderFailureKind.ProviderUnavailable,
                FailureDetail = "Fake response queue is empty."
            });
        }

        return Task.FromResult(_responses.Dequeue());
    }
}
