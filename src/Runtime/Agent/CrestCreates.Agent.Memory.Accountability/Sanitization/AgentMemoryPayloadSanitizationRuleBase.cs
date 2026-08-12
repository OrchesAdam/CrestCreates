using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Sanitization;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Accountability.Sanitization;

/// <summary>
/// Shared implementation core for the three frozen Agent Memory payload
/// sanitization rules. Parsing and reserialization always use the exact
/// source-generated <c>JsonTypeInfo</c>, so unknown members and invalid
/// versions are rejected before any custom validation runs. The rule only
/// rewrites the payload <see cref="AuditPayload.Data"/>; the protected
/// semantic fields Kind and Version are preserved exactly by construction.
/// </summary>
public abstract class AgentMemoryPayloadSanitizationRuleBase<T> : IAuditPayloadSanitizationRule
{
    private readonly JsonTypeInfo<T> _typeInfo;
    private readonly int _maxDiagnosticCodes;
    private readonly int _maxRedactionCodes;
    private readonly int _maxRequestedKinds;
    private readonly int _maxIdentifierLength;
    private readonly int _maxCodeLength;

    protected AgentMemoryPayloadSanitizationRuleBase(
        JsonTypeInfo<T> typeInfo,
        int maxDiagnosticCodes,
        int maxRedactionCodes,
        int maxRequestedKinds,
        int maxIdentifierLength,
        int maxCodeLength)
    {
        _typeInfo = typeInfo;
        _maxDiagnosticCodes = maxDiagnosticCodes;
        _maxRedactionCodes = maxRedactionCodes;
        _maxRequestedKinds = maxRequestedKinds;
        _maxIdentifierLength = maxIdentifierLength;
        _maxCodeLength = maxCodeLength;
    }

    public abstract string Kind { get; }

    public abstract int RuleVersion { get; }

    public AuditPayload Sanitize(AuditPayload payload)
    {
        if (payload.Version != AgentMemoryAccountabilityPayloadKinds.Version)
            throw new AuditSanitizationException("AUDIT_PAYLOAD_VERSION_UNSUPPORTED", "Payload.Version");

        T? typed;
        try
        {
            typed = payload.Data.Deserialize(_typeInfo);
        }
        catch (Exception)
        {
            throw new AuditSanitizationException("AUDIT_PAYLOAD_INVALID", "Payload.Data");
        }

        var errors = new List<(string Code, string Path)>();
        ValidateTyped(typed!, errors);
        if (errors.Count > 0)
            throw new AuditSanitizationException(errors[0].Code, errors[0].Path);

        JsonElement sanitized;
        try
        {
            sanitized = JsonSerializer.SerializeToElement(typed!, _typeInfo);
        }
        catch (Exception)
        {
            throw new AuditSanitizationException("AUDIT_PAYLOAD_SERIALIZATION_FAILED", "Payload.Data");
        }

        return payload with { Data = sanitized };
    }

    protected abstract void ValidateTyped(T payload, List<(string Code, string Path)> errors);

