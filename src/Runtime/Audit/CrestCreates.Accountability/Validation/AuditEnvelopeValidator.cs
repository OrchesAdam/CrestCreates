using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Accountability.Abstractions.Semantics;
using CrestCreates.Accountability.Abstractions.Validation;

namespace CrestCreates.Accountability.Validation;

public sealed class AuditEnvelopeValidator
{
    public AuditValidationResult ValidateStructure(AuditEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var issues = ImmutableArray.CreateBuilder<AuditRecordIssue>();

        if (envelope.Runtime is null)
            issues.Add(new("AUDIT_REQUIRED_FIELD_MISSING", "Runtime"));
        else if (envelope.Runtime.References.IsDefault)
            issues.Add(new("AUDIT_DEFAULT_IMMUTABLE_ARRAY", "Runtime.References"));

        if (envelope.Descriptors is null)
            issues.Add(new("AUDIT_REQUIRED_FIELD_MISSING", "Descriptors"));
        else if (envelope.Descriptors.Items.IsDefault)
            issues.Add(new("AUDIT_DEFAULT_IMMUTABLE_ARRAY", "Descriptors.Items"));

        if (envelope.Evidence.IsDefault)
            issues.Add(new("AUDIT_DEFAULT_IMMUTABLE_ARRAY", "Evidence"));
        if (envelope.Tags is null)
            issues.Add(new("AUDIT_NULL_IMMUTABLE_DICTIONARY", "Tags"));
        if (envelope.DataSnapshot is { Artifacts.IsDefault: true })
            issues.Add(new("AUDIT_DEFAULT_IMMUTABLE_ARRAY", "DataSnapshot.Artifacts"));
        if (envelope.Sanitization is { AppliedRuleIds.IsDefault: true })
            issues.Add(new("AUDIT_DEFAULT_IMMUTABLE_ARRAY", "Sanitization.AppliedRuleIds"));

        return issues.Count == 0
            ? AuditValidationResult.Valid
            : new AuditValidationResult { Issues = issues.ToImmutable() };
    }

    public AuditValidationResult ValidateCandidate(AuditEnvelope envelope)
        => Validate(envelope, candidate: true);

    public AuditValidationResult ValidateSafeSnapshot(AuditEnvelope envelope)
        => Validate(envelope, candidate: false);

    private static AuditValidationResult Validate(AuditEnvelope envelope, bool candidate)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var structural = new AuditEnvelopeValidator().ValidateStructure(envelope);
        if (!structural.IsValid)
            return structural;

        var issues = ImmutableArray.CreateBuilder<AuditRecordIssue>();

        CheckRequired(envelope, issues);
        CheckSemanticValues(envelope, issues);
        CheckReferences(envelope, issues);
        CheckCollections(envelope, issues);
        CheckJsonValues(envelope, candidate, issues);

