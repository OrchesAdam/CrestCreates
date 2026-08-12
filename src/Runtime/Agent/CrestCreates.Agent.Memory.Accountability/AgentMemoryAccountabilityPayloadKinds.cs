namespace CrestCreates.Agent.Memory.Accountability;

/// <summary>
/// Frozen wire kinds and version for the Agent Memory accountability payloads.
/// These constants are the single source of truth shared by the producer, the
/// sanitization rules, and the AuditId projection. Do not derive them from CLR
/// record names or enum.ToString().
/// </summary>
public static class AgentMemoryAccountabilityPayloadKinds
{
    public const string Recall = "agent-memory.recall.result";
    public const string Curation = "agent-memory.curation.result";
    public const string SourceExpansion = "agent-memory.source-expansion.result";

    public const int Version = 1;

    // Accountability v1 hard limits plus Memory-specific lower bounds (spec §11).
    public const int MaxDiagnosticCodes = 32;
    public const int MaxRedactionCodes = 16;
    public const int MaxRequestedKinds = 6;
    public const int MaxIdentifierLength = 256;
    public const int MaxCodeLength = 128;
    public const int MaxPayloadVersion = 1;
}
