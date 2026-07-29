using System.Collections.Immutable;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Sanitization;

namespace CrestCreates.Accountability.Sanitization;

public sealed class DefaultAuditSanitizer : IAuditSanitizer
{
    private readonly AuditPayloadSanitizationRuleRegistry _payloadRules;
    private readonly AuditDataArtifactSanitizationRuleRegistry _artifactRules;
    private readonly string _policyId;
    private readonly int _policyVersion;

    public DefaultAuditSanitizer(
        AuditPayloadSanitizationRuleRegistry payloadRules,
        AuditDataArtifactSanitizationRuleRegistry artifactRules,
        string policyId = "accountability-default",
        int policyVersion = 1)
    {
        _payloadRules = payloadRules;
        _artifactRules = artifactRules;
        _policyId = policyId;
        _policyVersion = policyVersion;
    }

    public ValueTask<AuditSanitizationResult> SanitizeAsync(AuditEnvelope candidate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var applied = ImmutableArray.CreateBuilder<string>();
        AuditPayload? payload = candidate.Payload;
        if (payload is not null)
        {
            payload = _payloadRules.Sanitize(payload);
            applied.Add(_payloadRules.GetAppliedRuleId(payload.Kind));
        }

        AuditDataSnapshot? snapshot = candidate.DataSnapshot;
        if (snapshot is not null)
        {
            var artifacts = ImmutableArray.CreateBuilder<AuditDataArtifact>(snapshot.Artifacts.Length);
            foreach (var artifact in snapshot.Artifacts)
            {
                var sanitized = _artifactRules.Sanitize(artifact);
                artifacts.Add(sanitized);
                applied.Add(_artifactRules.GetAppliedRuleId(sanitized.Kind));
            }
            snapshot = snapshot with { Artifacts = artifacts.ToImmutable() };
        }

        var sanitizedEnvelope = candidate with
        {
            Payload = payload,
            DataSnapshot = snapshot,
            Sanitization = null,
            Integrity = null
        };

        var stamp = new AuditSanitizationStamp
        {
            PolicyId = _policyId,
            PolicyVersion = _policyVersion,
            AppliedRuleIds = applied.Order(StringComparer.Ordinal).ToImmutableArray()
        };

        return ValueTask.FromResult(new AuditSanitizationResult
        {
            Envelope = sanitizedEnvelope,
            Stamp = stamp
        });
    }
}
