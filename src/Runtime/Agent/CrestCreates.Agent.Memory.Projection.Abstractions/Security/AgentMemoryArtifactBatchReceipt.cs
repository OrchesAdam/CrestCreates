namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Split receipt for handle and grant batches. A single receipt cannot express
/// "Handle reused, Grant newly created" — this can.
/// </summary>
public sealed record AgentMemoryArtifactBatchReceipt
{
    public required BatchReceipt? HandleBatch { get; init; }
    public required BatchReceipt? GrantBatch { get; init; }

    public sealed record BatchReceipt
    {
        public required string BatchHash { get; init; }
        public required int Count { get; init; }
        public required bool ReusedExisting { get; init; }
    }
}