    protected void ValidateIdentifier(string? value, string path, List<(string Code, string Path)> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(("AUDIT_FIELD_REQUIRED", path));
            return;
        }
        if (value.Length > _maxIdentifierLength)
            errors.Add(("AUDIT_IDENTIFIER_TOO_LONG", path));
    }

    /// <summary>
    /// Validates an optional identifier: a null value is skipped, but a
    /// present value must be non-blank and within the identifier length bound.
    /// </summary>
    protected void ValidateOptionalIdentifier(string? value, string path, List<(string Code, string Path)> errors)
    {
        if (value is null)
            return;
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(("AUDIT_FIELD_REQUIRED", path));
            return;
        }
        if (value.Length > _maxIdentifierLength)
            errors.Add(("AUDIT_IDENTIFIER_TOO_LONG", path));
    }

    protected void ValidateRequiredNonEmpty(string? value, string path, List<(string Code, string Path)> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add(("AUDIT_FIELD_REQUIRED", path));
    }

    protected void ValidateCodeList(IReadOnlyList<string>? codes, string path, List<(string Code, string Path)> errors)
    {
        if (codes is null || codes.Count == 0)
            return;
        if (codes.Count > _maxDiagnosticCodes)
        {
            errors.Add(("AUDIT_CODE_LIMIT_EXCEEDED", path));
            return;
        }
        for (var i = 0; i < codes.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(codes[i]) || codes[i].Length > _maxCodeLength)
            {
                errors.Add(("AUDIT_CODE_INVALID", $"{path}[{i}]"));
                return;
            }
        }
        for (var i = 1; i < codes.Count; i++)
        {
            if (string.CompareOrdinal(codes[i - 1], codes[i]) >= 0)
            {
                errors.Add(("AUDIT_CODES_NOT_SORTED_OR_DUPLICATE", path));
                return;
            }
        }
    }

    protected void ValidateRedactionList(IReadOnlyList<string>? codes, string path, List<(string Code, string Path)> errors)
    {
        if (codes is null || codes.Count == 0)
            return;
        if (codes.Count > _maxRedactionCodes)
        {
            errors.Add(("AUDIT_REDACTION_LIMIT_EXCEEDED", path));
            return;
        }
        for (var i = 0; i < codes.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(codes[i]) || codes[i].Length > _maxCodeLength)
            {
                errors.Add(("AUDIT_CODE_INVALID", $"{path}[{i}]"));
                return;
            }
        }
        for (var i = 1; i < codes.Count; i++)
        {
            if (string.CompareOrdinal(codes[i - 1], codes[i]) >= 0)
            {
                errors.Add(("AUDIT_CODES_NOT_SORTED_OR_DUPLICATE", path));
                return;
            }
        }
    }

    protected void ValidateRequestedKinds(IReadOnlyList<string>? kinds, string path, List<(string Code, string Path)> errors)
    {
        if (kinds is null || kinds.Count == 0)
            return;
        if (kinds.Count > _maxRequestedKinds)
        {
            errors.Add(("AUDIT_REQUESTED_KINDS_LIMIT_EXCEEDED", path));
            return;
        }
        foreach (var kind in kinds)
        {
            if (string.IsNullOrWhiteSpace(kind) || kind.Length > _maxIdentifierLength)
            {
                errors.Add(("AUDIT_KIND_INVALID", path));
                return;
            }
        }
    }

    protected void ValidateSanitizationSummary(
        AgentMemoryAccountabilitySanitizationSummary? summary,
        List<(string Code, string Path)> errors)
    {
        if (summary is null)
            return;
        if (summary.State is not ("none" or "redacted" or "rejected"))
        {
            errors.Add(("AUDIT_FIELD_INVALID", "Payload.Sanitization.State"));
            return;
        }
        ValidateRedactionList(summary.RedactionCodes, "Payload.Sanitization.RedactionCodes", errors);
        ValidateCodeList(summary.DiagnosticCodes, "Payload.Sanitization.DiagnosticCodes", errors);
    }

    protected void ValidateAllowList(string? value, IReadOnlyCollection<string> allowed, string path, List<(string Code, string Path)> errors)
    {
        if (value is null || !allowed.Contains(value, StringComparer.Ordinal))
            errors.Add(("AUDIT_FIELD_INVALID", path));
    }

    protected void ValidateHashMetadata(CanonicalHash? hash, string path, List<(string Code, string Path)> errors)
    {
        if (hash is null)
            return;
        if (string.IsNullOrWhiteSpace(hash.Value)
            || string.IsNullOrWhiteSpace(hash.Algorithm)
            || string.IsNullOrWhiteSpace(hash.AlgorithmVersion)
            || string.IsNullOrWhiteSpace(hash.ArtifactKind)
            || string.IsNullOrWhiteSpace(hash.Scope)
            || string.IsNullOrWhiteSpace(hash.Purpose)
            || string.IsNullOrWhiteSpace(hash.ContractVersion)
            || string.IsNullOrWhiteSpace(hash.CanonicalShapeVersion))
        {
            errors.Add(("AUDIT_HASH_METADATA_INVALID", path));
        }
    }
}
