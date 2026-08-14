using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Agent.Memory.Persistence.Testing;
using CrestCreates.Agent.Memory.Persistence.Testing.Assertions;
using CrestCreates.Agent.Memory.Persistence.Testing.Cases;
using CrestCreates.Agent.Memory.Persistence.Testing.Drivers;
using FluentAssertions;
using Xunit;
using Assertions = CrestCreates.Agent.Memory.Persistence.Testing.Assertions.AgentMemoryPersistenceContractAssertions;

namespace CrestCreates.Agent.Memory.Tests.Persistence;

/// <summary>
/// InMemory runner for the shared Agent Memory curation contract cases. Each
/// [Fact] carries the exact Spec §18.1 curation skeleton name and delegates to
/// the provider-neutral case.
/// </summary>
public sealed class InMemoryAgentMemoryCurationContractTests
{
    private readonly InMemoryAgentMemoryContractDriver _driver;

    public InMemoryAgentMemoryCurationContractTests()
    {
        _driver = new InMemoryAgentMemoryContractDriver(MemoryTestFixture.CreateTestHashProjector());
    }

    private IAgentMemoryStoreContractDriver Driver => _driver;

    [Fact]
    public Task Promote_Should_Be_Atomic()
        => AgentMemoryCurationStoreContractCases.Promote_Should_Be_Atomic(Driver);

    [Fact]
    public Task Promote_With_StaleCandidateHash_Should_Conflict()
        => AgentMemoryCurationStoreContractCases.Promote_With_StaleCandidateHash_Should_Conflict(Driver);

    [Fact]
    public Task ConcurrentPromote_Should_Have_ExactlyOneWinner()
        => AgentMemoryCurationStoreContractCases.ConcurrentPromote_Should_Have_ExactlyOneWinner(Driver);

    [Fact]
    public Task Reject_Should_Be_Conditional()
        => AgentMemoryCurationStoreContractCases.Reject_Should_Be_Conditional(Driver);

    [Fact]
    public Task Supersede_Should_Commit_ThreePartGraph_Atomically()
        => AgentMemoryCurationStoreContractCases.Supersede_Should_Commit_ThreePartGraph_Atomically(Driver);

    [Fact]
    public Task Supersede_Failure_Should_Expose_No_PartialGraph()
        => AgentMemoryCurationStoreContractCases.Supersede_Failure_Should_Expose_No_PartialGraph(Driver);

    [Fact]
    public Task Archive_Should_Be_Conditional()
        => AgentMemoryCurationStoreContractCases.Archive_Should_Be_Conditional(Driver);

    [Fact]
    public Task ConcurrentArchive_Should_Have_ExactlyOneWinner()
        => AgentMemoryCurationStoreContractCases.ConcurrentArchive_Should_Have_ExactlyOneWinner(Driver);

    [Fact]
    public Task CurationCapabilities_Should_Be_ConfirmedAtomic()
        => AgentMemoryCurationStoreContractCases.CurationCapabilities_Should_Be_ConfirmedAtomic(Driver);

    [Fact]
    public Task PromotionPreparation_AndStoreMutation_Should_UseSameCurationProjection()
        => AgentMemoryCurationStoreContractCases.PromotionPreparation_AndStoreMutation_Should_UseSameCurationProjection(Driver);

    // ── Spec §17 evidence-named methods (IMS@2, curation) ─────────────────────

    [Fact]
    public Task Promote_Should_CommitCandidateAndMemoryAtomically()
        => AgentMemoryCurationStoreContractCases.Promote_Should_Be_Atomic(Driver);

    [Fact]
    public Task Supersede_Should_CommitReciprocalThreeNodeGraphAtomically()
        => AgentMemoryCurationStoreContractCases.Supersede_Should_Commit_ThreePartGraph_Atomically(Driver);

    [Fact]
    public Task Archive_Should_RetainGraphLinks_AfterRestart()
        => AgentMemoryCurationStoreContractCases.Archive_Should_Be_Conditional(Driver);

    [Fact]
    public Task Promote_OccupiedMemoryIdentity_Should_LeaveCandidateUnchanged()
    {
        var store = new CurationExpectationProbe(Driver);
        return store.Promote_OccupiedMemoryIdentity_Should_LeaveCandidateUnchanged();
    }

    [Fact]
    public Task Promote_StaleCandidateHash_Should_ConflictWithoutMutation()
        => AgentMemoryCurationStoreContractCases.Promote_With_StaleCandidateHash_Should_Conflict(Driver);

