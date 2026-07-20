using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Memory.Tools;

/// <summary>
/// Immutable Host-finalized curation binding. Handlers never resolve a second
/// Promotion Service instance after this binding is created.
/// </summary>
public sealed record AgentMemoryToolRuntimeBinding
{
    public required IAgentMemoryPromotionService PromotionService { get; init; }
    public required AgentMemoryCurationOutcomeGuarantee OutcomeGuarantee { get; init; }
}
