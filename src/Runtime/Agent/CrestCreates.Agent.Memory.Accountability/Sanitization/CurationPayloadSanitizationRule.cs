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
                ValidateRequiredNonEmpty(payload.CandidateId, "Payload.CandidateId", errors);
                ValidateIdentifier(payload.CandidateId, "Payload.CandidateId", errors);
                ValidateAbsent(payload.MemoryId, "Payload.MemoryId", errors);
                ValidateAbsent(payload.ReplacementCandidateId, "Payload.ReplacementCandidateId", errors);
                if (payload.Result == "committed")
                    ValidateIdentifier(payload.NewMemoryId, "Payload.NewMemoryId", errors);
                else
                    ValidateOptionalIdentifier(payload.NewMemoryId, "Payload.NewMemoryId", errors);
                break;
            case "reject":
                ValidateRequiredNonEmpty(payload.CandidateId, "Payload.CandidateId", errors);
                ValidateIdentifier(payload.CandidateId, "Payload.CandidateId", errors);
                ValidateAbsent(payload.MemoryId, "Payload.MemoryId", errors);
                ValidateAbsent(payload.ReplacementCandidateId, "Payload.ReplacementCandidateId", errors);
                ValidateAbsent(payload.NewMemoryId, "Payload.NewMemoryId", errors);
                break;
            case "supersede":
                ValidateRequiredNonEmpty(payload.MemoryId, "Payload.MemoryId", errors);
                ValidateIdentifier(payload.MemoryId, "Payload.MemoryId", errors);
                ValidateRequiredNonEmpty(payload.ReplacementCandidateId, "Payload.ReplacementCandidateId", errors);
                ValidateIdentifier(payload.ReplacementCandidateId, "Payload.ReplacementCandidateId", errors);
                ValidateAbsent(payload.CandidateId, "Payload.CandidateId", errors);
                break;
            case "archive":
                ValidateRequiredNonEmpty(payload.MemoryId, "Payload.MemoryId", errors);
                ValidateIdentifier(payload.MemoryId, "Payload.MemoryId", errors);
                ValidateAbsent(payload.CandidateId, "Payload.CandidateId", errors);
                ValidateAbsent(payload.ReplacementCandidateId, "Payload.ReplacementCandidateId", errors);
                ValidateAbsent(payload.NewMemoryId, "Payload.NewMemoryId", errors);
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
            ValidateRequiredNonEmpty(payload.PreviousState, "Payload.PreviousState", errors);
            var expectedStates = payload.Operation switch
            {
                "promote" or "reject" => (Previous: "candidate", Resulting: payload.Operation == "promote" ? "active" : "rejected"),
                "supersede" => (Previous: "active", Resulting: "superseded"),
                "archive" => (Previous: "active", Resulting: "archived"),
                _ => (Previous: string.Empty, Resulting: string.Empty)
            };
            if (payload.PreviousState is not null && !string.Equals(payload.PreviousState, expectedStates.Previous, StringComparison.Ordinal))
                errors.Add(("AUDIT_FIELD_INVALID", "Payload.PreviousState"));
            if (payload.ResultingState is not null && !string.Equals(payload.ResultingState, expectedStates.Resulting, StringComparison.Ordinal))
                errors.Add(("AUDIT_FIELD_INVALID", "Payload.ResultingState"));
            if (payload.StableFailureCode is not null)
                errors.Add(("AUDIT_FIELD_INVALID", "Payload.StableFailureCode"));
        }
        else if (payload.Result is "rejected" or "conflict")
        {
            ValidateRequiredNonEmpty(payload.StableFailureCode, "Payload.StableFailureCode", errors);
            if (!string.IsNullOrWhiteSpace(payload.StableFailureCode))
            {
                var allowed = payload.Result == "conflict"
                    ? AgentMemoryAccountabilityPayloadKinds.CurationConflictCodeAllowList
                    : AgentMemoryAccountabilityPayloadKinds.CurationRejectedCodeAllowList;
                ValidateAllowList(payload.StableFailureCode, allowed, "Payload.StableFailureCode", errors);
            }
            ValidateAbsent(payload.PreviousState, "Payload.PreviousState", errors);
            ValidateAbsent(payload.ResultingState, "Payload.ResultingState", errors);
        }

        ValidateSanitizationSummary(payload.Sanitization, errors);

        // NewMemoryId is a required target for promote/supersede and optional
        // for reject/archive. It is validated last so that an absent optional
        // output does not shadow a more specific field error.
        if (payload.Operation is "supersede" && payload.Result == "committed")
            ValidateIdentifier(payload.NewMemoryId, "Payload.NewMemoryId", errors);
        else if (payload.Operation is "supersede")
            ValidateOptionalIdentifier(payload.NewMemoryId, "Payload.NewMemoryId", errors);
    }

    private static void ValidateAbsent(string? value, string path, List<(string Code, string Path)> errors)
    {
        if (value is not null)
            errors.Add(("AUDIT_FIELD_INVALID", path));
    }
}