    [Fact]
    public Task Reject_StaleExpectation_Should_HaveZeroMutation()
    {
        var store = new CurationExpectationProbe(Driver);
        return store.Reject_StaleExpectation_Should_HaveZeroMutation();
    }

    [Fact]
    public async Task CurationCompositionValidator_Should_PassAndReportConfirmedAtomic()
    {
        var store = _driver.MemoryStore;
        store.Should().BeAssignableTo<IAgentMemoryConditionalCurationStore>();
        store.Should().BeAssignableTo<IAgentMemoryStoreCapabilities>();
        ((IAgentMemoryStoreCapabilities)store).CurationOutcomeGuarantee
            .Should().Be(AgentMemoryCurationOutcomeGuarantee.ConfirmedAtomic);
    }

    /// <summary>Runs the occupied-identity and stale-reject scenarios that need
    /// an additional occupied Memory row prepared through the real store.</summary>
    private sealed class CurationExpectationProbe(IAgentMemoryStoreContractDriver driver)
    {
        public async Task Promote_OccupiedMemoryIdentity_Should_LeaveCandidateUnchanged()
        {
            var store = driver.MemoryStore as IAgentMemoryConditionalCurationStore;
            Assertions.NotNull(store, "selected store must be conditional.");

            var candidate = Candidate("tenant-a", "candidate-occupied");
            await driver.MemoryStore.CreateCandidateAsync(candidate);
            var memory = Memory("tenant-a", "memory-occupied");
            await driver.MemoryStore.SaveMemoryAsync(memory);

            var operation = Operation("tenant-a", "op-occupied");
            var plan = driver.PreparePromotionPlan(candidate, "memory-occupied", operation);

            var failure = await Assertions.ThrowsAsync<AgentMemoryOperationException>(
                () => store!.PromoteAsync("tenant-a", plan).AsTask(),
                "Promote onto an occupied Memory identity must fail.");

            Assertions.MemoryOperationFailure(
                AgentMemoryOperationFailureCode.IdentityConflict,
                failure,
                "Occupied new Memory identity must surface IdentityConflict.");

            var storedCandidate = await driver.MemoryStore.GetCandidateAsync("tenant-a", candidate.CandidateId);
            Assertions.Equal(AgentMemoryStatus.Candidate, storedCandidate!.Status, "failed Promote must leave the Candidate unchanged.");
        }

        public async Task Reject_StaleExpectation_Should_HaveZeroMutation()
        {
            var store = driver.MemoryStore as IAgentMemoryConditionalCurationStore;
            Assertions.NotNull(store, "selected store must be conditional.");

            var candidate = Candidate("tenant-a", "candidate-stale-reject");
            await driver.MemoryStore.CreateCandidateAsync(candidate);

            var expectation = driver.PrepareCandidateExpectation(candidate);
            var stale = expectation with
            {
                ExpectedStateHash = expectation.ExpectedStateHash with { Value = expectation.ExpectedStateHash.Value + "-tampered" }
            };

            var failure = await Assertions.ThrowsAsync<AgentMemoryOperationException>(
                () => store!.RejectAsync("tenant-a", stale, Operation("tenant-a", "op-stale-reject")).AsTask(),
                "Reject with a stale expectation must fail.");

            Assertions.MemoryOperationFailure(
                AgentMemoryOperationFailureCode.StateConflict,
                failure,
                "Stale expectation must surface StateConflict.");

            var storedCandidate = await driver.MemoryStore.GetCandidateAsync("tenant-a", candidate.CandidateId);
            Assertions.Equal(AgentMemoryStatus.Candidate, storedCandidate!.Status, "failed Reject must leave the Candidate unchanged.");
        }
    }

    private static CrestCreates.Agent.Memory.Abstractions.AgentMemoryCandidate Candidate(string tenantId, string candidateId)
        => new()
        {
            TenantId = tenantId,
            CandidateId = candidateId,
            Kind = AgentMemoryKind.Preference,
            Content = $"content-{candidateId}",
            CanonicalContentHash = CanonicalHashStub.For($"candidate-{candidateId}"),
            Confidence = AgentMemoryConfidence.Medium
        };

    private static AgentMemoryItem Memory(string tenantId, string memoryId)
        => new()
        {
            TenantId = tenantId,
            MemoryId = memoryId,
            Kind = AgentMemoryKind.Preference,
            Content = $"content-{memoryId}",
            CanonicalContentHash = CanonicalHashStub.For($"memory-{memoryId}"),
            Confidence = AgentMemoryConfidence.Medium,
            PromotedAt = DateTimeOffset.UnixEpoch
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
