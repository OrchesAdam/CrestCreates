namespace CrestCreates.Accountability.Abstractions.Validation;

public static class AuditContractLimits
{
    public const int MaxIdentifierLength = 256;
    public const int MaxSemanticKindLength = 128;
    public const int MaxActionNameLength = 512;
    public const int MaxSafeSummaryLength = 1_024;
    public const int MaxTags = 32;
    public const int MaxTagKeyLength = 128;
    public const int MaxTagValueLength = 512;
    public const int MaxDescriptorReferences = 32;
    public const int MaxRuntimeReferences = 64;
    public const int MaxEvidenceReferences = 64;
    public const int MaxDataArtifacts = 16;
    public const int MaxSingleArtifactBytes = 64 * 1024;
    public const int MaxPayloadBytes = 64 * 1024;
    public const int MaxCandidateEnvelopeBytes = 256 * 1024;
    public const int MaxSafeEnvelopeBytes = 256 * 1024;
}
