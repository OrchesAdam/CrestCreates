namespace CrestCreates.Agent.Prompting.Abstractions;

/// <summary>
/// Canonical shape version constants for agent prompt evidence hashing.
/// Each value identifies the structural layout of the canonical JSON payload.
/// </summary>
public static class AgentPromptCanonicalShapeVersions
{
    public const string InputEvidence = "agent-prompt-input-evidence-shape-v1";
    public const string OutputEvidence = "agent-prompt-output-evidence-shape-v1";
}
