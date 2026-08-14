using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Persistence.Testing.Cases;
using CrestCreates.Agent.Memory.Persistence.Testing.Drivers;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using CrestCreates.Runtime.Persistence.PostgreSql;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

/// <summary>
/// PostgreSQL runner for the shared Agent Memory curation contract cases plus
/// the PGS curation evidence-named methods.
/// </summary>
[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlAgentMemoryCurationContractTests : IAsyncLifetime
{
    private readonly PostgreSqlRuntimeCollectionFixture _fixture;
    private PostgreSqlRuntimeSchemaLease _lease = null!;
    private IAgentMemoryStoreContractDriver _driver = null!;

    public PostgreSqlAgentMemoryCurationContractTests(PostgreSqlRuntimeCollectionFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _lease = await _fixture.CreateSchemaLeaseAsync();
        _driver = new PostgreSqlAgentMemoryContractDriver(_lease);
    }

    public async Task DisposeAsync()
    {
        await _driver.DisposeAsync();
        await _lease.DisposeAsync();
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
    public Task PromotionPreparation_AndStoreMutation_Should_UseSameCurationProjection()
        => AgentMemoryCurationStoreContractCases.PromotionPreparation_AndStoreMutation_Should_UseSameCurationProjection(Driver);

    // ── Spec §17 evidence-named methods (PGS curation) ───────────────────────

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
    public Task Promote_OccupiedMemoryIdentity_Should_LeaveCandidateUnchanged()
        => AgentMemoryCurationStoreContractCases.Promote_OccupiedMemoryIdentity_Should_LeaveCandidateUnchanged(Driver);

    [Fact]
    public Task Promote_StaleCandidateHash_Should_ConflictWithoutMutation()
        => AgentMemoryCurationStoreContractCases.Promote_With_StaleCandidateHash_Should_Conflict(Driver);

    [Fact]
    public Task Reject_StaleExpectation_Should_HaveZeroMutation()
        => AgentMemoryCurationStoreContractCases.Reject_StaleExpectation_Should_HaveZeroMutation(Driver);

    [Fact]
    public Task PromotionPreparation_AndStoreMutation_Should_UseSameCurationProjection_Evidence()
        => AgentMemoryCurationStoreContractCases.PromotionPreparation_AndStoreMutation_Should_UseSameCurationProjection(Driver);
}
