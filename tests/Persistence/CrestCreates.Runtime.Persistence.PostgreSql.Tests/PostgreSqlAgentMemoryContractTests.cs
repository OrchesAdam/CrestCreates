using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Persistence.Testing;
using CrestCreates.Agent.Memory.Persistence.Testing.Cases;
using CrestCreates.Agent.Memory.Persistence.Testing.Drivers;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using FluentAssertions;
using CrestCreates.Runtime.Persistence.PostgreSql;
using Npgsql;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

/// <summary>
/// PostgreSQL runner for the shared Agent Memory Store contract cases plus the
/// PGS evidence-named methods. Restart/concurrency/failure evidence lives in
/// their dedicated runners.
/// </summary>
[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlAgentMemoryContractTests : IAsyncLifetime
{
    private readonly PostgreSqlRuntimeCollectionFixture _fixture;
    private PostgreSqlRuntimeSchemaLease _lease = null!;
    private PostgreSqlAgentMemoryContractDriver _driver = null!;

    public PostgreSqlAgentMemoryContractTests(PostgreSqlRuntimeCollectionFixture fixture)
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

    // ── Spec §17 evidence-named methods (PGS) ────────────────────────────────

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
    public async Task SaveMemory_ExactReplay_Should_NotMutateRevisionOrState()
    {
        var memory = Memory("tenant-a", "memory-exact-replay");
        await Driver.MemoryStore.SaveMemoryAsync(memory);
        var before = await ReadMemoryRowAsync("tenant-a", "memory-exact-replay");
        var beforeRevision = await _driver.ReadRawRevisionAsync(
            AgentMemoryArtifactKind.Memory, "tenant-a", "memory-exact-replay");

        await Driver.MemoryStore.SaveMemoryAsync(memory);

        var after = await ReadMemoryRowAsync("tenant-a", "memory-exact-replay");
        var afterRevision = await _driver.ReadRawRevisionAsync(
            AgentMemoryArtifactKind.Memory, "tenant-a", "memory-exact-replay");
        afterRevision.Revision.Should().Be(beforeRevision.Revision, "exact replay must not increment PostgreSQL revision.");
        after.StateJson.Should().Be(before.StateJson, "exact replay must not rewrite state_json.");
        after.StateHash.Should().Be(before.StateHash, "exact replay must not rewrite state_hash.");
    }

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

    [Fact]
    public async Task ContextCreate_WithBlocks_Should_SatisfyImmediateForeignKey()
    {
        var context = CompressedContext(
            "tenant-a",
            "context-fk",
            ContextBlock("tenant-a", "block-fk-1", "first", 0),
            ContextBlock("tenant-a", "block-fk-2", "second", 1));
        await Driver.ContextStore.CreateCompressedContextAsync(context);

        var read = await Driver.ContextStore.GetCompressedContextAsync("tenant-a", "context-fk");
        read.Should().NotBeNull();
        read!.Blocks.Should().HaveCount(2);
        var block = await Driver.ContextStore.GetCompressedContextBlockAsync("tenant-a", "block-fk-2");
        block.Should().NotBeNull();
        block!.Content.Should().Be("second");
    }

    private static AgentCompressedContextBlock ContextBlock(string tenantId, string blockId, string content, int ordinal)
        => new()
        {
            BlockId = blockId,
            TenantId = tenantId,
            Content = content,
            CanonicalContentHash = CanonicalHashStub.For($"block-{blockId}"),
            SourceRefs = [new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.ConversationTurn,
                TenantId = tenantId,
                SourceId = $"source-{ordinal}"
            }]
        };

    private static AgentCompressedContext CompressedContext(string tenantId, string contextId, params AgentCompressedContextBlock[] blocks)
        => new() { TenantId = tenantId, ContextId = contextId, Blocks = blocks };

    private async Task<(long Revision, string StateJson, string StateHash)> ReadMemoryRowAsync(string tenantId, string memoryId)
    {
        await using var connection = new NpgsqlConnection(_lease.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"select revision, state_json::text, state_hash from \"{_lease.Options.Schema}\".agent_memories where tenant_id=@tenant and memory_id=@memory;",
            connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("memory", memoryId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue("the memory row must exist for raw evidence.");
        return (reader.GetInt64(0), reader.GetString(1), reader.GetString(2));
    }

    private static AgentMemoryItem Memory(string tenantId, string memoryId)
        => new()
        {
            TenantId = tenantId,
            MemoryId = memoryId,
            Kind = AgentMemoryKind.Preference,
            Content = "exact replay content",
            CanonicalContentHash = CanonicalHashStub.For("exact-replay"),
            Confidence = AgentMemoryConfidence.Medium,
            PromotedAt = DateTimeOffset.UnixEpoch
        };
}
