using CrestCreates.Agent.Authoring.Abstractions.Model;

namespace CrestCreates.Agent.Authoring.Clients;

public sealed class RecordedDescriptorAuthoringModelClient : IDescriptorAuthoringModelClient
{
    private readonly Dictionary<string, string> _fixtures;
    private readonly string _providerName;
    private readonly string _modelName;

    public RecordedDescriptorAuthoringModelClient(
        Dictionary<string, string> fixtures,
        string providerName = "recorded",
        string modelName = "recorded-model")
    {
        _fixtures = fixtures ?? throw new ArgumentNullException(nameof(fixtures));
        _providerName = providerName;
        _modelName = modelName;
    }

    public Task<DescriptorAuthoringModelResponse> CompleteAsync(
        DescriptorAuthoringModelRequest request,
        CancellationToken cancellationToken = default)
    {
        var hashValue = request.Prompt.PromptInputHash.Value;

        if (!_fixtures.TryGetValue(hashValue, out var responseText))
        {
            // Missing fixture must surface as ProviderUnavailable, not as an empty successful plan
            return Task.FromResult(new DescriptorAuthoringModelResponse
            {
                ResponseText = string.Empty,
                ProviderName = _providerName,
                ModelName = _modelName,
                PromptInputHash = request.Prompt.PromptInputHash,
                FailureKind = DescriptorAuthoringProviderFailureKind.Unknown,
                FailureDetail = $"No recorded fixture found for prompt input hash '{hashValue}'."
            });
        }

        return Task.FromResult(new DescriptorAuthoringModelResponse
        {
            ResponseText = responseText,
            ProviderName = _providerName,
            ModelName = _modelName,
            PromptInputHash = request.Prompt.PromptInputHash
        });
    }
}
