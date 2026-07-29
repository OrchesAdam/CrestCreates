using CrestCreates.Accountability.Abstractions.Contracts;

namespace CrestCreates.Accountability.Abstractions.Sanitization;

public interface IAuditSanitizer
{
    ValueTask<AuditSanitizationResult> SanitizeAsync(
        AuditEnvelope candidate,
        CancellationToken cancellationToken = default);
}

public sealed record AuditSanitizationResult
{
    public required AuditEnvelope Envelope { get; init; }
    public required AuditSanitizationStamp Stamp { get; init; }
}

public interface IAuditPayloadSanitizationRule
{
    string Kind { get; }
    int RuleVersion { get; }
    AuditPayload Sanitize(AuditPayload payload);
}

public interface IAuditDataArtifactSanitizationRule
{
    string Kind { get; }
    int RuleVersion { get; }
    AuditDataArtifact Sanitize(AuditDataArtifact artifact);
}
