using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Accountability;

/// <summary>
/// Projects governed curation Accountability payloads from confirmed conditional
/// commit outcomes or complete typed rejection/conflict contexts. Raw
/// Reason/Explanation/source content never enters a payload; only stable
/// expected CAS hashes, transition identities, and bounded sanitization codes.
/// </summary>
public class AgentMemoryCurationFactProjector
{
    private const int MaxDiagnosticCodes = 32;
    private const int MaxRedactionCodes = 16;

    public virtual AgentMemoryCurationAccountabilityPayload PromoteCommitted(
        AgentMemoryOperationRequest operation,
        AgentMemoryPromotionPlan plan,
        AgentMemoryItem committed)
        => new()
        {
            OperationId = operation.Identity.OperationId,
            Operation = "promote",
            CandidateId = plan.Candidate.CandidateId,
            NewMemoryId = plan.NewMemoryId,
            ExpectedCandidateStateHash = plan.Candidate.ExpectedStateHash,
            ExpectedMemoryStateHash = plan.ExpectedMemoryStateHash,
            ExpectedContentHash = plan.ExpectedMemoryContentHash,
            PreviousState = "candidate",
            ResultingState = "active",
            Result = "committed",
            Sanitization = MapSanitization(committed)
        };

    public virtual AgentMemoryCurationAccountabilityPayload RejectCommitted(
        AgentMemoryOperationRequest operation,
        AgentMemoryCandidateExpectation expectation)
        => new()
        {
            OperationId = operation.Identity.OperationId,
            Operation = "reject",
            CandidateId = expectation.CandidateId,
            ExpectedCandidateStateHash = expectation.ExpectedStateHash,
            PreviousState = "candidate",
            ResultingState = "rejected",
            Result = "committed"
        };

    public virtual AgentMemoryCurationAccountabilityPayload SupersedeCommitted(
        AgentMemoryOperationRequest operation,
        AgentMemorySupersessionPlan plan,
        AgentMemoryItem committed)
        => new()
        {
            OperationId = operation.Identity.OperationId,
            Operation = "supersede",
            MemoryId = plan.TargetMemory.MemoryId,
            ReplacementCandidateId = plan.ReplacementCandidate.CandidateId,
            NewMemoryId = plan.NewMemoryId,
            ExpectedMemoryStateHash = plan.TargetMemory.ExpectedStateHash,
            ExpectedReplacementStateHash = plan.ReplacementCandidate.ExpectedStateHash,
            ExpectedContentHash = plan.ExpectedMemoryContentHash,
            PreviousState = "active",
            ResultingState = "superseded",
            Result = "committed",
            Sanitization = MapSanitization(committed)
        };

    public virtual AgentMemoryCurationAccountabilityPayload ArchiveCommitted(
        AgentMemoryOperationRequest operation,
        AgentMemoryItemExpectation expectation,
        AgentMemoryStatus previousStatus,
        AgentMemoryItem archived)
        => new()
        {
            OperationId = operation.Identity.OperationId,
            Operation = "archive",
            MemoryId = expectation.MemoryId,
            ExpectedMemoryStateHash = expectation.ExpectedStateHash,
            PreviousState = MapStatus(previousStatus),
            ResultingState = "archived",
            Result = "committed",
            Sanitization = MapSanitization(archived)
        };

    public virtual AgentMemoryCurationAccountabilityPayload PromoteFailure(
        AgentMemoryOperationRequest operation,
        AgentMemoryPromotionPlan plan,
        AgentMemoryOperationFailureCode code)
        => TypedFailure(
            operation, "promote", code,
            candidateId: plan.Candidate.CandidateId,
            newMemoryId: plan.NewMemoryId,
            expectedCandidateStateHash: plan.Candidate.ExpectedStateHash,
            expectedMemoryStateHash: plan.ExpectedMemoryStateHash,
            expectedContentHash: plan.ExpectedMemoryContentHash);

    public virtual AgentMemoryCurationAccountabilityPayload RejectFailure(
        AgentMemoryOperationRequest operation,
        AgentMemoryCandidateExpectation expectation,
        AgentMemoryOperationFailureCode code)
        => TypedFailure(
            operation, "reject", code,
            candidateId: expectation.CandidateId,
            expectedCandidateStateHash: expectation.ExpectedStateHash);

    public virtual AgentMemoryCurationAccountabilityPayload SupersedeFailure(
        AgentMemoryOperationRequest operation,
        AgentMemorySupersessionPlan plan,
        AgentMemoryOperationFailureCode code)
        => TypedFailure(
            operation, "supersede", code,
            memoryId: plan.TargetMemory.MemoryId,
            replacementCandidateId: plan.ReplacementCandidate.CandidateId,
            newMemoryId: plan.NewMemoryId,
            expectedMemoryStateHash: plan.TargetMemory.ExpectedStateHash,
            expectedReplacementStateHash: plan.ReplacementCandidate.ExpectedStateHash,
            expectedContentHash: plan.ExpectedMemoryContentHash);

