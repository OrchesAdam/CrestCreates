using System.Collections.Immutable;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Hashing;
using CrestCreates.Accountability.Abstractions.Preparation;
using CrestCreates.Accountability.Abstractions.Sanitization;
using CrestCreates.Accountability.Abstractions.Validation;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Accountability.CanonicalHashing;
using CrestCreates.Accountability.Sanitization;
using CrestCreates.Accountability.Validation;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Accountability.Preparation;

public sealed class DefaultAuditEnvelopePreparer : IAuditEnvelopePreparer
{
    private readonly AuditEnvelopeValidator _validator;
    private readonly IAuditSanitizer _sanitizer;
    private readonly IAuditIntegrityHasher _hasher;
    private readonly AccountabilityCanonicalProjectionWriter _projectionWriter;
    private readonly ILogger<DefaultAuditEnvelopePreparer> _logger;

    public DefaultAuditEnvelopePreparer(
        AuditEnvelopeValidator validator,
        IAuditSanitizer sanitizer,
        IAuditIntegrityHasher hasher,
        AccountabilityCanonicalProjectionWriter projectionWriter,
        ILogger<DefaultAuditEnvelopePreparer> logger)
    {
        _validator = validator;
        _sanitizer = sanitizer;
        _hasher = hasher;
        _projectionWriter = projectionWriter;
        _logger = logger;
    }

    public async ValueTask<AuditEnvelopePreparationResult> PrepareAsync(AuditEnvelope candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var structural = _validator.ValidateStructure(candidate);
        if (!structural.IsValid) return Rejected(structural.Issues);
        var validation = _validator.ValidateCandidate(candidate);
        if (!validation.IsValid) return Rejected(validation.Issues);
        if (candidate.Sanitization is not null || candidate.Integrity is not null)
            return Rejected(new AuditRecordIssue("AUDIT_CANDIDATE_METADATA_SUPPLIED"));

        var snapshot = Snapshot(candidate);
        if (_projectionWriter.MeasureBytes(snapshot) > AuditContractLimits.MaxCandidateEnvelopeBytes)
            return Rejected(new AuditRecordIssue("AUDIT_LIMIT_EXCEEDED", "CandidateEnvelope"));

        AuditSanitizationResult sanitizedResult;
        try
        {
            sanitizedResult = await _sanitizer.SanitizeAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
        catch (AuditSanitizationException ex)
        {
            return Rejected(new AuditRecordIssue(ex.Code, ex.Path));
        }

        var sanitized = Snapshot(sanitizedResult.Envelope);
        var safeValidation = _validator.ValidateSafeSnapshot(sanitized);
        if (!safeValidation.IsValid) return Rejected(safeValidation.Issues);
        if (sanitized.Sanitization is not null || sanitized.Integrity is not null)
            return Rejected(new AuditRecordIssue("AUDIT_SANITIZED_OUTPUT_INVALID"));
        if (!AuditProtectedFactComparer.AreEqual(snapshot, sanitized) || !AuditSanitizerOutputComparer.IsAllowed(snapshot, sanitized))
            return Rejected(new AuditRecordIssue("AUDIT_SANITIZER_REWROTE_PROTECTED_FACT"));
        if (sanitizedResult.Stamp is null || string.IsNullOrWhiteSpace(sanitizedResult.Stamp.PolicyId)
            || sanitizedResult.Stamp.PolicyVersion <= 0 || sanitizedResult.Stamp.AppliedRuleIds.IsDefault)
            return Rejected(new AuditRecordIssue("AUDIT_SANITIZATION_STAMP_INVALID", "Sanitization"));

        var stamped = sanitized with { Sanitization = sanitizedResult.Stamp };
        var stampedValidation = _validator.ValidateSafeSnapshot(stamped);
        if (!stampedValidation.IsValid) return Rejected(stampedValidation.Issues);
        if (_projectionWriter.MeasureBytes(stamped) > AuditContractLimits.MaxSafeEnvelopeBytes)
            return Rejected(new AuditRecordIssue("AUDIT_MAX_SAFE_ENVELOPE_BYTES_EXCEEDED"));
        return AuditEnvelopePreparationResult.Accepted(stamped with { Integrity = _hasher.Compute(stamped) });
    }

    private static AuditEnvelopePreparationResult Rejected(params AuditRecordIssue[] issues)
        => AuditEnvelopePreparationResult.Rejected(issues);

    private static AuditEnvelopePreparationResult Rejected(ImmutableArray<AuditRecordIssue> issues)
        => AuditEnvelopePreparationResult.Rejected(issues.ToArray());

    internal static AuditEnvelope Snapshot(AuditEnvelope value)
    {
        var payload = value.Payload is { } p ? p with { Data = p.Data.Clone() } : null;
        var data = value.DataSnapshot is { } snapshot
            ? snapshot with { Artifacts = snapshot.Artifacts.Select(x => x is null ? null! : x with { SanitizedValue = x.SanitizedValue?.Clone() }).ToImmutableArray() }
            : null;
        var tags = value.Tags.OrderBy(x => x.Key, StringComparer.Ordinal).ToImmutableSortedDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        return value with { Payload = payload, DataSnapshot = data, Tags = tags };
    }
}
