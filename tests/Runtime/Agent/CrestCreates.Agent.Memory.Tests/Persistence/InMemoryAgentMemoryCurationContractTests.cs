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
    public Task BlankNewMemoryIdentity_Should_BeRejected()
        => AgentMemoryCurationStoreContractCases.BlankNewMemoryIdentity_Should_BeRejected(Driver);
    [Fact]
    public Task CurationCapabilities_Should_Be_ConfirmedAtomic()
        => AgentMemoryCurationStoreContractCases.CurationCapabilities_Should_Be_ConfirmedAtomic(Driver);

    [Fact]
    public Task Promote_OccupiedMemoryIdentity_Should_LeaveCandidateUnchanged()
        => AgentMemoryCurationStoreContractCases.Promote_OccupiedMemoryIdentity_Should_LeaveCandidateUnchanged(Driver);

    [Fact]
    public Task Reject_StaleExpectation_Should_HaveZeroMutation()
        => AgentMemoryCurationStoreContractCases.Reject_StaleExpectation_Should_HaveZeroMutation(Driver);

    [Fact]
    public Task Archive_Should_RetainGraphLinks()
        => AgentMemoryCurationStoreContractCases.Archive_Should_RetainGraphLinks(Driver);

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
        => AgentMemoryCurationStoreContractCases.Archive_Should_RetainGraphLinks(Driver);

    [Fact]
    public Task Promote_StaleCandidateHash_Should_ConflictWithoutMutation()
        => AgentMemoryCurationStoreContractCases.Promote_With_StaleCandidateHash_Should_Conflict(Driver);

    [Fact]
    public async Task CurationCompositionValidator_Should_PassAndReportConfirmedAtomic()
    {
        var store = _driver.MemoryStore;
        store.Should().BeAssignableTo<IAgentMemoryConditionalCurationStore>();
        store.Should().BeAssignableTo<IAgentMemoryStoreCapabilities>();
        ((IAgentMemoryStoreCapabilities)store).CurationOutcomeGuarantee
            .Should().Be(AgentMemoryCurationOutcomeGuarantee.ConfirmedAtomic);
    }
}
