using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Persistence.Testing.Assertions;
using CrestCreates.Agent.Memory.Persistence.Testing.Drivers;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using ContractAssertions = CrestCreates.Agent.Memory.Persistence.Testing.Assertions.AgentMemoryPersistenceContractAssertions;

namespace CrestCreates.Agent.Memory.Persistence.Testing.Cases;

/// <summary>
/// Provider-neutral conditional curation contract cases. Each method carries
/// the exact Spec §18.1 curation skeleton name and is activated by concrete
/// InMemory and PostgreSQL runners. Cases never compute or hard-code state
/// hashes; the driver prepares all expectations through the real shared
/// projectors so preparation and locked Store mutation share one truth.
/// </summary>
public static class AgentMemoryCurationStoreContractCases
{
    public static async Task Promote_Should_Be_Atomic(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var store = RequireConditionalStore(driver);
        var candidate = Candidate("tenant-a", "candidate-promote");
        await driver.MemoryStore.CreateCandidateAsync(candidate, cancellationToken);

        var operation = Operation("tenant-a", "op-promote-1");
        var plan = driver.PreparePromotionPlan(candidate, "memory-promoted", operation);

        var committed = await store.PromoteAsync("tenant-a", plan, cancellationToken);

        ContractAssertions.Equal("memory-promoted", committed.MemoryId, "Promote must return the committed Memory.");
        ContractAssertions.Equal(AgentMemoryStatus.Active, committed.Status, "Promoted Memory must be Active.");
        ContractAssertions.Equal(false, committed.IsAuthoritative, "Promoted Memory must be non-authoritative.");
        ContractAssertions.Equal(operation.Identity.OccurredAt, committed.PromotedAt, "PromotedAt must come from Operation.Identity.OccurredAt.");
        ContractAssertions.Equal(candidate.CanonicalContentHash, committed.CanonicalContentHash, "promoted content hash must match the Candidate.");

        var storedCandidate = await driver.MemoryStore.GetCandidateAsync("tenant-a", candidate.CandidateId, cancellationToken);
        ContractAssertions.NotNull(storedCandidate, "candidate must exist after Promote.");
        ContractAssertions.Equal(AgentMemoryStatus.Active, storedCandidate!.Status, "Candidate must transition to Active atomically.");

        var storedMemory = await driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-promoted", cancellationToken);
        ContractAssertions.NotNull(storedMemory, "promoted Memory must exist.");
        ContractAssertions.Equal(committed, storedMemory, "committed and stored Memory snapshots must be identical.");
    }

    public static async Task Promote_With_StaleCandidateHash_Should_Conflict(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var store = RequireConditionalStore(driver);
        var candidate = Candidate("tenant-a", "candidate-stale");
        await driver.MemoryStore.CreateCandidateAsync(candidate, cancellationToken);

        var plan = driver.PreparePromotionPlan(candidate, "memory-stale", Operation("tenant-a", "op-stale"));
        var stalePlan = plan with
        {
            Candidate = new AgentMemoryCandidateExpectation
            {
                CandidateId = candidate.CandidateId,
                ExpectedStateHash = Tampered(plan.Candidate.ExpectedStateHash)
            }
        };

        var failure = await ContractAssertions.ThrowsAsync<AgentMemoryOperationException>(
            () => store.PromoteAsync("tenant-a", stalePlan, cancellationToken).AsTask(),
            "Promote with a stale Candidate expectation must fail.");

        ContractAssertions.MemoryOperationFailure(
            AgentMemoryOperationFailureCode.StateConflict,
            failure,
            "Stale Candidate hash must surface StateConflict with zero mutation.");

        var storedCandidate = await driver.MemoryStore.GetCandidateAsync("tenant-a", candidate.CandidateId, cancellationToken);
        ContractAssertions.NotNull(storedCandidate, "candidate must remain after a stale Promote.");
        ContractAssertions.Equal(AgentMemoryStatus.Candidate, storedCandidate!.Status, "failed Promote must not mutate the Candidate.");
        var storedMemory = await driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-stale", cancellationToken);
        ContractAssertions.Null(storedMemory, "failed Promote must not create a Memory.");
    }

