namespace CrestCreates.Agent.Memory.Abstractions.Accountability;

/// <summary>
/// Pushes completed Memory accountability facts to the unified Audit Sink. Each
/// method is one independent bounded attempt; identity.OperationId must equal
/// payload.OperationId and identity.OccurredAt maps directly to the Envelope.
/// No business cancellation token is accepted.
/// </summary>
public interface IAgentMemoryAccountabilityProducer
{
    ValueTask PublishRecallAsync(
        AgentMemoryOperationIdentity identity,
        AgentMemoryInvocationContext context,
        AgentMemoryRecallAccountabilityPayload payload);

    ValueTask PublishCurationAsync(
        AgentMemoryOperationIdentity identity,
        AgentMemoryInvocationContext context,
        AgentMemoryCurationAccountabilityPayload payload);

    ValueTask PublishSourceExpansionAsync(
        AgentMemoryOperationIdentity identity,
        AgentMemoryInvocationContext context,
        AgentMemorySourceExpansionAccountabilityPayload payload);
}
