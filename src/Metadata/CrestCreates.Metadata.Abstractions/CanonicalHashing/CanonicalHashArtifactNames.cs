namespace CrestCreates.Metadata.Abstractions.CanonicalHashing;

/// <summary>
/// Canonical string constants for artifact kind values in canonical hash metadata.
/// Used in <see cref="CanonicalHash.ArtifactKind"/> and envelope metadata.
/// Never use enum.ToString() for hash input — always use these canonical string helpers.
/// </summary>
public static class CanonicalHashArtifactNames
{
    public const string Descriptor = "Descriptor";
    public const string ReviewResult = "ReviewResult";
    public const string Package = "Package";
    public const string Report = "Report";
    public const string PackageManifest = "PackageManifest";
    public const string PackageEvidence = "PackageEvidence";
    public const string PackageEvidenceEnvelope = "PackageEvidenceEnvelope";
    public const string AgentMemoryContent = "AgentMemoryContent";
    public const string AgentMemoryPack = "AgentMemoryPack";
    public const string AgentMemoryScope = "AgentMemoryScope";
    public const string AgentMemorySet = "AgentMemorySet";
    public const string AgentPromptInputEvidence = "AgentPromptInputEvidence";
    public const string AgentPromptOutputEvidence = "AgentPromptOutputEvidence";
    public const string AgentPromptTemplateDescriptor = "AgentPromptTemplateDescriptor";
}