    public virtual AgentMemoryCurationAccountabilityPayload ArchiveFailure(
        AgentMemoryOperationRequest operation,
        AgentMemoryItemExpectation expectation,
        AgentMemoryOperationFailureCode code)
        => TypedFailure(
            operation, "archive", code,
            memoryId: expectation.MemoryId,
            expectedMemoryStateHash: expectation.ExpectedStateHash);

    public virtual AgentMemoryCurationAccountabilityPayload PromoteValidationFailure(
        AgentMemoryOperationRequest operation,
        string candidateId,
        string newMemoryId,
        AgentMemoryOperationFailureCode code)
        => TypedFailure(operation, "promote", code, candidateId: candidateId, newMemoryId: newMemoryId);

    public virtual AgentMemoryCurationAccountabilityPayload RejectValidationFailure(
        AgentMemoryOperationRequest operation,
        string candidateId,
        AgentMemoryOperationFailureCode code)
        => TypedFailure(operation, "reject", code, candidateId: candidateId);

    public virtual AgentMemoryCurationAccountabilityPayload SupersedeValidationFailure(
        AgentMemoryOperationRequest operation,
        string memoryId,
        string replacementCandidateId,
        string newMemoryId,
        AgentMemoryOperationFailureCode code)
        => TypedFailure(
            operation, "supersede", code,
            memoryId: memoryId,
            replacementCandidateId: replacementCandidateId,
            newMemoryId: newMemoryId);

    public virtual AgentMemoryCurationAccountabilityPayload ArchiveValidationFailure(
        AgentMemoryOperationRequest operation,
        string memoryId,
        AgentMemoryOperationFailureCode code)
        => TypedFailure(operation, "archive", code, memoryId: memoryId);

    private static AgentMemoryCurationAccountabilityPayload TypedFailure(
        AgentMemoryOperationRequest operation,
        string operationName,
        AgentMemoryOperationFailureCode code,
        string? candidateId = null,
        string? memoryId = null,
        string? replacementCandidateId = null,
        string? newMemoryId = null,
        CanonicalHash? expectedCandidateStateHash = null,
        CanonicalHash? expectedMemoryStateHash = null,
        CanonicalHash? expectedReplacementStateHash = null,
        CanonicalHash? expectedContentHash = null)
    {
        var (result, stableCode) = MapFailure(code);
        return new AgentMemoryCurationAccountabilityPayload
        {
            OperationId = operation.Identity.OperationId,
            Operation = operationName,
            CandidateId = candidateId,
            MemoryId = memoryId,
            ReplacementCandidateId = replacementCandidateId,
            NewMemoryId = newMemoryId,
            ExpectedCandidateStateHash = expectedCandidateStateHash,
            ExpectedMemoryStateHash = expectedMemoryStateHash,
            ExpectedReplacementStateHash = expectedReplacementStateHash,
            ExpectedContentHash = expectedContentHash,
            Result = result,
            StableFailureCode = stableCode
        };
    }

    private static (string Result, string StableFailureCode) MapFailure(AgentMemoryOperationFailureCode code)
        => code switch
        {
            AgentMemoryOperationFailureCode.StateConflict => ("conflict", "state-conflict"),
            AgentMemoryOperationFailureCode.IdentityConflict => ("conflict", "identity-conflict"),
            AgentMemoryOperationFailureCode.ResourceUnavailable => ("rejected", "resource-unavailable"),
            AgentMemoryOperationFailureCode.InvalidLifecycleState => ("rejected", "invalid-lifecycle-state"),
            AgentMemoryOperationFailureCode.TenantMismatch => ("rejected", "tenant-mismatch"),
            AgentMemoryOperationFailureCode.MissingActor => ("rejected", "missing-actor"),
            AgentMemoryOperationFailureCode.MissingReason => ("rejected", "missing-reason"),
            AgentMemoryOperationFailureCode.MissingTimestamp => ("rejected", "missing-timestamp"),
            AgentMemoryOperationFailureCode.MissingSourceOrExplanation => ("rejected", "missing-source-or-explanation"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(code), code, "Unknown failure code cannot be recorded as a deterministic curation outcome.")
        };

    private static string MapStatus(AgentMemoryStatus status)
        => status switch
        {
            AgentMemoryStatus.Candidate => "candidate",
            AgentMemoryStatus.Active => "active",
            AgentMemoryStatus.Rejected => "rejected",
            AgentMemoryStatus.Superseded => "superseded",
            AgentMemoryStatus.Archived => "archived",
            _ => "unknown"
        };

    private static AgentMemoryAccountabilitySanitizationSummary MapSanitization(AgentMemoryItem item)
        => new()
        {
            State = item.RedactionKinds.Count > 0 ? "redacted" : "none",
            RedactionCodes = NormalizeCodes(item.RedactionKinds, MaxRedactionCodes),
            DiagnosticCodes = NormalizeCodes(item.SanitizationDiagnostics.Select(d => d.Code.RequireValue()), MaxDiagnosticCodes)
        };

    private static string[] NormalizeCodes(IEnumerable<string> codes, int maxCount)
        => codes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .Take(maxCount)
            .ToArray();
}
