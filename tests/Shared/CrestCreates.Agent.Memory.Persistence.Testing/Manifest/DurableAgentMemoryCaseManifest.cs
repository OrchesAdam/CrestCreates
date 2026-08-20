namespace CrestCreates.Agent.Memory.Persistence.Testing.Manifest;

public enum DurableAgentMemoryEvidenceKind
{
    InMemorySemantic = 0,
    PostgreSqlSemantic = 1,
    PostgreSqlConcurrency = 2,
    PostgreSqlRestart = 3,
    PostgreSqlFailureInjection = 4,
    CrashWorker = 5,
    PostgreSqlComposition = 6,
    AccountabilityComposition = 7,
    RecallExpansionParity = 8,
    Migration = 9,
    JsonArchitecture = 10,
    Boundary = 11,
    NativeAot = 12,
    CanonicalBuild = 13
}

/// <summary>One required evidence tuple: the concrete test that must exist and
/// pass, its evidence kind, and the Slice that owns its activation.</summary>
public sealed record DurableAgentMemoryEvidenceTuple(
    string CaseId,
    DurableAgentMemoryEvidenceKind Kind,
    int OwningSlice,
    string ExactFullyQualifiedTestName);

/// <summary>One approved Spec §17 acceptance case with its normative name.</summary>
public sealed record DurableAgentMemoryCase(
    string CaseId,
    string NormativeTestName,
    IReadOnlyList<DurableAgentMemoryEvidenceTuple> Evidence);

/// <summary>
/// Immutable evidence ledger for the approved #55 design: 59 Case IDs and 98
/// RequiredEvidence tuples (IMS 28, PGS 24, PGC 5, PGR 8, PGF 10, CW 2,
/// PGD 5, ACC 3, REP 5, MIG 2, JSON 1, BND 3, AOT 1, BLD 1). Tuples are
/// unique by (CaseId, EvidenceKind, ExactFullyQualifiedTestName).
/// </summary>
public static class DurableAgentMemoryCaseManifest
{
    public const int CaseCount = 59;
    public const int EvidenceTupleCount = 98;

    public static IReadOnlyList<DurableAgentMemoryCase> Cases { get; } = BuildCases();

    public static IReadOnlyDictionary<string, DurableAgentMemoryCase> ById { get; } =
        Cases.ToDictionary(caseItem => caseItem.CaseId, StringComparer.Ordinal);

    public static IReadOnlyList<DurableAgentMemoryEvidenceTuple> EvidenceTuples { get; } =
        Cases.SelectMany(caseItem => caseItem.Evidence).ToArray();

    public static IReadOnlyDictionary<DurableAgentMemoryEvidenceKind, int> EvidenceCountByKind { get; } =
        EvidenceTuples
            .GroupBy(tuple => tuple.Kind)
            .ToDictionary(group => group.Key, group => group.Count());

    private const string InMemoryStoreClass = "CrestCreates.Agent.Memory.Tests.Persistence.InMemoryAgentMemoryStoreContractTests";
    private const string InMemoryCurationClass = "CrestCreates.Agent.Memory.Tests.Persistence.InMemoryAgentMemoryCurationContractTests";
    private const string PgStoreClass = "CrestCreates.Runtime.Persistence.PostgreSql.Tests.PostgreSqlAgentMemoryContractTests";
    private const string PgCurationClass = "CrestCreates.Runtime.Persistence.PostgreSql.Tests.PostgreSqlAgentMemoryCurationContractTests";
    private const string PgConcurrencyClass = "CrestCreates.Runtime.Persistence.PostgreSql.Tests.PostgreSqlAgentMemoryConcurrencyTests";
    private const string PgRestartClass = "CrestCreates.Runtime.Persistence.PostgreSql.Tests.PostgreSqlAgentMemoryRestartTests";
    private const string PgFailureClass = "CrestCreates.Runtime.Persistence.PostgreSql.Tests.PostgreSqlAgentMemoryFailureTaxonomyTests";
    private const string PgCrashClass = "CrestCreates.Runtime.Persistence.PostgreSql.Tests.PostgreSqlAgentMemoryCrashTests";
    private const string PgCompositionClass = "CrestCreates.Runtime.Persistence.PostgreSql.Tests.PostgreSqlAgentMemoryCompositionTests";
    private const string PgAccountabilityClass = "CrestCreates.Runtime.Persistence.PostgreSql.Tests.PostgreSqlAgentMemoryAccountabilityCompositionTests";
    private const string PgRecallExpansionClass = "CrestCreates.Runtime.Persistence.PostgreSql.Tests.PostgreSqlAgentMemoryRecallExpansionTests";
    private const string PgMigrationClass = "CrestCreates.Runtime.Persistence.PostgreSql.Tests.PostgreSqlAgentMemoryMigrationTests";
    private const string BoundaryClass = "CrestCreates.DependencyBoundaries.Tests.DurableAgentMemoryPersistenceArchitectureTests";
    private const string AotFixtureClass = "CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests.PostgreSqlRuntimeAotFixtureTests";

