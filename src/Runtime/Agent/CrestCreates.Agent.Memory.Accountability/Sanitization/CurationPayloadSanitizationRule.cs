using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Abstractions.Json;

namespace CrestCreates.Agent.Memory.Accountability.Sanitization;

/// <summary>
/// Payload sanitization rule for <c>agent-memory.curation.result</c> (v1).
/// Validates the operation/target matrix, committed/rejected/conflict result
/// contract, canonical hash metadata, sanitization summary, and hard bounds.
/// </summary>
public sealed class CurationPayloadSanitizationRule : AgentMemoryPayloadSanitizationRuleBase<AgentMemoryCurationAccountabilityPayload>
{
    public CurationPayloadSanitizationRule()
        : base(
            AgentMemoryAccountabilityJsonSerializerContext.Default.AgentMemoryCurationAccountabilityPayload,
            AgentMemoryAccountabilityPayloadKinds.MaxDiagnosticCodes,
            AgentMemoryAccountabilityPayloadKinds.MaxRedactionCodes,
            AgentMemoryAccountabilityPayloadKinds.MaxRequestedKinds,
            AgentMemoryAccountabilityPayloadKinds.MaxIdentifierLength,
            AgentMemoryAccountabilityPayloadKinds.MaxCodeLength)
    {
    }

    public override string Kind => AgentMemoryAccountabilityPayloadKinds.Curation;

    public override int RuleVersion => 1;

    protected override void ValidateTyped(
        AgentMemoryCurationAccountabilityPayload payload,
        List<(string Code, string Path)> errors)
    {
        ValidateIdentifier(payload.OperationId, "Payload.OperationId", errors);
        ValidateAllowList(payload.Operation, ["promote", "reject", "supersede", "archive"], "Payload.Operation", errors);

        switch (payload.Operation)
        {
            case "promote":
            case "reject":
                ValidateRequiredNonEmpty(payload.CandidateId, "Payload.CandidateId", errors);
                ValidateIdentifier(payload.CandidateId, "Payload.CandidateId", errors);
                break;
            case "supersede":
                ValidateRequiredNonEmpty(payload.MemoryId, "Payload.MemoryId", errors);
                ValidateIdentifier(payload.MemoryId, "Payload.MemoryId", errors);
                ValidateRequiredNonEmpty(payload.ReplacementCandidateId, "Payload.ReplacementCandidateId", errors);
                ValidateIdentifier(payload.ReplacementCandidateId, "Payload.ReplacementCandidateId", errors);
                break;
            case "archive":
                ValidateRequiredNonEmpty(payload.MemoryId, "Payload.MemoryId", errors);
                ValidateIdentifier(payload.MemoryId, "Payload.MemoryId", errors);
                break;
        }

        ValidateHashMetadata(payload.ExpectedCandidateStateHash, "Payload.ExpectedCandidateStateHash", errors);
        ValidateHashMetadata(payload.ExpectedMemoryStateHash, "Payload.ExpectedMemoryStateHash", errors);
        ValidateHashMetadata(payload.ExpectedReplacementStateHash, "Payload.ExpectedReplacementStateHash", errors);
        ValidateHashMetadata(payload.ExpectedContentHash, "Payload.ExpectedContentHash", errors);

        ValidateAllowList(payload.Result, ["committed", "rejected", "conflict"], "Payload.Result", errors);
        if (payload.Result == "committed")
        {
            ValidateRequiredNonEmpty(payload.ResultingState, "Payload.ResultingState", errors);
        }
        else if (payload.Result == "rejected")
        {
            ValidateRequiredNonEmpty(payload.StableFailureCode, "Payload.StableFailureCode", errors);
        }

        ValidateSanitizationSummary(payload.Sanitization, errors);

        // NewMemoryId is a required target for promote/supersede and optional
        // for reject/archive. It is validated last so that an absent optional
        // output does not shadow a more specific field error.
        if (payload.Operation is "promote" or "supersede")
            ValidateIdentifier(payload.NewMemoryId, "Payload.NewMemoryId", errors);
        else
            ValidateOptionalIdentifier(payload.NewMemoryId, "Payload.NewMemoryId", errors);
    }
}
