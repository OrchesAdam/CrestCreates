using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Persistence.Testing.Cases;
using CrestCreates.Agent.Memory.Persistence.Testing.Drivers;
using Xunit;

namespace CrestCreates.Agent.Memory.Tests.Persistence;

/// <summary>
/// InMemory runner for the shared Agent Memory Store contract cases. Each
/// [Fact] carries the exact Spec §18.1 skeleton name and delegates to the
/// provider-neutral case; the evidence-named methods (Spec §17 normative
/// names) close the IMS@2 evidence tuples.
/// </summary>
public sealed class InMemoryAgentMemoryStoreContractTests
{
    private readonly InMemoryAgentMemoryContractDriver _driver;

    public InMemoryAgentMemoryStoreContractTests()
    {
        _driver = new InMemoryAgentMemoryContractDriver(MemoryTestFixture.CreateTestHashProjector());
    }

    private IAgentMemoryStoreContractDriver Driver => _driver;

    [Fact]
    public Task Conversation_Should_Preserve_TenantIsolation()
        => AgentMemoryStoreContractCases.Conversation_Should_Preserve_TenantIsolation(Driver);

    [Fact]
    public Task Conversation_Should_Return_Snapshot()
        => AgentMemoryStoreContractCases.Conversation_Should_Return_Snapshot(Driver);

    [Fact]
    public Task Conversation_Should_Persist_Only_Sanitized_Turns()
        => AgentMemoryStoreContractCases.Conversation_Should_Persist_Only_Sanitized_Turns(Driver);

    [Fact]
    public Task Conversation_Should_Preserve_TurnSequence()
        => AgentMemoryStoreContractCases.Conversation_Should_Preserve_TurnSequence(Driver);

    [Fact]
    public Task Task_Should_Preserve_TenantIsolation()
        => AgentMemoryStoreContractCases.Task_Should_Preserve_TenantIsolation(Driver);

    [Fact]
    public Task Task_Should_Return_Snapshot()
        => AgentMemoryStoreContractCases.Task_Should_Return_Snapshot(Driver);

    [Fact]
    public Task Task_Should_Persist_Only_Sanitized_Content()
        => AgentMemoryStoreContractCases.Task_Should_Persist_Only_Sanitized_Content(Driver);

    [Fact]
    public Task Task_Should_Preserve_Deterministic_Order()
        => AgentMemoryStoreContractCases.Task_Should_Preserve_Deterministic_Order(Driver);

    [Fact]
    public Task Concurrent_TaskAppend_Should_Not_Lose_Event()
        => AgentMemoryStoreContractCases.Concurrent_TaskAppend_Should_Not_Lose_Event(Driver);

    [Fact]
    public Task TaskAppend_MissingTask_Should_Return_ResourceUnavailable()
        => AgentMemoryStoreContractCases.TaskAppend_MissingTask_Should_Return_ResourceUnavailable(Driver);

    [Fact]
    public Task CompressedContext_Should_Return_Snapshot()
        => AgentMemoryStoreContractCases.CompressedContext_Should_Return_Snapshot(Driver);

    [Fact]
    public Task CompressedContext_Should_Reject_CrossTenant_Block()
        => AgentMemoryStoreContractCases.CompressedContext_Should_Reject_CrossTenant_Block(Driver);

    [Fact]
    public Task BlockIdentity_Should_Be_TenantWide_Unique()
        => AgentMemoryStoreContractCases.BlockIdentity_Should_Be_TenantWide_Unique(Driver);

    [Fact]
    public Task ReplacingContext_Should_Remove_Old_BlockProjection()
        => AgentMemoryStoreContractCases.ReplacingContext_Should_Remove_Old_BlockProjection(Driver);

    [Fact]
    public Task Candidate_Should_Return_Snapshot()
        => AgentMemoryStoreContractCases.Candidate_Should_Return_Snapshot(Driver);

    [Fact]
    public Task Memory_Should_Return_Snapshot()
        => AgentMemoryStoreContractCases.Memory_Should_Return_Snapshot(Driver);

    [Fact]
    public Task SaveMemory_Should_Be_CreateOrExactReplay()
        => AgentMemoryStoreContractCases.SaveMemory_Should_Be_CreateOrExactReplay(Driver);

    [Fact]
    public Task SaveMemory_InvalidInitialLifecycleOrAuthority_Should_BeRejected()
        => AgentMemoryStoreContractCases.SaveMemory_InvalidInitialLifecycleOrAuthority_Should_BeRejected(Driver);

