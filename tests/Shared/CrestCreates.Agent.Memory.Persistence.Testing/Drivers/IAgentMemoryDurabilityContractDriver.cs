namespace CrestCreates.Agent.Memory.Persistence.Testing.Drivers;

/// <summary>
/// Adds durable-process capabilities on top of the semantic store driver:
/// provider rebuild and raw revision observation through a typed result.
/// Implemented by the PostgreSQL runner only; the InMemory runner never
/// claims process durability.
/// </summary>
public interface IAgentMemoryDurabilityContractDriver : IAgentMemoryStoreContractDriver
{
    /// <summary>Disposes the current provider and builds a fresh one over the
    /// same schema, proving that durable state survives provider rebuild.</summary>
    ValueTask RebuildProviderAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads the raw durable revision for one artifact through a typed
    /// observation result. Only used to assert exact revision increments; never
    /// exposed as a Store contract.</summary>
    ValueTask<AgentMemoryRevisionObservation> ReadRawRevisionAsync(
        AgentMemoryArtifactKind artifactKind,
        string tenantId,
        string artifactId,
        CancellationToken cancellationToken = default);
}
