using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;

namespace CrestCreates.Agent.Memory.Accountability;

/// <summary>
/// No-op Accountability producer used when no durable Audit Sink is configured.
/// Each call completes immediately without publishing any fact.
/// </summary>
public sealed class NullAgentMemoryAccountabilityProducer : INullAgentMemoryAccountabilityProducer
{
    public ValueTask PublishRecallAsync(
        AgentMemoryOperationIdentity identity,
        AgentMemoryInvocationContext context,
        AgentMemoryRecallAccountabilityPayload payload) => ValueTask.CompletedTask;

    public ValueTask PublishCurationAsync(
        AgentMemoryOperationIdentity identity,
        AgentMemoryInvocationContext context,
        AgentMemoryCurationAccountabilityPayload payload) => ValueTask.CompletedTask;

    public ValueTask PublishSourceExpansionAsync(
        AgentMemoryOperationIdentity identity,
        AgentMemoryInvocationContext context,
        AgentMemorySourceExpansionAccountabilityPayload payload) => ValueTask.CompletedTask;
}
