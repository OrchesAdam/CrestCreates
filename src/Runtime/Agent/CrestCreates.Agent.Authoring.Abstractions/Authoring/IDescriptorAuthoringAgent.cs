using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Authoring;

public interface IDescriptorAuthoringAgent
{
    Task<DescriptorAuthoringResult> AuthorAsync(
        AgentAuthoringContext context,
        CancellationToken cancellationToken = default);
}
