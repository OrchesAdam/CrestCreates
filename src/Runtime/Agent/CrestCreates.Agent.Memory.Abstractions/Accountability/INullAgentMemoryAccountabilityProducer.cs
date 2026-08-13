namespace CrestCreates.Agent.Memory.Abstractions.Accountability;

/// <summary>
/// Implemented by the no-op producer registered when no durable Audit Sink is
/// configured. A composition validator can detect a bridge that was never wired
/// (a "still null" producer) without referencing the concrete implementation.
/// </summary>
public interface INullAgentMemoryAccountabilityProducer : IAgentMemoryAccountabilityProducer
{
}
