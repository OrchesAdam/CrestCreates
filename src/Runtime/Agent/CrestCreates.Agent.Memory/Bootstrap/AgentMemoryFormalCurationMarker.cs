using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Memory.Bootstrap;

/// <summary>
/// Concrete formal-curation marker surfaced by <c>AddAgentMemoryCuration</c>.
/// Presence of this singleton enables curation composition validation.
/// </summary>
public sealed class AgentMemoryFormalCurationMarker : IAgentMemoryFormalCurationMarker
{
}