    [Fact]
    public Task ListMemories_Should_Be_Ordinally_Deterministic()
        => AgentMemoryStoreContractCases.ListMemories_Should_Be_Ordinally_Deterministic(Driver);

    [Fact]
    public Task ListStores_NonBmpIdentifiers_Should_Match_StringComparerOrdinal()
        => AgentMemoryStoreContractCases.ListStores_NonBmpIdentifiers_Should_Match_StringComparerOrdinal(Driver);

    [Fact]
    public Task Memory_Query_Should_Match_InMemory_Contract()
        => AgentMemoryStoreContractCases.Memory_Query_Should_Match_InMemory_Contract(Driver);

    // ── Spec §17 evidence-named methods (IMS@2) ──────────────────────────────

    [Fact]
    public Task Conversation_SaveAndRestart_Should_PreserveSanitizedSnapshotAndTurnSequence()
        => AgentMemoryStoreContractCases.Conversation_Should_Preserve_TurnSequence(Driver);

    [Fact]
    public Task Task_SaveAppendAndRestart_Should_PreserveSanitizedSnapshotAndEventSequence()
        => AgentMemoryStoreContractCases.Task_Should_Preserve_Deterministic_Order(Driver);

    [Fact]
    public Task ContextCreate_WithBlocks_Should_SatisfyImmediateForeignKey_AndRestartLookup()
        => AgentMemoryStoreContractCases.CompressedContext_Should_Return_Snapshot(Driver);

    [Fact]
    public Task SaveMemory_ExactReplay_Should_NotMutateRevisionOrState()
        => AgentMemoryStoreContractCases.SaveMemory_Should_Be_CreateOrExactReplay(Driver);

    [Fact]
    public Task AllStores_Should_IsolateSameIdentityAcrossTenants()
        => AgentMemoryStoreContractCases.AllStores_Should_IsolateSameIdentityAcrossTenants(Driver);

    [Fact]
    public Task AllCrossTenantLookups_Should_ReturnNullOrEmptyWithoutLeakage()
        => AgentMemoryStoreContractCases.AllCrossTenantLookups_Should_ReturnNullOrEmptyWithoutLeakage(Driver);

    [Fact]
    public Task BlockIdentity_Should_BeIndependentAcrossTenants()
        => AgentMemoryStoreContractCases.BlockIdentity_Should_BeIndependentAcrossTenants(Driver);

    [Fact]
    public Task BlockIdentity_Should_BeTenantWideUniqueAcrossContexts()
        => AgentMemoryStoreContractCases.BlockIdentity_Should_Be_TenantWide_Unique(Driver);

    [Fact]
    public Task ReplacingContext_Should_RemoveOldBlockProjectionAtomically()
        => AgentMemoryStoreContractCases.ReplacingContext_Should_Remove_Old_BlockProjection(Driver);

    [Fact]
    public Task OrderedArtifacts_Should_PreserveSubmittedSequence_NotTimestampOrIdOrder()
        => AgentMemoryStoreContractCases.OrderedArtifacts_Should_PreserveSubmittedSequence_NotTimestampOrIdOrder(Driver);

    [Fact]
    public Task IncludeStale_Should_RemainNoOp_WithoutStaleSchema()
        => AgentMemoryStoreContractCases.IncludeStale_Should_RemainNoOp_WithoutStaleSchema(Driver);

    [Fact]
    public Task AllStores_Should_ReturnDetachedSnapshots()
        => AgentMemoryStoreContractCases.AllStores_Should_ReturnDetachedSnapshots(Driver);

    [Fact]
    public Task CandidateBatch_WithOneConflict_Should_WriteNone()
        => AgentMemoryStoreContractCases.CandidateBatch_WithOneConflict_Should_WriteNone(Driver);

    [Fact]
    public Task SaveMemory_ExistingOneFieldDifference_Should_ReturnStateConflict()
        => AgentMemoryStoreContractCases.SaveMemory_ExistingOneFieldDifference_Should_ReturnStateConflict(Driver);

    [Fact]
    public Task SaveMemory_Should_Not_CreateOneSidedSupersedeGraph()
        => AgentMemoryStoreContractCases.SaveMemory_Should_Not_CreateOneSidedSupersedeGraph(Driver);

    [Fact]
    public Task CancellationBeforeFirstWrite_Should_ProduceZeroMutation()
        => AgentMemoryStoreContractCases.CancellationBeforeFirstWrite_Should_ProduceZeroMutation(Driver);
}
