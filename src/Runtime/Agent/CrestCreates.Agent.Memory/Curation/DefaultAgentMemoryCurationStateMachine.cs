using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Abstractions.CanonicalHashing;
using CrestCreates.Agent.Memory.Abstractions.Curation;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Curation;

/// <summary>
/// Default conditional curation state machine over already-loaded locked
/// snapshots. Owns Tenant comparison, expected state-hash comparison, allowed
/// source lifecycle states, canonical content-hash comparison, expected
/// new-state hash validation, and projection through
/// <see cref="IAgentMemoryCurationProjector"/>. It has no "not found" or
/// identity-availability query/result path; the Store owns
/// ResourceUnavailable/IdentityConflict before invoking it.
/// </summary>
public sealed class DefaultAgentMemoryCurationStateMachine : IAgentMemoryCurationStateMachine
{
    private readonly IAgentMemoryStateHashProjector _stateHashes;
    private readonly IAgentMemoryCurationProjector _projector;

    public DefaultAgentMemoryCurationStateMachine(
        IAgentMemoryStateHashProjector stateHashes,
        IAgentMemoryCurationProjector projector)
    {
        _stateHashes = stateHashes ?? throw new ArgumentNullException(nameof(stateHashes));
        _projector = projector ?? throw new ArgumentNullException(nameof(projector));
    }

    public AgentMemoryPromoteMutation PreparePromote(
        string tenantId,
        AgentMemoryCandidate candidate,
        AgentMemoryPromotionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(plan);
        EnsureTenant(candidate.TenantId, tenantId);
        EnsureMemoryIdentity(plan.NewMemoryId);
        EnsureCandidateExpectation(candidate, plan.Candidate);
        EnsureLifecycle(candidate.Status == AgentMemoryStatus.Candidate, "Candidate is not in Candidate state.");
        EnsureContentHash(candidate.CanonicalContentHash, plan.ExpectedMemoryContentHash);

        var memory = _projector.ProjectPromotedMemory(candidate, plan.NewMemoryId, plan.Operation);
        EnsureExpectedMemory(memory, plan.ExpectedMemoryStateHash);

        return new AgentMemoryPromoteMutation
        {
            Candidate = _projector.ProjectCandidateStatus(candidate, AgentMemoryStatus.Active),
            Memory = memory
        };
    }

    public AgentMemoryRejectMutation PrepareReject(
        string tenantId,
        AgentMemoryCandidate candidate,
        AgentMemoryCandidateExpectation expectation)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(expectation);
        EnsureTenant(candidate.TenantId, tenantId);
        EnsureCandidateExpectation(candidate, expectation);
        EnsureLifecycle(candidate.Status == AgentMemoryStatus.Candidate, "Candidate is not in Candidate state.");

        return new AgentMemoryRejectMutation
        {
            Candidate = _projector.ProjectCandidateStatus(candidate, AgentMemoryStatus.Rejected)
        };
    }

    public AgentMemorySupersedeMutation PrepareSupersede(
        string tenantId,
        AgentMemoryItem targetMemory,
        AgentMemoryCandidate replacementCandidate,
        AgentMemorySupersessionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(targetMemory);
        ArgumentNullException.ThrowIfNull(replacementCandidate);
        ArgumentNullException.ThrowIfNull(plan);
        EnsureTenant(targetMemory.TenantId, tenantId);
        EnsureTenant(replacementCandidate.TenantId, tenantId);
        EnsureMemoryExpectation(targetMemory, plan.TargetMemory);
        EnsureLifecycle(targetMemory.Status == AgentMemoryStatus.Active, "Target Memory is not Active.");
        EnsureCandidateExpectation(replacementCandidate, plan.ReplacementCandidate);
        EnsureLifecycle(replacementCandidate.Status == AgentMemoryStatus.Candidate, "Replacement Candidate is not in Candidate state.");
        EnsureContentHash(replacementCandidate.CanonicalContentHash, plan.ExpectedMemoryContentHash);
        EnsureMemoryIdentity(plan.NewMemoryId);
        if (string.Equals(targetMemory.MemoryId, plan.NewMemoryId, StringComparison.Ordinal))
            throw StateConflict("Supersede cannot replace a Memory with itself.");

        var superseded = _projector.ProjectSupersededMemory(targetMemory, plan.NewMemoryId);
        var superseding = _projector.ProjectSupersedingMemory(
            replacementCandidate, targetMemory.MemoryId, plan.NewMemoryId, plan.Operation);
        EnsureExpectedMemory(superseding, plan.ExpectedMemoryStateHash);

        return new AgentMemorySupersedeMutation
        {
            SupersededMemory = superseded,
            SupersedingMemory = superseding,
            ReplacementCandidate = _projector.ProjectCandidateStatus(replacementCandidate, AgentMemoryStatus.Active)
        };
    }

    public AgentMemoryArchiveMutation PrepareArchive(
        string tenantId,
        AgentMemoryItem memory,
        AgentMemoryItemExpectation expectation)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(expectation);
        EnsureTenant(memory.TenantId, tenantId);
        EnsureMemoryExpectation(memory, expectation);
        EnsureLifecycle(
            memory.Status is AgentMemoryStatus.Active or AgentMemoryStatus.Superseded,
            "Memory cannot be archived from its current state.");

        return new AgentMemoryArchiveMutation
        {
            Memory = _projector.ProjectArchivedMemory(memory)
        };
    }

    private void EnsureCandidateExpectation(AgentMemoryCandidate candidate, AgentMemoryCandidateExpectation expectation)
    {
        if (!string.Equals(candidate.CandidateId, expectation.CandidateId, StringComparison.Ordinal)
            || !_stateHashes.ComputeCandidateStateHash(candidate).Equals(expectation.ExpectedStateHash))
            throw StateConflict("Candidate state changed since preparation.");
    }

    private void EnsureMemoryExpectation(AgentMemoryItem memory, AgentMemoryItemExpectation expectation)
    {
        if (!string.Equals(memory.MemoryId, expectation.MemoryId, StringComparison.Ordinal)
            || !_stateHashes.ComputeMemoryStateHash(memory).Equals(expectation.ExpectedStateHash))
            throw StateConflict("Memory state changed since preparation.");
    }

    private void EnsureExpectedMemory(AgentMemoryItem memory, CanonicalHash expected)
    {
        if (!_stateHashes.ComputeMemoryStateHash(memory).Equals(expected))
            throw StateConflict("Prepared Memory state does not match the committed graph.");
    }

    private static void EnsureContentHash(CanonicalHash actual, CanonicalHash expected)
    {
        if (!actual.Equals(expected))
            throw StateConflict("Prepared content hash does not match Candidate payload.");
    }

    private static void EnsureMemoryIdentity(string memoryId)
    {
        if (string.IsNullOrWhiteSpace(memoryId))
            throw new AgentMemoryOperationException(
                AgentMemoryOperationFailureCode.IdentityConflict,
                "New Memory identity must not be empty or whitespace.");
    }

    private static void EnsureTenant(string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.TenantMismatch, "Tenant mismatch.");
    }

    private static void EnsureLifecycle(bool condition, string message)
    {
        if (!condition)
            throw new AgentMemoryOperationException(AgentMemoryOperationFailureCode.InvalidLifecycleState, message);
    }

    private static AgentMemoryOperationException StateConflict(string message)
        => new(AgentMemoryOperationFailureCode.StateConflict, message);
}
