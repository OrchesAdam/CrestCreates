using CrestCreates.Agent.Authoring.Abstractions.Model;

namespace CrestCreates.Agent.Authoring.Clients;

public sealed class FakeDescriptorAuthoringModelClient : IDescriptorAuthoringModelClient
{
    private readonly DescriptorAuthoringModelResponse _response;

    public FakeDescriptorAuthoringModelClient(DescriptorAuthoringModelResponse response)
    {
        _response = response;
    }

    public FakeDescriptorAuthoringModelClient(string responseText)
    {
        _response = new DescriptorAuthoringModelResponse
        {
            ResponseText = responseText,
            ProviderName = "fake",
            ModelName = "fake-model"
        };
    }

    public FakeDescriptorAuthoringModelClient(DescriptorAuthoringProviderFailureKind failureKind, string? failureDetail = null)
    {
        _response = new DescriptorAuthoringModelResponse
        {
            ResponseText = string.Empty,
            ProviderName = "fake",
            ModelName = "fake-model",
            FailureKind = failureKind,
            FailureDetail = failureDetail
        };
    }

    public Task<DescriptorAuthoringModelResponse> CompleteAsync(
        DescriptorAuthoringModelRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_response);
    }
}
