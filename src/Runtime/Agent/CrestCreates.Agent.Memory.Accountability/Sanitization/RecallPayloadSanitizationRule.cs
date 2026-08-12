using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Abstractions.Json;

namespace CrestCreates.Agent.Memory.Accountability.Sanitization;

/// <summary>
/// Payload sanitization rule for <c>agent-memory.recall.result</c> (v1).
/// Validates the completed/rejected result contract, hard bounds, and
/// canonical hash metadata, then reserializes with the source-generated
/// type info.
/// </summary>
public sealed class RecallPayloadSanitizationRule : AgentMemoryPayloadSanitizationRuleBase<AgentMemoryRecallAccountabilityPayload>
{
    public RecallPayloadSanitizationRule()
        : base(
            AgentMemoryAccountabilityJsonSerializerContext.Default.AgentMemoryRecallAccountabilityPayload,
            AgentMemoryAccountabilityPayloadKinds.MaxDiagnosticCodes,
            AgentMemoryAccountabilityPayloadKinds.MaxRedactionCodes,
            AgentMemoryAccountabilityPayloadKinds.MaxRequestedKinds,
            AgentMemoryAccountabilityPayloadKinds.MaxIdentifierLength,
            AgentMemoryAccountabilityPayloadKinds.MaxCodeLength)
    {
    }

    public override string Kind => AgentMemoryAccountabilityPayloadKinds.Recall;

    public override int RuleVersion => 1;

    protected override void ValidateTyped(
        AgentMemoryRecallAccountabilityPayload payload,
        List<(string Code, string Path)> errors)
    {
        ValidateIdentifier(payload.OperationId, "Payload.OperationId", errors);
        ValidateAllowList(payload.Result, ["completed", "rejected"], "Payload.Result", errors);

        if (payload.Result == "completed")
        {
            ValidateRequiredHashMetadata(payload.EffectivePackHash, "Payload.EffectivePackHash", errors);
            if (payload.StableFailureCode is not null)
                errors.Add(("AUDIT_FIELD_INVALID", "Payload.StableFailureCode"));
        }
        else if (payload.Result == "rejected")
        {
            ValidateRequiredNonEmpty(payload.StableFailureCode, "Payload.StableFailureCode", errors);
            if (!string.IsNullOrWhiteSpace(payload.StableFailureCode))
                ValidateAllowList(payload.StableFailureCode, AgentMemoryAccountabilityPayloadKinds.RecallFailureCodeAllowList, "Payload.StableFailureCode", errors);
            if (payload.EffectivePackHash is not null)
                errors.Add(("AUDIT_FIELD_INVALID", "Payload.EffectivePackHash"));
            if (payload.ReturnedCount != 0)
                errors.Add(("AUDIT_FIELD_INVALID", "Payload.ReturnedCount"));
            if (payload.WasTruncated)
                errors.Add(("AUDIT_FIELD_INVALID", "Payload.WasTruncated"));
        }

        if (payload.ReturnedCount < 0)
            errors.Add(("AUDIT_FIELD_INVALID", "Payload.ReturnedCount"));
        if (payload.MaximumCount < 0)
            errors.Add(("AUDIT_FIELD_INVALID", "Payload.MaximumCount"));
        if (payload.CharacterBudget < 0)
            errors.Add(("AUDIT_FIELD_INVALID", "Payload.CharacterBudget"));
        ValidateRequiredNonEmpty(payload.MinimumConfidence, "Payload.MinimumConfidence", errors);
        if (!string.IsNullOrWhiteSpace(payload.MinimumConfidence))
            ValidateAllowList(
                payload.MinimumConfidence,
                AgentMemoryAccountabilityPayloadKinds.MinimumConfidenceAllowList,
                "Payload.MinimumConfidence",
                errors);
        ValidateCodeList(payload.DiagnosticCodes, "Payload.DiagnosticCodes", errors);
        ValidateRequestedKinds(payload.RequestedKinds, "Payload.RequestedKinds", errors);
        if (payload.RequestedKinds.Any(kind => !AgentMemoryAccountabilityPayloadKinds.RequestedKindAllowList.Contains(kind)))
            errors.Add(("AUDIT_FIELD_INVALID", "Payload.RequestedKinds"));
    }
}