    private const string AotMethodName = "DurableControlPlaneReferenceDataAotFixture_Should_PublishLinkAndRun";

    private static DurableAgentMemoryEvidenceTuple T(
        DurableAgentMemoryEvidenceKind kind,
        int slice,
        string className,
        string name)
        => new(string.Empty, kind, slice, $"{className}.{name}");

    private static DurableAgentMemoryEvidenceTuple Ims(int slice, bool curation, string name)
        => T(DurableAgentMemoryEvidenceKind.InMemorySemantic, slice, curation ? InMemoryCurationClass : InMemoryStoreClass, name);

    private static DurableAgentMemoryEvidenceTuple Pgs(int slice, bool curation, string name)
        => T(DurableAgentMemoryEvidenceKind.PostgreSqlSemantic, slice, curation ? PgCurationClass : PgStoreClass, name);

    private static DurableAgentMemoryEvidenceTuple Pgc(int slice, string name)
        => T(DurableAgentMemoryEvidenceKind.PostgreSqlConcurrency, slice, PgConcurrencyClass, name);

    private static DurableAgentMemoryEvidenceTuple Pgr(int slice, string name)
        => T(DurableAgentMemoryEvidenceKind.PostgreSqlRestart, slice, PgRestartClass, name);

    private static DurableAgentMemoryEvidenceTuple Pgf(int slice, string name)
        => T(DurableAgentMemoryEvidenceKind.PostgreSqlFailureInjection, slice, PgFailureClass, name);

    private static DurableAgentMemoryEvidenceTuple Cw(int slice, string name)
        => T(DurableAgentMemoryEvidenceKind.CrashWorker, slice, PgCrashClass, name);

    private static DurableAgentMemoryEvidenceTuple Pgd(int slice, string name)
        => T(DurableAgentMemoryEvidenceKind.PostgreSqlComposition, slice, PgCompositionClass, name);

    private static DurableAgentMemoryEvidenceTuple Acc(int slice, string name)
        => T(DurableAgentMemoryEvidenceKind.AccountabilityComposition, slice, PgAccountabilityClass, name);

    private static DurableAgentMemoryEvidenceTuple Rep(int slice, string name)
        => T(DurableAgentMemoryEvidenceKind.RecallExpansionParity, slice, PgRecallExpansionClass, name);

    private static DurableAgentMemoryEvidenceTuple Mig(int slice, string name)
        => T(DurableAgentMemoryEvidenceKind.Migration, slice, PgMigrationClass, name);

    private static DurableAgentMemoryEvidenceTuple Json(int slice, string name)
        => T(DurableAgentMemoryEvidenceKind.JsonArchitecture, slice, PgMigrationClass, name);

    private static DurableAgentMemoryEvidenceTuple Bnd(int slice, string name)
        => T(DurableAgentMemoryEvidenceKind.Boundary, slice, BoundaryClass, name);

    private static DurableAgentMemoryEvidenceTuple Bld(int slice, string name)
        => T(DurableAgentMemoryEvidenceKind.CanonicalBuild, slice, BoundaryClass, name);

    private static DurableAgentMemoryEvidenceTuple Aot(string name)
        => T(DurableAgentMemoryEvidenceKind.NativeAot, 11, AotFixtureClass, name);

    private static DurableAgentMemoryCase Case(string caseId, string name, params DurableAgentMemoryEvidenceTuple[] evidence)
        => new(caseId, name, evidence.Select(item => item with { CaseId = caseId }).ToArray());

