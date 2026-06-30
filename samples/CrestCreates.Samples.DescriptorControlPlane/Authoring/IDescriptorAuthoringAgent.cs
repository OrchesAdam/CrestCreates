using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Samples.DescriptorControlPlane.Authoring;

public interface IDescriptorAuthoringAgent
{
    Task<DescriptorAuthoringResult> AuthorAsync(
        AgentAuthoringContext context,
        CancellationToken cancellationToken = default);
}