    public static async Task ConcurrentPromote_Should_Have_ExactlyOneWinner(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var store = RequireConditionalStore(driver);
        var candidate = Candidate("tenant-a", "candidate-concurrent-promote");
        await driver.MemoryStore.CreateCandidateAsync(candidate, cancellationToken);

        var plan = driver.PreparePromotionPlan(candidate, "memory-concurrent-promote", Operation("tenant-a", "op-concurrent-promote"));
        var results = await System.Threading.Tasks.Task.WhenAll(
            RunPromote(store, plan, cancellationToken),
            RunPromote(store, plan, cancellationToken));

        var successes = results.Where(result => result).Count();
        ContractAssertions.Equal(1, successes, "exactly one concurrent Promote must win.");

        var storedCandidate = await driver.MemoryStore.GetCandidateAsync("tenant-a", candidate.CandidateId, cancellationToken);
        ContractAssertions.Equal(AgentMemoryStatus.Active, storedCandidate!.Status, "the winner must leave the Candidate Active.");
        var storedMemory = await driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-concurrent-promote", cancellationToken);
        ContractAssertions.NotNull(storedMemory, "exactly one Memory must exist.");
        ContractAssertions.Equal("memory-concurrent-promote", storedMemory!.MemoryId, "the committed Memory identity must match the plan.");
    }

    public static async Task Reject_Should_Be_Conditional(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var store = RequireConditionalStore(driver);
        var candidate = Candidate("tenant-a", "candidate-reject");
        await driver.MemoryStore.CreateCandidateAsync(candidate, cancellationToken);

        var expectation = driver.PrepareCandidateExpectation(candidate);
        await store.RejectAsync("tenant-a", expectation, Operation("tenant-a", "op-reject"), cancellationToken);

        var stored = await driver.MemoryStore.GetCandidateAsync("tenant-a", candidate.CandidateId, cancellationToken);
        ContractAssertions.NotNull(stored, "candidate must exist after Reject.");
        ContractAssertions.Equal(AgentMemoryStatus.Rejected, stored!.Status, "Reject must transition the Candidate to Rejected.");
    }

    public static async Task Supersede_Should_Commit_ThreePartGraph_Atomically(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var store = RequireConditionalStore(driver);
        var candidate = Candidate("tenant-a", "candidate-supersede-source");
        await driver.MemoryStore.CreateCandidateAsync(candidate, cancellationToken);
        var promotePlan = driver.PreparePromotionPlan(candidate, "memory-original", Operation("tenant-a", "op-supersede-1"));
        var original = await store.PromoteAsync("tenant-a", promotePlan, cancellationToken);

        var replacement = Candidate("tenant-a", "candidate-supersede-replacement", AgentMemoryKind.Decision);
        await driver.MemoryStore.CreateCandidateAsync(replacement, cancellationToken);

        var supersession = driver.PrepareSupersessionPlan(
            original, replacement, "memory-replacement", Operation("tenant-a", "op-supersede-2"));
        var committed = await store.SupersedeAsync("tenant-a", supersession, cancellationToken);

        ContractAssertions.Equal("memory-replacement", committed.MemoryId, "Supersede must return the new Memory.");

        var oldMemory = await driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-original", cancellationToken);
        ContractAssertions.NotNull(oldMemory, "old Memory must exist after Supersede.");
        ContractAssertions.Equal(AgentMemoryStatus.Superseded, oldMemory!.Status, "old Memory must be Superseded.");
        ContractAssertions.Equal("memory-replacement", oldMemory.SupersededByMemoryId, "old Memory must point to the new Memory.");
        ContractAssertions.Null(oldMemory.SupersedesMemoryId, "old Memory must retain its original SupersedesMemoryId.");

        var newMemory = await driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-replacement", cancellationToken);
        ContractAssertions.NotNull(newMemory, "new Memory must exist after Supersede.");
        ContractAssertions.Equal(AgentMemoryStatus.Active, newMemory!.Status, "new Memory must be Active.");
        ContractAssertions.Equal(false, newMemory.IsAuthoritative, "new Memory must be non-authoritative.");
        ContractAssertions.Equal("memory-original", newMemory.SupersedesMemoryId, "new Memory must point back to the old Memory.");
        ContractAssertions.Null(newMemory.SupersededByMemoryId, "new Memory must have no SupersededByMemoryId.");
        ContractAssertions.Equal(committed, newMemory, "committed and stored new Memory snapshots must be identical.");

        var replacementCandidate = await driver.MemoryStore.GetCandidateAsync("tenant-a", replacement.CandidateId, cancellationToken);
        ContractAssertions.NotNull(replacementCandidate, "replacement Candidate must exist after Supersede.");
        ContractAssertions.Equal(AgentMemoryStatus.Active, replacementCandidate!.Status, "replacement Candidate must be Active.");
    }

