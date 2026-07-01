namespace CrestCreates.Agent.Authoring.Abstractions.Model;

public interface IDescriptorAuthoringModelClient
{
    Task<DescriptorAuthoringModelResponse> CompleteAsync(
        DescriptorAuthoringModelRequest request,
        CancellationToken cancellationToken = default);
}