        return issues.Count == 0
            ? AuditValidationResult.Valid
            : new AuditValidationResult { Issues = issues.ToImmutable() };
    }

    private static void CheckRequired(AuditEnvelope envelope, ImmutableArray<AuditRecordIssue>.Builder issues)
    {
        if (envelope.ContractVersion != 1) issues.Add(new("AUDIT_INVALID_CONTRACT_VERSION", "ContractVersion"));
        if (envelope.OccurredAt == default) issues.Add(new("AUDIT_REQUIRED_FIELD_MISSING", "OccurredAt"));
        CheckIdentifier(envelope.AuditId, "AuditId", issues);
        CheckIdentifier(envelope.CorrelationId, "CorrelationId", issues);
        CheckText(envelope.Actor?.Kind, "Actor.Kind", issues);
        CheckIdentifier(envelope.Actor?.Id, "Actor.Id", issues);
        CheckText(envelope.Action?.Kind, "Action.Kind", issues);
        CheckActionName(envelope.Action?.Name, issues);
        CheckText(envelope.Target?.Kind, "Target.Kind", issues);
        CheckIdentifier(envelope.Target?.Id, "Target.Id", issues);
        CheckOptionalIdentifier(envelope.CausationId, "CausationId", issues);
        CheckOptionalIdentifier(envelope.ParentAuditId, "ParentAuditId", issues);
        CheckOptionalIdentifier(envelope.PreviousAuditId, "PreviousAuditId", issues);
        if (envelope.Outcome is null) issues.Add(new("AUDIT_REQUIRED_FIELD_MISSING", "Outcome"));
        if (string.Equals(envelope.ParentAuditId, envelope.AuditId, StringComparison.Ordinal))
            issues.Add(new("AUDIT_SELF_RELATION", "ParentAuditId"));
        if (string.Equals(envelope.PreviousAuditId, envelope.AuditId, StringComparison.Ordinal))
            issues.Add(new("AUDIT_SELF_RELATION", "PreviousAuditId"));
    }

    private static void CheckSemanticValues(AuditEnvelope envelope, ImmutableArray<AuditRecordIssue>.Builder issues)
    {
        if (envelope.Outcome is null || !AuditOutcomeStatuses.IsKnown(envelope.Outcome.Status))
            issues.Add(new("AUDIT_UNKNOWN_OUTCOME_STATUS", "Outcome.Status"));
        if (envelope.Runtime is null) issues.Add(new("AUDIT_REQUIRED_FIELD_MISSING", "Runtime"));
        if (envelope.Descriptors is null) issues.Add(new("AUDIT_REQUIRED_FIELD_MISSING", "Descriptors"));

        CheckKind(envelope.Actor?.Kind, "Actor.Kind", issues);
        CheckKind(envelope.Action?.Kind, "Action.Kind", issues);
        CheckKind(envelope.Target?.Kind, "Target.Kind", issues);
        if (envelope.Runtime is { InvocationSource: not null })
            CheckKind(envelope.Runtime.InvocationSource, "Runtime.InvocationSource", issues);

        CheckOptionalIdentifier(envelope.Outcome?.Code, "Outcome.Code", issues);
        if (envelope.Outcome?.SafeSummary is { Length: > AuditContractLimits.MaxSafeSummaryLength })
            issues.Add(new("AUDIT_LIMIT_EXCEEDED", "Outcome.SafeSummary"));
        CheckOptionalIdentifier(envelope.Target?.Version, "Target.Version", issues);
        if (envelope.Actor is { } actor)
        {
            CheckOptionalIdentifier(actor.DelegationId, "Actor.DelegationId", issues);
            CheckOptionalIdentifier(actor.ImpersonationId, "Actor.ImpersonationId", issues);
            CheckActorReference(actor.InitiatedBy, "Actor.InitiatedBy", issues);
            CheckActorReference(actor.OnBehalfOf, "Actor.OnBehalfOf", issues);
            if (actor.DisplayName is { Length: > AuditContractLimits.MaxSafeSummaryLength })
                issues.Add(new("AUDIT_LIMIT_EXCEEDED", "Actor.DisplayName"));
        }
    }

    private static void CheckReferences(AuditEnvelope envelope, ImmutableArray<AuditRecordIssue>.Builder issues)
    {
        if (envelope.Runtime is not null)
        {
            CheckOptionalIdentifier(envelope.Runtime.ExecutionId, "Runtime.ExecutionId", issues);
            CheckOptionalIdentifier(envelope.Runtime.RequestId, "Runtime.RequestId", issues);
            CheckOptionalIdentifier(envelope.Runtime.TraceId, "Runtime.TraceId", issues);
            CheckOptionalIdentifier(envelope.Runtime.SpanId, "Runtime.SpanId", issues);
            foreach (var reference in envelope.Runtime.References)
            {
                if (reference is null)
                {
                    issues.Add(new("AUDIT_REQUIRED_FIELD_MISSING", "Runtime.References"));
                    continue;
                }
                CheckKind(reference.Kind, "Runtime.References.Kind", issues);
                CheckIdentifier(reference.Id, "Runtime.References.Id", issues);
            }
            AddDuplicateIssues(envelope.Runtime.References.Where(x => x is not null).Select(x => x.Kind + "\u001f" + x.Id), "Runtime.References", issues);
        }
        if (envelope.Descriptors is not null)
        {
            CheckOptionalIdentifier(envelope.Descriptors.SnapshotId, "Descriptors.SnapshotId", issues);
            foreach (var reference in envelope.Descriptors.Items)
            {
                if (reference is null)
                {
                    issues.Add(new("AUDIT_REQUIRED_FIELD_MISSING", "Descriptors.Items"));
                    continue;
                }
                CheckKind(reference.Kind, "Descriptors.Items.Kind", issues);
                CheckIdentifier(reference.Id, "Descriptors.Items.Id", issues);
                if (reference.Version <= 0)
                    issues.Add(new("AUDIT_INVALID_DESCRIPTOR_VERSION", "Descriptors.Items.Version"));
                ValidateHash(reference.ContractHash, "Descriptors.Items.ContractHash", issues);
            }
            AddDuplicateIssues(envelope.Descriptors.Items.Where(x => x is not null).Select(x => x.Kind + "\u001f" + x.Id + "\u001f" + x.Version), "Descriptors.Items", issues);
        }
        if (envelope.Sanitization is { } stamp)
        {
            CheckIdentifier(stamp.PolicyId, "Sanitization.PolicyId", issues);
            if (stamp.PolicyVersion <= 0)
                issues.Add(new("AUDIT_INVALID_SANITIZATION_POLICY_VERSION", "Sanitization.PolicyVersion"));
            foreach (var ruleId in stamp.AppliedRuleIds)
                CheckIdentifier(ruleId, "Sanitization.AppliedRuleIds", issues);
            AddDuplicateIssues(stamp.AppliedRuleIds, "Sanitization.AppliedRuleIds", issues);
        }

        AddDuplicateIssues(envelope.Evidence.Where(x => x is not null).Select(x => x.Kind + "\u001f" + x.Id), "Evidence", issues);
        foreach (var reference in envelope.Evidence)
        {
            if (reference is null)
            {
                issues.Add(new("AUDIT_REQUIRED_FIELD_MISSING", "Evidence"));
                continue;
            }
            CheckKind(reference.Kind, "Evidence.Kind", issues);
            CheckIdentifier(reference.Id, "Evidence.Id", issues);
            ValidateHash(reference.Hash, "Evidence.Hash", issues);
        }
        ValidateHash(envelope.Descriptors?.SnapshotHash, "Descriptors.SnapshotHash", issues);
        ValidateHash(envelope.Integrity, "Integrity", issues);
        if (envelope.Sanitization is { PolicyId: null })
            issues.Add(new("AUDIT_REQUIRED_FIELD_MISSING", "Sanitization.PolicyId"));
    }

    private static void CheckCollections(AuditEnvelope envelope, ImmutableArray<AuditRecordIssue>.Builder issues)
    {
        if (envelope.Tags is { Count: > AuditContractLimits.MaxTags })
            issues.Add(new("AUDIT_LIMIT_EXCEEDED", "Tags"));
        if (envelope.Runtime is { References.Length: > AuditContractLimits.MaxRuntimeReferences })
            issues.Add(new("AUDIT_LIMIT_EXCEEDED", "Runtime.References"));
        if (envelope.Descriptors is { Items.Length: > AuditContractLimits.MaxDescriptorReferences })
            issues.Add(new("AUDIT_LIMIT_EXCEEDED", "Descriptors.Items"));
        if (envelope.Evidence.Length > AuditContractLimits.MaxEvidenceReferences)
            issues.Add(new("AUDIT_LIMIT_EXCEEDED", "Evidence"));

        if (envelope.Tags is null)
            return;

        foreach (var pair in envelope.Tags)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > AuditContractLimits.MaxTagKeyLength)
                issues.Add(new("AUDIT_LIMIT_EXCEEDED", "Tags.Key"));
            if (string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Length > AuditContractLimits.MaxTagValueLength)
                issues.Add(new("AUDIT_LIMIT_EXCEEDED", "Tags.Value"));
        }
    }

    private static void CheckJsonValues(AuditEnvelope envelope, bool candidate, ImmutableArray<AuditRecordIssue>.Builder issues)
    {
        if (envelope.Payload is { } payload)
        {
            CheckKind(payload.Kind, "Payload.Kind", issues);
            if (payload.Version <= 0)
                issues.Add(new("AUDIT_INVALID_PAYLOAD_VERSION", "Payload.Version"));
            if (payload.Data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                issues.Add(new("AUDIT_INVALID_JSON_VALUE", "Payload.Data"));
            if (payload.Data.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
            {
                var bytes = Encoding.UTF8.GetByteCount(payload.Data.GetRawText());
                if (bytes > AuditContractLimits.MaxPayloadBytes) issues.Add(new("AUDIT_LIMIT_EXCEEDED", "Payload.Data"));
                ValidateJson(payload.Data, "Payload.Data", issues);
            }
        }

        if (envelope.DataSnapshot is { } snapshot)
        {
            if (snapshot.Artifacts.Length > AuditContractLimits.MaxDataArtifacts)
                issues.Add(new("AUDIT_LIMIT_EXCEEDED", "DataSnapshot.Artifacts"));
            CheckText(snapshot.CapturePolicyId, "DataSnapshot.CapturePolicyId", issues);
            if (snapshot.CapturePolicyVersion <= 0)
                issues.Add(new("AUDIT_INVALID_CAPTURE_POLICY_VERSION", "DataSnapshot.CapturePolicyVersion"));
            for (var index = 0; index < snapshot.Artifacts.Length; index++)
            {
                var artifact = snapshot.Artifacts[index];
                if (artifact is null)
                {
                    issues.Add(new("AUDIT_REQUIRED_FIELD_MISSING", $"DataSnapshot.Artifacts[{index}]"));
                    continue;
                }
                CheckKind(artifact.Kind, $"DataSnapshot.Artifacts[{index}].Kind", issues);
                ValidateHash(artifact.ContentHash, $"DataSnapshot.Artifacts[{index}].ContentHash", issues);
                if (artifact.ContentHash is not null && artifact.ContentHashBasis is null)
                    issues.Add(new("AUDIT_INVALID_HASH_METADATA", $"DataSnapshot.Artifacts[{index}].ContentHashBasis"));
                if (artifact.ContentHash is null && artifact.ContentHashBasis is not null)
                    issues.Add(new("AUDIT_INVALID_HASH_METADATA", $"DataSnapshot.Artifacts[{index}].ContentHashBasis"));
                if (artifact.SanitizedValue is { } value)
                {
                    if (value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
                    {
                        var bytes = Encoding.UTF8.GetByteCount(value.GetRawText());
                        if (bytes > AuditContractLimits.MaxSingleArtifactBytes)
                            issues.Add(new("AUDIT_LIMIT_EXCEEDED", $"DataSnapshot.Artifacts[{index}]"));
                        ValidateJson(value, $"DataSnapshot.Artifacts[{index}].SanitizedValue", issues);
                    }
                }
            }

            AddDuplicateIssues(
                snapshot.Artifacts.Where(x => x is not null).Select(x => x.Kind),
                "DataSnapshot.Artifacts.Kind",
                issues);
        }
    }

    private static void CheckIdentifier(string? value, string path, ImmutableArray<AuditRecordIssue>.Builder issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add(new("AUDIT_REQUIRED_FIELD_MISSING", path));
        else if (value.Length > AuditContractLimits.MaxIdentifierLength)
            issues.Add(new("AUDIT_MAX_IDENTIFIER_LENGTH_EXCEEDED", path));
    }

    private static void CheckOptionalIdentifier(string? value, string path, ImmutableArray<AuditRecordIssue>.Builder issues)
    {
        if (value is not null && (string.IsNullOrWhiteSpace(value) || value.Length > AuditContractLimits.MaxIdentifierLength))
            issues.Add(new(value.Length > AuditContractLimits.MaxIdentifierLength ? "AUDIT_LIMIT_EXCEEDED" : "AUDIT_INVALID_TEXT", path));
    }

    private static void CheckActionName(string? value, ImmutableArray<AuditRecordIssue>.Builder issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add(new("AUDIT_REQUIRED_FIELD_MISSING", "Action.Name"));
        else if (value.Length > AuditContractLimits.MaxActionNameLength)
            issues.Add(new("AUDIT_LIMIT_EXCEEDED", "Action.Name"));
    }

    private static void CheckActorReference(AuditActorReference? reference, string path, ImmutableArray<AuditRecordIssue>.Builder issues)
    {
        if (reference is null) return;
        CheckKind(reference.Kind, $"{path}.Kind", issues);
        CheckIdentifier(reference.Id, $"{path}.Id", issues);
    }

    private static void CheckText(string? value, string path, ImmutableArray<AuditRecordIssue>.Builder issues)
        => CheckIdentifier(value, path, issues);

    private static void CheckKind(string? value, string path, ImmutableArray<AuditRecordIssue>.Builder issues)
    {
        if (!AuditSemanticNames.IsStableKind(value, AuditContractLimits.MaxSemanticKindLength))
            issues.Add(new("AUDIT_INVALID_SEMANTIC_KIND", path));
    }

    private static void ValidateHash(
        CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHash? hash,
        string path,
        ImmutableArray<AuditRecordIssue>.Builder issues)
    {
        if (hash is null) return;
        if (string.IsNullOrWhiteSpace(hash.Value)
            || string.IsNullOrWhiteSpace(hash.Algorithm)
            || string.IsNullOrWhiteSpace(hash.AlgorithmVersion)
            || string.IsNullOrWhiteSpace(hash.ArtifactKind)
            || string.IsNullOrWhiteSpace(hash.Scope)
            || string.IsNullOrWhiteSpace(hash.Purpose)
            || string.IsNullOrWhiteSpace(hash.ContractVersion)
            || string.IsNullOrWhiteSpace(hash.CanonicalShapeVersion))
            issues.Add(new("AUDIT_INVALID_HASH_METADATA", path));
    }

    private static void ValidateJson(JsonElement value, string path, ImmutableArray<AuditRecordIssue>.Builder issues)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    issues.Add(new("AUDIT_INVALID_JSON_VALUE", path));
                ValidateJson(property.Value, $"{path}.{property.Name}", issues);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
                ValidateJson(item, $"{path}[{index++}]", issues);
        }
    }

    private static void AddDuplicateIssues(IEnumerable<string> values, string path, ImmutableArray<AuditRecordIssue>.Builder issues)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
            if (!seen.Add(value)) issues.Add(new("AUDIT_DUPLICATE_REFERENCE", path));
    }
}