    public static async Task Supersede_Failure_Should_Expose_No_PartialGraph(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var store = RequireConditionalStore(driver);
        var candidate = Candidate("tenant-a", "candidate-supersede-fail-source");
        await driver.MemoryStore.CreateCandidateAsync(candidate, cancellationToken);
        var promotePlan = driver.PreparePromotionPlan(candidate, "memory-fail-original", Operation("tenant-a", "op-fail-1"));
        var original = await store.PromoteAsync("tenant-a", promotePlan, cancellationToken);

        var replacement = Candidate("tenant-a", "candidate-supersede-fail-replacement", AgentMemoryKind.Decision);
        await driver.MemoryStore.CreateCandidateAsync(replacement, cancellationToken);

        var supersession = driver.PrepareSupersessionPlan(
            original, replacement, "memory-fail-new", Operation("tenant-a", "op-fail-2"));
        var staleSupersession = supersession with
        {
            TargetMemory = new AgentMemoryItemExpectation
            {
                MemoryId = original.MemoryId,
                ExpectedStateHash = Tampered(supersession.TargetMemory.ExpectedStateHash)
            }
        };

        var failure = await ContractAssertions.ThrowsAsync<AgentMemoryOperationException>(
            () => store.SupersedeAsync("tenant-a", staleSupersession, cancellationToken).AsTask(),
            "Supersede with a stale target expectation must fail.");

        ContractAssertions.MemoryOperationFailure(
            AgentMemoryOperationFailureCode.StateConflict,
            failure,
            "Stale target hash must surface StateConflict.");

        var oldMemory = await driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-fail-original", cancellationToken);
        ContractAssertions.NotNull(oldMemory, "old Memory must remain after failed Supersede.");
        ContractAssertions.Equal(AgentMemoryStatus.Active, oldMemory!.Status, "failed Supersede must leave the old Memory Active.");
        ContractAssertions.Null(oldMemory.SupersededByMemoryId, "failed Supersede must not link the old Memory.");
        var newMemory = await driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-fail-new", cancellationToken);
        ContractAssertions.Null(newMemory, "failed Supersede must not create the new Memory.");
        var replacementCandidate = await driver.MemoryStore.GetCandidateAsync("tenant-a", replacement.CandidateId, cancellationToken);
        ContractAssertions.Equal(AgentMemoryStatus.Candidate, replacementCandidate!.Status, "failed Supersede must leave the replacement Candidate unchanged.");
    }

    public static async Task Archive_Should_Be_Conditional(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var store = RequireConditionalStore(driver);
        var candidate = Candidate("tenant-a", "candidate-archive");
        await driver.MemoryStore.CreateCandidateAsync(candidate, cancellationToken);
        var promotePlan = driver.PreparePromotionPlan(candidate, "memory-archive", Operation("tenant-a", "op-archive-1"));
        var memory = await store.PromoteAsync("tenant-a", promotePlan, cancellationToken);

        var expectation = driver.PrepareMemoryExpectation(memory);
        var archived = await store.ArchiveAsync("tenant-a", expectation, Operation("tenant-a", "op-archive-2"), cancellationToken);

        ContractAssertions.Equal(AgentMemoryStatus.Archived, archived.Status, "Archive must return the Archived Memory.");
        ContractAssertions.Equal("memory-archive", archived.MemoryId, "Archive must return the same Memory identity.");

        var stored = await driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-archive", cancellationToken);
        ContractAssertions.NotNull(stored, "archived Memory must still be readable.");
        ContractAssertions.Equal(AgentMemoryStatus.Archived, stored!.Status, "stored Memory must be Archived.");
        ContractAssertions.Equal(archived, stored, "committed and stored archived snapshots must be identical.");
    }

    public static async Task ConcurrentArchive_Should_Have_ExactlyOneWinner(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var store = RequireConditionalStore(driver);
        var candidate = Candidate("tenant-a", "candidate-concurrent-archive");
        await driver.MemoryStore.CreateCandidateAsync(candidate, cancellationToken);
        var promotePlan = driver.PreparePromotionPlan(candidate, "memory-concurrent-archive", Operation("tenant-a", "op-ca-1"));
        var memory = await store.PromoteAsync("tenant-a", promotePlan, cancellationToken);

        var expectation = driver.PrepareMemoryExpectation(memory);
        var operation = Operation("tenant-a", "op-ca-2");
        var results = await System.Threading.Tasks.Task.WhenAll(
            RunArchive(store, expectation, operation, cancellationToken),
            RunArchive(store, expectation, operation, cancellationToken));

        var successes = results.Where(result => result).Count();
        ContractAssertions.Equal(1, successes, "exactly one concurrent Archive must win.");

        var stored = await driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-concurrent-archive", cancellationToken);
        ContractAssertions.Equal(AgentMemoryStatus.Archived, stored!.Status, "the winner must leave the Memory Archived.");
    }

