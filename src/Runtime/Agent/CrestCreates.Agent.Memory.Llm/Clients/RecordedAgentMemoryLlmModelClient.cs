using CrestCreates.Agent.Memory.Llm.Model;

namespace CrestCreates.Agent.Memory.Llm.Clients;

public sealed class RecordedAgentMemoryLlmModelClient : IAgentMemoryLlmModelClient
{
    private readonly IReadOnlyList<RecordedAgentMemoryLlmFixture> _fixtures;

    public RecordedAgentMemoryLlmModelClient(IReadOnlyList<RecordedAgentMemoryLlmFixture> fixtures)
    {
        _fixtures = fixtures;
    }

    public Task<AgentMemoryLlmModelResponse> CompleteAsync(
        AgentMemoryLlmModelRequest request,
        CancellationToken cancellationToken = default)
    {
        var evidence = request.PromptInputEvidence;
        var fixture = _fixtures.FirstOrDefault(item =>
            item.PromptInputHash == evidence.InputHash.Value &&
            item.TemplateId == evidence.TemplateId.Value &&
            item.TemplateVersion == evidence.TemplateVersion.Value &&
            item.ModelProfileRef == evidence.ModelProfileRef.Value &&
            item.ProviderProfileRef == evidence.ProviderProfileRef.Value);

        if (fixture is null)
        {
            return Task.FromResult(new AgentMemoryLlmModelResponse
            {
                FailureKind = AgentMemoryLlmProviderFailureKind.ProviderUnavailable,
                FailureDetail = $"MissingRecordedFixture: {evidence.TemplateId.Value}/{evidence.TemplateVersion.Value}/{evidence.InputHash.Value}"
            });
        }

        return Task.FromResult(new AgentMemoryLlmModelResponse
        {
            ResponseText = fixture.ResponseText,
            ProviderName = fixture.ProviderName,
            ModelName = fixture.ModelName
        });
    }
}