    private static IReadOnlyList<DurableAgentMemoryCase> BuildCases() => new List<DurableAgentMemoryCase>
    {
        // ── §17.1 Happy path ──
        Case("H01", "Conversation_SaveAndRestart_Should_PreserveSanitizedSnapshotAndTurnSequence",
            Ims(2, curation: false, "Conversation_SaveAndRestart_Should_PreserveSanitizedSnapshotAndTurnSequence"),
            Pgs(4, curation: false, "Conversation_SaveAndRestart_Should_PreserveSanitizedSnapshotAndTurnSequence"),
            Pgr(4, "Conversation_SaveAndRestart_Should_PreserveSanitizedSnapshotAndTurnSequence")),
        Case("H02", "Task_SaveAppendAndRestart_Should_PreserveSanitizedSnapshotAndEventSequence",
            Ims(2, curation: false, "Task_SaveAppendAndRestart_Should_PreserveSanitizedSnapshotAndEventSequence"),
            Pgs(4, curation: false, "Task_SaveAppendAndRestart_Should_PreserveSanitizedSnapshotAndEventSequence"),
            Pgr(4, "Task_SaveAppendAndRestart_Should_PreserveSanitizedSnapshotAndEventSequence")),
        Case("H03", "ContextCreate_WithBlocks_Should_SatisfyImmediateForeignKey_AndRestartLookup",
            Ims(2, curation: false, "ContextCreate_WithBlocks_Should_SatisfyImmediateForeignKey_AndRestartLookup"),
            Pgs(5, curation: false, "ContextCreate_WithBlocks_Should_SatisfyImmediateForeignKey_AndRestartLookup"),
            Pgr(5, "ContextCreate_WithBlocks_Should_SatisfyImmediateForeignKey_AndRestartLookup")),
        Case("H04", "Promote_Should_CommitCandidateAndMemoryAtomically",
            Ims(2, curation: true, "Promote_Should_CommitCandidateAndMemoryAtomically"),
            Pgs(7, curation: true, "Promote_Should_CommitCandidateAndMemoryAtomically")),
        Case("H05", "Supersede_Should_CommitReciprocalThreeNodeGraphAtomically",
            Ims(2, curation: true, "Supersede_Should_CommitReciprocalThreeNodeGraphAtomically"),
            Pgs(8, curation: true, "Supersede_Should_CommitReciprocalThreeNodeGraphAtomically")),
        Case("H06", "Archive_Should_RetainGraphLinks_AfterRestart",
            Ims(2, curation: true, "Archive_Should_RetainGraphLinks_AfterRestart"),
            Pgs(8, curation: true, "Archive_Should_RetainGraphLinks_AfterRestart"),
            Pgr(8, "Archive_Should_RetainGraphLinks_AfterRestart")),
        Case("H07", "Recall_Should_ReturnSameOrderPackAndHashes_AfterRestart",
            Rep(10, "Recall_Should_ReturnSameOrderPackAndHashes_AfterRestart"),
            Pgr(10, "Recall_Should_ReturnSameOrderPackAndHashes_AfterRestart")),
        Case("H08", "SourceExpansion_Should_ReturnSameDomainMaterial_AfterRestart",
            Rep(10, "SourceExpansion_Should_ReturnSameDomainMaterial_AfterRestart"),
            Pgr(10, "SourceExpansion_Should_ReturnSameDomainMaterial_AfterRestart")),
        Case("H09", "SaveMemory_ExactReplay_Should_NotMutateRevisionOrState",
            Ims(2, curation: false, "SaveMemory_ExactReplay_Should_NotMutateRevisionOrState"),
            Pgs(6, curation: false, "SaveMemory_ExactReplay_Should_NotMutateRevisionOrState")),

        // ── §17.2 Boundary ──
        Case("B01", "AllStores_Should_IsolateSameIdentityAcrossTenants",
            Ims(2, curation: false, "AllStores_Should_IsolateSameIdentityAcrossTenants"),
            Pgs(6, curation: false, "AllStores_Should_IsolateSameIdentityAcrossTenants")),
        Case("B02", "AllCrossTenantLookups_Should_ReturnNullOrEmptyWithoutLeakage",
            Ims(2, curation: false, "AllCrossTenantLookups_Should_ReturnNullOrEmptyWithoutLeakage"),
            Pgs(6, curation: false, "AllCrossTenantLookups_Should_ReturnNullOrEmptyWithoutLeakage")),
        Case("B03", "BlockIdentity_Should_BeIndependentAcrossTenants",
            Ims(2, curation: false, "BlockIdentity_Should_BeIndependentAcrossTenants"),
            Pgs(5, curation: false, "BlockIdentity_Should_BeIndependentAcrossTenants")),
        Case("B04", "BlockIdentity_Should_BeTenantWideUniqueAcrossContexts",
            Ims(2, curation: false, "BlockIdentity_Should_BeTenantWideUniqueAcrossContexts"),
            Pgs(5, curation: false, "BlockIdentity_Should_BeTenantWideUniqueAcrossContexts")),
        Case("B05", "ReplacingContext_Should_RemoveOldBlockProjectionAtomically",
            Ims(2, curation: false, "ReplacingContext_Should_RemoveOldBlockProjectionAtomically"),
            Pgs(5, curation: false, "ReplacingContext_Should_RemoveOldBlockProjectionAtomically")),
        Case("B06", "OrderedArtifacts_Should_PreserveSubmittedSequence_NotTimestampOrIdOrder",
            Ims(2, curation: false, "OrderedArtifacts_Should_PreserveSubmittedSequence_NotTimestampOrIdOrder"),
            Pgs(5, curation: false, "OrderedArtifacts_Should_PreserveSubmittedSequence_NotTimestampOrIdOrder")),
        Case("B07", "Concurrent_TaskAppend_Should_Not_Lose_Event",
            Ims(2, curation: false, "Concurrent_TaskAppend_Should_Not_Lose_Event"),
            Pgc(4, "Concurrent_TaskAppend_Should_Not_Lose_Event")),
        Case("B08", "ConcurrentTaskAppend_CommittedOrder_Should_SurviveRestart",
            Pgc(4, "ConcurrentTaskAppend_CommittedOrder_Should_SurviveRestart"),
            Pgr(4, "ConcurrentTaskAppend_CommittedOrder_Should_SurviveRestart")),
        Case("B09", "ListStores_NonBmpIdentifiers_Should_Match_StringComparerOrdinal",
            Ims(2, curation: false, "ListStores_NonBmpIdentifiers_Should_Match_StringComparerOrdinal"),
            Pgs(6, curation: false, "ListStores_NonBmpIdentifiers_Should_Match_StringComparerOrdinal")),
        Case("B10", "IncludeStale_Should_RemainNoOp_WithoutStaleSchema",
            Ims(2, curation: false, "IncludeStale_Should_RemainNoOp_WithoutStaleSchema"),
            Pgs(6, curation: false, "IncludeStale_Should_RemainNoOp_WithoutStaleSchema"),
            Mig(3, "IncludeStale_Should_RemainNoOp_WithoutStaleSchema")),
        Case("B11", "Memory_Query_Should_Match_InMemory_Contract",
            Rep(10, "Memory_Query_Should_Match_InMemory_Contract")),
        Case("B12", "AllStores_Should_ReturnDetachedSnapshots",
            Ims(2, curation: false, "AllStores_Should_ReturnDetachedSnapshots"),
            Pgs(6, curation: false, "AllStores_Should_ReturnDetachedSnapshots")),
        Case("B13", "CandidateBatch_WithOneConflict_Should_WriteNone",
            Ims(2, curation: false, "CandidateBatch_WithOneConflict_Should_WriteNone"),
            Pgc(9, "CandidateBatch_WithOneConflict_Should_WriteNone")),
        Case("B14", "SaveMemory_ExistingOneFieldDifference_Should_ReturnStateConflict",
            Ims(2, curation: false, "SaveMemory_ExistingOneFieldDifference_Should_ReturnStateConflict"),
            Pgs(6, curation: false, "SaveMemory_ExistingOneFieldDifference_Should_ReturnStateConflict")),
        Case("B15", "PostgreSqlAgentMemoryPersistence_Should_ReplaceStores_InEitherOrder",
            Pgd(3, "PostgreSqlAgentMemoryPersistence_Should_ReplaceStores_InEitherOrder")),
        Case("B16", "SaveMemory_Should_Not_CreateOneSidedSupersedeGraph",
            Ims(2, curation: false, "SaveMemory_Should_Not_CreateOneSidedSupersedeGraph"),
            Pgs(6, curation: false, "SaveMemory_Should_Not_CreateOneSidedSupersedeGraph")),
        Case("B17", "TaskAppend_MissingTask_Should_Return_ResourceUnavailable",
            Ims(2, curation: false, "TaskAppend_MissingTask_Should_Return_ResourceUnavailable"),
            Pgs(4, curation: false, "TaskAppend_MissingTask_Should_Return_ResourceUnavailable")),
        Case("B18", "SaveMemory_InvalidInitialLifecycleOrAuthority_Should_BeRejected",
            Ims(2, curation: false, "SaveMemory_InvalidInitialLifecycleOrAuthority_Should_BeRejected"),
            Pgs(6, curation: false, "SaveMemory_InvalidInitialLifecycleOrAuthority_Should_BeRejected")),

        // ── §17.3 Failure and concurrency ──
        Case("F01", "Promote_OccupiedMemoryIdentity_Should_LeaveCandidateUnchanged",
            Ims(2, curation: true, "Promote_OccupiedMemoryIdentity_Should_LeaveCandidateUnchanged"),
            Pgs(7, curation: true, "Promote_OccupiedMemoryIdentity_Should_LeaveCandidateUnchanged")),
        Case("F02", "Promote_StaleCandidateHash_Should_ConflictWithoutMutation",
            Ims(2, curation: true, "Promote_StaleCandidateHash_Should_ConflictWithoutMutation"),
            Pgs(7, curation: true, "Promote_StaleCandidateHash_Should_ConflictWithoutMutation")),
        Case("F03", "ConcurrentPromote_Should_HaveExactlyOneWinner",
            Pgc(9, "ConcurrentPromote_Should_HaveExactlyOneWinner")),
        Case("F04", "Reject_StaleExpectation_Should_HaveZeroMutation",
            Ims(2, curation: true, "Reject_StaleExpectation_Should_HaveZeroMutation"),
            Pgs(7, curation: true, "Reject_StaleExpectation_Should_HaveZeroMutation")),
        Case("F05", "Supersede_FailureAfterEachWritePoint_Should_ExposeNoPartialGraph",
            Pgf(9, "Supersede_FailureAfterEachWritePoint_Should_ExposeNoPartialGraph")),
        Case("F06", "ConcurrentSupersedeOrArchive_Should_HaveOneValidWinner",
            Pgc(9, "ConcurrentSupersedeOrArchive_Should_HaveOneValidWinner")),
        Case("F07", "CrashBeforeCurationCommit_Should_ExposeNoMutationAfterBackendExit",
            Cw(9, "CrashBeforeCurationCommit_Should_ExposeNoMutationAfterBackendExit")),
        Case("F08", "CrashAfterCurationCommit_Should_RemainVisibleToFreshProcess",
            Cw(9, "CrashAfterCurationCommit_Should_RemainVisibleToFreshProcess")),
        Case("F09", "DatabaseUnavailable_Should_RemainRuntimePersistenceUnavailable",
            Pgf(9, "DatabaseUnavailable_Should_RemainRuntimePersistenceUnavailable")),
        Case("F10", "CommitAcknowledgementLoss_Should_RemainCommitUnknown",
            Pgf(9, "CommitAcknowledgementLoss_Should_RemainCommitUnknown")),
        Case("F11", "MalformedPersistedState_Should_FailPersistedInvariantValidation",
            Pgf(9, "MalformedPersistedState_Should_FailPersistedInvariantValidation")),
        Case("F12", "RejectedRawContent_Should_BeAbsentFromDatabaseParametersAndRows",
            Pgf(4, "RejectedRawContent_Should_BeAbsentFromDatabaseParametersAndRows")),
        Case("F13", "ContextBlockConflict_Should_RestoreOldAggregateAndProjection",
            Pgf(5, "ContextBlockConflict_Should_RestoreOldAggregateAndProjection")),
        Case("F14", "CancellationBeforeFirstWrite_Should_ProduceZeroMutation",
            Ims(2, curation: false, "CancellationBeforeFirstWrite_Should_ProduceZeroMutation"),
            Pgf(9, "CancellationBeforeFirstWrite_Should_ProduceZeroMutation")),
        Case("F15", "FormalCuration_WithPreexistingAmbientTransaction_Should_FailBeforeMutation",
            Pgf(7, "FormalCuration_WithPreexistingAmbientTransaction_Should_FailBeforeMutation")),
        Case("F16", "TamperedBlockContextOrOrdinal_Should_FailPersistedInvariantValidation",
            Pgf(5, "TamperedBlockContextOrOrdinal_Should_FailPersistedInvariantValidation")),

        // ── §17.4 Composition ──
        Case("C01", "SelectedMemoryStore_Should_ImplementConditionalAndCapabilitiesWithoutSeparateDescriptors",
            Pgd(8, "SelectedMemoryStore_Should_ImplementConditionalAndCapabilitiesWithoutSeparateDescriptors")),
        Case("C02", "CurationCompositionValidator_Should_PassAndReportConfirmedAtomic",
            Ims(2, curation: true, "CurationCompositionValidator_Should_PassAndReportConfirmedAtomic"),
            Pgd(8, "CurationCompositionValidator_Should_PassAndReportConfirmedAtomic")),
        Case("C03", "PostgreSqlProvider_Should_ReferenceOnlyAgentMemoryAbstractions",
            Bnd(3, "PostgreSqlProvider_Should_ReferenceOnlyAgentMemoryAbstractions")),
        Case("C04", "PostgreSqlAgentMemoryStores_Should_HaveNoAccountabilityDependency",
            Bnd(10, "PostgreSqlAgentMemoryStores_Should_HaveNoAccountabilityDependency")),
        Case("C05", "Retriever_Should_HaveInMemoryPostgreSqlParity",
            Rep(10, "Retriever_Should_HaveInMemoryPostgreSqlParity")),
        Case("C06", "SourceExpanderAndReadCore_Should_RemainUnchangedAfterRestart",
            Rep(10, "SourceExpanderAndReadCore_Should_RemainUnchangedAfterRestart"),
            Pgr(10, "SourceExpanderAndReadCore_Should_RemainUnchangedAfterRestart")),
        Case("C07", "KnownCommitAndTypedConflictFacts_Should_RemainCorrectWithDurableStore",
            Acc(10, "KnownCommitAndTypedConflictFacts_Should_RemainCorrectWithDurableStore")),
        Case("C08", "UnavailableOrCommitUnknown_Should_CreateNoFalseDeterministicCurationFact",
            Acc(10, "UnavailableOrCommitUnknown_Should_CreateNoFalseDeterministicCurationFact"),
            Pgf(9, "UnavailableOrCommitUnknown_Should_CreateNoFalseDeterministicCurationFact")),
        Case("C09", "V010Manifest_Should_ValidateApplyChecksumShapeCollationAndForeignKeyDeleteAction",
            Mig(3, "V010Manifest_Should_ValidateApplyChecksumShapeCollationAndForeignKeyDeleteAction")),
        Case("C10", "PostgreSqlAgentMemoryJsonPaths_Should_UseExactGeneratedRootsOnly",
            Json(3, "PostgreSqlAgentMemoryJsonPaths_Should_UseExactGeneratedRootsOnly")),
        Case("C11", "DurableAgentMemoryDependencyBoundariesAndCanonicalSolutions_Should_Build",
            Bnd(11, "DurableAgentMemoryDependencyBoundariesAndCanonicalSolutions_Should_Build"),
            Bld(11, "DurableAgentMemoryDependencyBoundariesAndCanonicalSolutions_Should_Build")),
        Case("C12", "PublishNativeAotPostgreSqlRuntime_Should_ExecuteDurableAgentMemoryMainline",
            Aot(AotMethodName)),
        Case("C13", "CommittedAccountability_Should_Never_Precede_DurableCommit",
            Acc(7, "CommittedAccountability_Should_Never_Precede_DurableCommit")),
        Case("C14", "PostgreSqlProvider_WithoutAgentMemoryRuntime_Should_ValidateAndBuild",
            Pgd(3, "PostgreSqlProvider_WithoutAgentMemoryRuntime_Should_ValidateAndBuild")),
        Case("C15", "ExplicitAgentMemoryProviderRegistration_Should_ReplaceFourStores_InEitherOrder",
            Pgd(3, "ExplicitAgentMemoryProviderRegistration_Should_ReplaceFourStores_InEitherOrder")),
        Case("C16", "PromotionPreparation_AndStoreMutation_Should_UseSameCurationProjection",
            Ims(2, curation: true, "PromotionPreparation_AndStoreMutation_Should_UseSameCurationProjection"),
            Pgs(7, curation: true, "PromotionPreparation_AndStoreMutation_Should_UseSameCurationProjection"))
    };
}