    public static async Task CurationCapabilities_Should_Be_ConfirmedAtomic(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var store = RequireConditionalStore(driver);
        var capabilities = store as IAgentMemoryStoreCapabilities;
        ContractAssertions.NotNull(capabilities, "the selected Memory Store must implement IAgentMemoryStoreCapabilities.");
        ContractAssertions.Equal(
            AgentMemoryCurationOutcomeGuarantee.ConfirmedAtomic,
            capabilities!.CurationOutcomeGuarantee,
            "the selected Memory Store must truthfully report ConfirmedAtomic.");
    }

    public static async Task PromotionPreparation_AndStoreMutation_Should_UseSameCurationProjection(
        IAgentMemoryStoreContractDriver driver,
        CancellationToken cancellationToken = default)
    {
        var store = RequireConditionalStore(driver);
        var candidate = Candidate("tenant-a", "candidate-projection-parity");
        await driver.MemoryStore.CreateCandidateAsync(candidate, cancellationToken);

        var operation = Operation("tenant-a", "op-projection-parity");
        var plan = driver.PreparePromotionPlan(candidate, "memory-projection-parity", operation);
        var projected = driver.ProjectPromotedMemory(candidate, "memory-projection-parity", operation);

        var committed = await store.PromoteAsync("tenant-a", plan, cancellationToken);

        ContractAssertions.Equal(projected, committed, "prepared projection and committed Memory must be value-identical.");

        var stored = await driver.MemoryStore.GetMemoryAsync("tenant-a", "memory-projection-parity", cancellationToken);
        ContractAssertions.Equal(projected, stored, "prepared projection and persisted Memory must be value-identical.");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static IAgentMemoryConditionalCurationStore RequireConditionalStore(IAgentMemoryStoreContractDriver driver)
    {
        var store = driver.MemoryStore as IAgentMemoryConditionalCurationStore;
        ContractAssertions.NotNull(store, "the selected Memory Store must implement IAgentMemoryConditionalCurationStore.");
        return store!;
    }

    private static CanonicalHash Tampered(CanonicalHash original)
        => original with { Value = original.Value + "-tampered" };

    private static async Task<bool> RunPromote(
        IAgentMemoryConditionalCurationStore store,
        AgentMemoryPromotionPlan plan,
        CancellationToken cancellationToken)
    {
        try
        {
            await store.PromoteAsync(plan.Operation.TenantId, plan, cancellationToken);
            return true;
        }
        catch (AgentMemoryOperationException)
        {
            return false;
        }
    }

    private static async Task<bool> RunArchive(
        IAgentMemoryConditionalCurationStore store,
        AgentMemoryItemExpectation expectation,
        AgentMemoryOperationRequest operation,
        CancellationToken cancellationToken)
    {
        try
        {
            await store.ArchiveAsync(operation.TenantId, expectation, operation, cancellationToken);
            return true;
        }
        catch (AgentMemoryOperationException)
        {
            return false;
        }
    }

    private static AgentMemoryCandidate Candidate(string tenantId, string candidateId, AgentMemoryKind kind = AgentMemoryKind.Preference)
        => new()
        {
            TenantId = tenantId,
            CandidateId = candidateId,
            Kind = kind,
            Content = $"content-{candidateId}",
            CanonicalContentHash = CanonicalHashStub.For($"candidate-{candidateId}"),
            Confidence = AgentMemoryConfidence.Medium
        };

    private static AgentMemoryOperationRequest Operation(string tenantId, string operationId)
        => new()
        {
            TenantId = tenantId,
            InvocationContext = new AgentMemoryInvocationContext
            {
                TenantId = tenantId,
                ActorId = "contract-runner",
                ActorKind = "system",
                CorrelationId = $"correlation-{operationId}",
                InvocationSource = "system"
            },
            Reason = "contract case",
            Identity = new AgentMemoryOperationIdentity
            {
                OperationId = operationId,
                OccurredAt = DateTimeOffset.UnixEpoch.AddSeconds(10)
            },
            Explanation = "contract case explanation"
        };
}
