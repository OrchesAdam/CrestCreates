namespace CrestCreates.Agent.Memory.Persistence.Testing.Manifest;

/// <summary>
/// One Spec §18 skeleton name together with the Slice that first activates it.
/// Entries are immutable evidence requirements, not progress flags.
/// </summary>
public sealed record DurableAgentMemorySpecSkeletonEntry(string Name, int OwningSlice);

/// <summary>
/// Frozen Spec §18 test skeleton: 31 shared method names, 6 PostgreSQL
/// group/class names, 7 PostgreSQL method names, and the 44-name union.
/// The owning Slice records when a concrete runner must first expose the name.
/// </summary>
public static class DurableAgentMemorySpecTestSkeleton
{
    public const int SharedMethodCount = 31;
    public const int PostgreSqlGroupCount = 6;
    public const int PostgreSqlMethodCount = 7;
    public const int TotalNameCount = SharedMethodCount + PostgreSqlGroupCount + PostgreSqlMethodCount;

    public static IReadOnlyList<DurableAgentMemorySpecSkeletonEntry> SharedRequiredMethodNames { get; } =
    [
        new("Conversation_Should_Preserve_TenantIsolation", 2),
        new("Conversation_Should_Return_Snapshot", 2),
        new("Conversation_Should_Persist_Only_Sanitized_Turns", 2),
        new("Conversation_Should_Preserve_TurnSequence", 2),
        new("Task_Should_Preserve_TenantIsolation", 2),
        new("Task_Should_Return_Snapshot", 2),
        new("Task_Should_Persist_Only_Sanitized_Content", 2),
        new("Task_Should_Preserve_Deterministic_Order", 2),
        new("Concurrent_TaskAppend_Should_Not_Lose_Event", 2),
        new("TaskAppend_MissingTask_Should_Return_ResourceUnavailable", 2),
        new("CompressedContext_Should_Return_Snapshot", 2),
        new("CompressedContext_Should_Reject_CrossTenant_Block", 2),
        new("BlockIdentity_Should_Be_TenantWide_Unique", 2),
        new("ReplacingContext_Should_Remove_Old_BlockProjection", 2),
        new("Candidate_Should_Return_Snapshot", 2),
        new("Memory_Should_Return_Snapshot", 2),
        new("SaveMemory_Should_Be_CreateOrExactReplay", 2),
        new("SaveMemory_InvalidInitialLifecycleOrAuthority_Should_BeRejected", 2),
        new("ListMemories_Should_Be_Ordinally_Deterministic", 2),
        new("ListStores_NonBmpIdentifiers_Should_Match_StringComparerOrdinal", 2),
        new("Memory_Query_Should_Match_InMemory_Contract", 2),
        new("Promote_Should_Be_Atomic", 2),
        new("Promote_With_StaleCandidateHash_Should_Conflict", 2),
        new("ConcurrentPromote_Should_Have_ExactlyOneWinner", 2),
        new("Reject_Should_Be_Conditional", 2),
        new("Supersede_Should_Commit_ThreePartGraph_Atomically", 2),
        new("Supersede_Failure_Should_Expose_No_PartialGraph", 2),
        new("Archive_Should_Be_Conditional", 2),
        new("ConcurrentArchive_Should_Have_ExactlyOneWinner", 2),
        new("CurationCapabilities_Should_Be_ConfirmedAtomic", 2),
        new("PromotionPreparation_AndStoreMutation_Should_UseSameCurationProjection", 2)
    ];

    public static IReadOnlyList<DurableAgentMemorySpecSkeletonEntry> PostgreSqlRequiredGroupNames { get; } =
    [
        new("PostgreSqlAgentMemoryRestartTests", 4),
        new("PostgreSqlAgentMemoryConcurrencyTests", 4),
        new("PostgreSqlAgentMemoryCrashTests", 9),
        new("PostgreSqlAgentMemoryFailureTaxonomyTests", 4),
        new("PostgreSqlAgentMemoryMigrationTests", 3),
        new("PostgreSqlAgentMemoryCompositionTests", 3)
    ];

    public static IReadOnlyList<DurableAgentMemorySpecSkeletonEntry> PostgreSqlRequiredMethodNames { get; } =
    [
        new("CommittedAccountability_Should_Never_Precede_DurableCommit", 7),
        new("ContextCreate_WithBlocks_Should_SatisfyImmediateForeignKey", 5),
        new("FormalCuration_WithPreexistingAmbientTransaction_Should_FailBeforeMutation", 7),
        new("PostgreSqlProvider_WithoutAgentMemoryRuntime_Should_ValidateAndBuild", 3),
        new("PostgreSqlAgentMemoryPersistence_Should_ReplaceStores_InEitherOrder", 3),
        new("TamperedBlockContextOrOrdinal_Should_FailPersistedInvariantValidation", 5),
        new("V010Manifest_Should_ValidateCollationAndForeignKeyDeleteAction", 3)
    ];

    /// <summary>Exact union of the three arrays, preserving order.</summary>
    public static IReadOnlyList<string> SpecRequiredTestNames { get; } =
        SharedRequiredMethodNames
            .Select(entry => entry.Name)
            .Concat(PostgreSqlRequiredGroupNames.Select(entry => entry.Name))
            .Concat(PostgreSqlRequiredMethodNames.Select(entry => entry.Name))
            .ToArray();

    public static IReadOnlyDictionary<string, int> OwningSliceByName { get; } =
        SharedRequiredMethodNames
            .Concat(PostgreSqlRequiredGroupNames)
            .Concat(PostgreSqlRequiredMethodNames)
            .ToDictionary(entry => entry.Name, entry => entry.OwningSlice, StringComparer.Ordinal);
}
