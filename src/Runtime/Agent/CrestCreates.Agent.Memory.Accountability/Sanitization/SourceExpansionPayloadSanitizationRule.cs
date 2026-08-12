using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Abstractions.Json;

namespace CrestCreates.Agent.Memory.Accountability.Sanitization;

/// <summary>
/// Payload sanitization rule for <c>agent-memory.source-expansion.result</c>
/// (v1). Validates the authorized source target, expanded/redacted/rejected
/// status contract, effective-visible-content hash metadata, required
/// sanitization summary, and hard bounds.
/// </summary>
public sealed class SourceExpansionPayloadSanitizationRule : AgentMemoryPayloadSanitizationRuleBase<AgentMemorySourceExpansionAccountabilityPayload>
{
    public SourceExpansionPayloadSanitizationRule()
        : base(
            AgentMemoryAccountabilityJsonSerializerContext.Default.AgentMemorySourceExpansionAccountabilityPayload,
            AgentMemoryAccountabilityPayloadKinds.MaxDiagnosticCodes,
            AgentMemoryAccountabilityPayloadKinds.MaxRedactionCodes,
            AgentMemoryAccountabilityPayloadKinds.MaxRequestedKinds,
            AgentMemoryAccountabilityPayloadKinds.MaxIdentifierLength,
            AgentMemoryAccountabilityPayloadKinds.MaxCodeLength)
    {
    }

    public override string Kind => AgentMemoryAccountabilityPayloadKinds.SourceExpansion;

    public override int RuleVersion => 1;

    protected override void ValidateTyped(
        AgentMemorySourceExpansionAccountabilityPayload payload,
        List<(string Code, string Path)> errors)
    {
        ValidateIdentifier(payload.OperationId, "Payload.OperationId", errors);
        ValidateRequiredNonEmpty(payload.SourceKind, "Payload.SourceKind", errors);
        if (!string.IsNullOrWhiteSpace(payload.SourceKind))
            ValidateAllowList(
                payload.SourceKind,
                AgentMemoryAccountabilityPayloadKinds.SourceKindAllowList,
                "Payload.SourceKind",
                errors);
        ValidateIdentifier(payload.SourceId, "Payload.SourceId", errors);

        ValidateAllowList(
            payload.Status,
            ["expanded", "redacted", "not-found", "not-expandable", "external-source-not-supported"],
            "Payload.Status",
            errors);

        if (payload.Status == "expanded")
        {
            ValidateEffectiveVisibleContentHash(payload.EffectiveVisibleContentHash, "Payload.EffectiveVisibleContentHash", errors);
        }
        else
        {
            if (payload.EffectiveVisibleContentHash is not null)
                errors.Add(("AUDIT_FIELD_INVALID", "Payload.EffectiveVisibleContentHash"));
            if (payload.WasTruncated)
                errors.Add(("AUDIT_FIELD_INVALID", "Payload.WasTruncated"));
        }

        if (payload.MaximumCharacters < 0)
            errors.Add(("AUDIT_FIELD_INVALID", "Payload.MaximumCharacters"));
        if (payload.Sanitization is null)
            errors.Add(("AUDIT_FIELD_REQUIRED", "Payload.Sanitization"));
        else
            ValidateSanitizationSummary(payload.Sanitization, errors);
        ValidateCodeList(payload.DiagnosticCodes, "Payload.DiagnosticCodes", errors,
            AgentMemoryAccountabilityPayloadKinds.DiagnosticCodeAllowList);
    }
}
