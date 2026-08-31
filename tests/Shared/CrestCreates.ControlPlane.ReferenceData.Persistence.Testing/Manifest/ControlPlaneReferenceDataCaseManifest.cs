using System.Collections.Immutable;

namespace CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;

public static class ControlPlaneReferenceDataCaseManifest
{
    private static readonly ImmutableArray<RequiredRunner> SharedRunners =
        ImmutableArray.Create(RequiredRunner.InMemory, RequiredRunner.PostgreSql);

    public static IReadOnlyList<CaseManifestEntry> AllCases { get; } = BuildAllCases();

    public static IReadOnlyDictionary<(string CaseId, string Variant), ImmutableArray<EvidenceVectorKey>> EvidenceVectorExpansion { get; }
        = BuildEvidenceVectorExpansion();

    /// <summary>
    /// Cases whose acceptance evidence is required on more than one runner.
    /// These are the shared contract cases from the frozen specification. The
    /// manifest is an acceptance oracle, not a summary of whichever provider
    /// tests happen to exist today.
    /// NOTE: must be declared before <see cref="EvidenceTuples"/>, which reads it.
    /// </summary>
    public static IReadOnlyDictionary<string, ImmutableArray<RequiredRunner>> RunnerExpansion { get; } =
        new Dictionary<string, ImmutableArray<RequiredRunner>>
        {
            [CaseId.D01] = SharedRunners,
            [CaseId.D02] = SharedRunners,
            [CaseId.D03] = SharedRunners,
            [CaseId.D04] = SharedRunners,
            [CaseId.D05] = SharedRunners,
            [CaseId.D06] = SharedRunners,
            [CaseId.D07] = SharedRunners,
            [CaseId.D08] = SharedRunners,
            [CaseId.D11] = SharedRunners,
            [CaseId.D12] = SharedRunners,
            [CaseId.D13] = SharedRunners,
            [CaseId.O01] = SharedRunners,
            [CaseId.O02] = SharedRunners,
            [CaseId.O03] = SharedRunners,
            [CaseId.O04] = SharedRunners,
            [CaseId.O05] = SharedRunners,
            [CaseId.O06] = SharedRunners,
            [CaseId.O07] = SharedRunners,
            [CaseId.O08] = SharedRunners,
            [CaseId.O09] = SharedRunners,
            [CaseId.O10] = SharedRunners,
            [CaseId.O11] = SharedRunners,
            [CaseId.O12] = SharedRunners,
            [CaseId.O13] = SharedRunners,
            [CaseId.O14] = SharedRunners,
            [CaseId.O19] = SharedRunners,
            [CaseId.O20] = SharedRunners,
            [CaseId.O21] = SharedRunners,
            [CaseId.O22] = SharedRunners,
            [CaseId.P01] = SharedRunners,
            [CaseId.P02] = SharedRunners,
            [CaseId.P03] = SharedRunners,
            [CaseId.P04] = SharedRunners,
            [CaseId.P05] = SharedRunners,
            [CaseId.P06] = SharedRunners,
            [CaseId.P07] = SharedRunners,
            [CaseId.P10] = SharedRunners,
            [CaseId.P11] = SharedRunners,
            [CaseId.P12] = SharedRunners,
            [CaseId.V01] = SharedRunners,
            [CaseId.V02] = SharedRunners,
            [CaseId.V03] = SharedRunners,
            [CaseId.V04] = SharedRunners,
            [CaseId.V05] = SharedRunners,
            [CaseId.F01] = SharedRunners,
            [CaseId.F02] = SharedRunners,
            [CaseId.OVG01] = SharedRunners,
            [CaseId.OVG02] = SharedRunners,
            [CaseId.OVG03] = SharedRunners,
            [CaseId.OVG04] = SharedRunners,
            [CaseId.OVG05] = SharedRunners,
            [CaseId.OVG07] = SharedRunners,
            [CaseId.OVG08] = SharedRunners,
            [CaseId.OVG12] = SharedRunners,
        };

    public static IReadOnlyList<EvidenceTuple> EvidenceTuples { get; } = BuildEvidenceTuples();

    public static IReadOnlySet<string> RequiredTupleKeys { get; } = BuildRequiredTupleKeys();

    public static IEnumerable<EvidenceTuple> EvidenceTuplesFor(string caseId, RequiredRunner runner)
        => EvidenceTuples.Where(tuple => tuple.CaseId == caseId && tuple.Runner == runner);

    private static IReadOnlySet<string> BuildRequiredTupleKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tuple in EvidenceTuples)
            keys.Add(ControlPlaneReferenceDataEvidenceLedger.EvidenceTupleKey(tuple));
        return keys;
    }

    private static IReadOnlyList<CaseManifestEntry> BuildAllCases()
    {
        var entries = new List<CaseManifestEntry>();

        // ── Descriptor Draft ──
        entries.Add(new(CaseId.D01, "Draft", "DescriptorPayloadVariant", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice2,
            "DescriptorDraftPayloadVariant_Should_RoundTripCompleteSnapshot"));
        entries.Add(new(CaseId.D02, "Draft", "Draft", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice2,
            "DescriptorDraft_Save_Should_CaptureSnapshot"));
        entries.Add(new(CaseId.D03, "Draft", "Draft", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice2,
            "DescriptorDraft_Read_Should_ReturnDetachedSnapshot"));
        entries.Add(new(CaseId.D04, "Draft", "Draft", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice2,
            "DescriptorDraft_SameIdInTwoTenants_Should_NotCollide"));
        entries.Add(new(CaseId.D05, "Draft", "DraftQueryVariant", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice2,
            "DescriptorDraftQueryVariant_Should_PreserveSemantics"));
        entries.Add(new(CaseId.D06, "Draft", "Draft", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice2,
            "DescriptorDraft_List_Should_OrderByDraftIdOrdinal"));
        entries.Add(new(CaseId.D07, "Draft", "Draft", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice2,
            "DescriptorDraft_Save_Should_ReplaceCompleteSnapshot"));
        entries.Add(new(CaseId.D08, "Draft", "DraftValidatorOwnedInvalidVariant", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice2,
            "DraftValidatorOwnedInvalidVariant_Should_RemainDurableAndDiagnosable"));
        entries.Add(new(CaseId.D09, "Draft", "Draft", EvidenceVectorKey.Default, RequiredRunner.PostgreSql, OwningSlice.Slice9,
            "DescriptorDraft_Should_SurviveProviderRestart"));
        entries.Add(new(CaseId.D10, "Draft", "Draft", EvidenceVectorKey.Default, RequiredRunner.PostgreSql, OwningSlice.Slice10,
            "DescriptorDraft_Should_SurviveProcessRestart"));
        entries.Add(new(CaseId.D11, "Draft", "Draft", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice2,
            "DescriptorDraft_TimeFilter_Should_PreserveHundredNanosecondBoundaries"));
        entries.Add(new(CaseId.D12, "Draft", "Draft", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice2,
            "DescriptorDraft_TimeFilter_Should_CompareUtcTicksNotOffset"));
        entries.Add(new(CaseId.D13, "Draft", "Draft", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice2,
            "DescriptorDraft_CreatedAt_Should_PreserveOriginalOffsetAndTicks"));

        // ── Organization ──
        entries.Add(new(CaseId.O01, "Organization", "OrganizationIdentitySurface", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "OrganizationIdentitySurface_GlobalAndTenant_Should_NotCollide"));
        entries.Add(new(CaseId.O02, "Organization", "OrganizationIdentitySurface", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "OrganizationIdentitySurface_SameIdInTwoTenants_Should_NotCollide"));
        entries.Add(new(CaseId.O03, "Organization", "OrganizationQuerySurface", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "OrganizationQuerySurface_Should_PreserveExplicitTenantIsolation"));
        entries.Add(new(CaseId.O04, "Organization", "OrganizationQuerySurface", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "OrganizationQuerySurface_NullTenant_Should_RemainUnfiltered"));
        entries.Add(new(CaseId.O05, "Organization", "OrganizationUnit", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "OrganizationUnits_Should_OrderBySortOrderScopeThenId"));
        entries.Add(new(CaseId.O06, "Organization", "Position", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "Positions_Should_OrderByScopeThenId"));
        entries.Add(new(CaseId.O07, "Organization", "MembershipByUser", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "MembershipsByUser_Should_OrderByCreatedAtScopeThenId"));
        entries.Add(new(CaseId.O08, "Organization", "MembershipByUnit", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "MembershipsByUnit_Should_OrderByCreatedAtScopeThenId"));
        entries.Add(new(CaseId.O09, "Organization", "RoleAssignment", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "RoleAssignments_Should_OrderByCreatedAtScopeThenId"));
        entries.Add(new(CaseId.O10, "Organization", "Membership", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "PrimaryMembership_FullTie_Should_UseNormalizedScopeThenId"));
        entries.Add(new(CaseId.O11, "Organization", "IdentityService", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "OrganizationIdentity_Should_BeDeterministic"));
        entries.Add(new(CaseId.O12, "Organization", "HierarchyService", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "OrganizationHierarchy_Should_BeDeterministic"));
        entries.Add(new(CaseId.O13, "Organization", "OrganizationUnit", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "OrganizationUnit_MissingParent_Should_NotFailSave"));
        entries.Add(new(CaseId.O14, "Organization", "MissingReferenceVariant", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "OrganizationReferenceVariant_Should_NotFailSave"));
        entries.Add(new(CaseId.O15, "Organization", "OrganizationEntitySurface", EvidenceVectorKey.Default, RequiredRunner.Architecture, OwningSlice.Slice6,
            "OrganizationProvider_Should_NotIntroduceReferentialSemantics"));
        entries.Add(new(CaseId.O16, "Organization", "OrganizationEntitySurface", EvidenceVectorKey.Default, RequiredRunner.PostgreSql, OwningSlice.Slice10,
            "OrganizationEntitySurface_Should_SurviveProcessRestart"));
        entries.Add(new(CaseId.O17, "Organization", "HierarchyService", EvidenceVectorKey.Default, RequiredRunner.PostgreSql, OwningSlice.Slice10,
            "OrganizationHierarchy_Should_RemainStableAfterRestart"));
        entries.Add(new(CaseId.O18, "Organization", "IdentityService", EvidenceVectorKey.Default, RequiredRunner.PostgreSql, OwningSlice.Slice10,
            "OrganizationIdentity_Should_RemainStableAfterRestart"));
        entries.Add(new(CaseId.O19, "Organization", "ScopedKeyCollisionVariant", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "OrganizationScopedKey_Should_NotAliasDelimiterValues"));
        entries.Add(new(CaseId.O20, "Organization", "OrganizationEntitySurface", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "OrganizationEntitySurface_Save_Should_CaptureSnapshot"));
        entries.Add(new(CaseId.O21, "Organization", "OrganizationReadSurface", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "OrganizationReadSurface_Should_ReturnDetachedSnapshot"));
        entries.Add(new(CaseId.O22, "Organization", "OrganizationCreatedAtVariant", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "OrganizationCreatedAtVariant_Should_PreserveExactOrderAndSnapshot"));

        // ── Phase 9d: Organization generation authority ──
        entries.Add(new(CaseId.OVG01, "Authority", "InitialGeneration", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "OrganizationScopeGeneration_Should_StartAtZero"));
        entries.Add(new(CaseId.OVG02, "Authority", "OrganizationUnit", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "OrganizationWrite_Should_Atomically_AdvanceGeneration"));
        entries.Add(new(CaseId.OVG03, "Authority", "Position", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "OrganizationSaveSurface_Should_AdvanceSharedScopeGeneration"));
        entries.Add(new(CaseId.OVG04, "Authority", "Membership", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "OrganizationSaveSurface_Should_AdvanceSharedScopeGeneration"));
        entries.Add(new(CaseId.OVG05, "Authority", "RoleAssignment", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "OrganizationSaveSurface_Should_AdvanceSharedScopeGeneration"));
        entries.Add(new(CaseId.OVG07, "Authority", "TenantIsolation", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "TenantGeneration_Should_Not_Affect_OtherTenants"));
        entries.Add(new(CaseId.OVG08, "Authority", "RepeatedBlindSave", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "RepeatedBlindSave_Should_AdvanceGenerationAgain"));
        entries.Add(new(CaseId.OVG12, "Contract", "ScopeIdentity", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice3,
            "OrganizationScopeIdentity_Should_Reject_DefaultUnknownAndInvalidTenant"));

        // ── DataPermission rules ──
        entries.Add(new(CaseId.P01, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice4,
            "DataPermissionRule_Should_MatchTenantExact"));
        entries.Add(new(CaseId.P02, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice4,
            "DataPermissionRule_Should_MatchTenantWildcardPermission"));
        entries.Add(new(CaseId.P03, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice4,
            "DataPermissionRule_Should_MatchTenantWildcardAction"));
        entries.Add(new(CaseId.P04, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice4,
            "DataPermissionRule_Should_FallBackToGlobal"));
        entries.Add(new(CaseId.P05, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice4,
            "DataPermissionRule_TenantWildcard_Should_WinOverGlobalExact"));
        entries.Add(new(CaseId.P06, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice4,
            "DataPermissionRule_OtherTenant_Should_NotApply"));
        entries.Add(new(CaseId.P07, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice4,
            "DataPermissionRule_Save_Should_ReplaceExactRule"));
        entries.Add(new(CaseId.P08, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.Architecture, OwningSlice.Slice6,
            "DataPermissionScope_Should_RemainDerived"));
        entries.Add(new(CaseId.P09, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.PostgreSql, OwningSlice.Slice10,
            "DataPermissionRule_Should_SurviveProcessRestart"));
        entries.Add(new(CaseId.P10, "Rule", "RuleExactEmptyVariant", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice4,
            "DataPermissionRule_EmptyExact_Should_RemainDistinctFromWildcard"));
        entries.Add(new(CaseId.P11, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice4,
            "DataPermissionRule_WildcardActionExactPermission_Should_NotMatchNonNullAction"));
        entries.Add(new(CaseId.P12, "Rule", "Rule", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice4,
            "DataPermissionRule_WildcardActionExactPermission_Should_MatchNullActionRequest"));
        entries.Add(new(CaseId.P13, "Rule", "PersistedRuleCorruptionVariant", EvidenceVectorKey.Default, RequiredRunner.PostgreSql, OwningSlice.Slice9,
            "PersistedRuleCorruptionVariant_Should_FailClosed"));

        // ── Validation and cancellation ──
        entries.Add(new(CaseId.V01, "Validation", "IdentityValidationVector", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice2,
            "IdentityValidationVector_Should_FailBeforeMutation"));
        entries.Add(new(CaseId.V02, "Validation", "RuleSentinelField", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice4,
            "RuleSentinelField_Should_FailBeforeMutation"));
        entries.Add(new(CaseId.V03, "Validation", "PersistedEnumSurface", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice2,
            "PersistedEnumSurface_Should_FailBeforeMutation"));
        entries.Add(new(CaseId.V04, "Validation", "Draft", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice2,
            "UnsupportedDraftPayload_Should_FailBeforeMutation"));
        entries.Add(new(CaseId.V05, "Validation", "StoreMethodSurface", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice2,
            "PreCancelledStoreMethod_Should_ExitBeforeQueryOrMutation"));

        // ── Cross-store concurrency, crash, failure ──
        entries.Add(new(CaseId.F01, "Failure", "SaveSurface", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice7,
            "SaveSurface_ConcurrentBlindSave_Should_ExposeOneCompleteSnapshot"));
        entries.Add(new(CaseId.F02, "Failure", "SaveSurface", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice7,
            "SaveSurface_ConcurrentBlindSave_Should_NotInventStaleWriterConflict"));
        entries.Add(new(CaseId.F03, "Failure", "SaveSurface", EvidenceVectorKey.Default, RequiredRunner.PostgreSql, OwningSlice.Slice10,
            "SaveSurface_CrashBeforeCommit_Should_NotExposePartialSnapshot"));
        entries.Add(new(CaseId.F04, "Failure", "SaveSurface", EvidenceVectorKey.Default, RequiredRunner.PostgreSql, OwningSlice.Slice10,
            "SaveSurface_CrashAfterCommit_Should_ExposeCompleteSnapshot"));
        entries.Add(new(CaseId.F05, "Failure", "SaveSurface", EvidenceVectorKey.Default, RequiredRunner.PostgreSql, OwningSlice.Slice10,
            "SaveSurface_CommitUnknown_Should_NotBeReportedAsDeterministicFailure"));
        entries.Add(new(CaseId.F06, "Failure", "StoreMethodSurface", EvidenceVectorKey.Default, RequiredRunner.PostgreSql, OwningSlice.Slice7,
            "StoreMethodSurface_UnavailableProvider_Should_UseSharedFailureTaxonomy"));
        entries.Add(new(CaseId.F07, "Failure", "PersistedSnapshotCorruptionVariant", EvidenceVectorKey.Default, RequiredRunner.PostgreSql, OwningSlice.Slice10,
            "PersistedSnapshotCorruptionVariant_Should_FailClosed"));
        entries.Add(new(CaseId.F08, "Failure", "SaveSurface", EvidenceVectorKey.Default, RequiredRunner.PostgreSql, OwningSlice.Slice7,
            "SaveSurface_Should_RejectAmbientRuntimeTransactionBeforeMutation"));
        entries.Add(new(CaseId.F09, "Failure", "PersistedStructuredFieldVariant", EvidenceVectorKey.Default, RequiredRunner.PostgreSql, OwningSlice.Slice7,
            "PersistedStructuredFieldVariant_Mismatch_Should_FailClosed"));

        // ── Migration, composition, architecture, AOT ──
        entries.Add(new(CaseId.C01, "Composition", "Migration", EvidenceVectorKey.Default, RequiredRunner.PostgreSql, OwningSlice.Slice6,
            "ReapplyingMigration_Should_NotDriftSchema"));
        entries.Add(new(CaseId.C02, "Composition", "Migration", EvidenceVectorKey.Default, RequiredRunner.PostgreSql, OwningSlice.Slice6,
            "MigrationValidation_Should_DetectChecksumDrift"));
        entries.Add(new(CaseId.C03, "Composition", "Migration", EvidenceVectorKey.Default, RequiredRunner.PostgreSql, OwningSlice.Slice6,
            "MigrationValidation_Should_DetectSchemaDrift"));
        entries.Add(new(CaseId.C04, "Composition", "Kernel", EvidenceVectorKey.Default, RequiredRunner.Architecture, OwningSlice.Slice11,
            "Provider_Should_ReuseRuntimePersistenceKernel"));
        entries.Add(new(CaseId.C05, "Composition", "Kernel", EvidenceVectorKey.Default, RequiredRunner.Architecture, OwningSlice.Slice11,
            "Provider_Should_NotExpandRuntimeRecoveryTransactionBoundary"));
        entries.Add(new(CaseId.C06, "Composition", "Contracts", EvidenceVectorKey.Default, RequiredRunner.Architecture, OwningSlice.Slice11,
            "StoreContracts_Should_NotExposeProviderTypes"));
        entries.Add(new(CaseId.C07, "Composition", "OrganizationEntitySurface", EvidenceVectorKey.Default, RequiredRunner.Architecture, OwningSlice.Slice6,
            "OrganizationSchema_Should_NotContainCrossEntityForeignKeys"));
        entries.Add(new(CaseId.C08, "Composition", "Composition", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice6,
            "BaseProviderRegistration_Should_NotReplaceReferenceStores"));
        entries.Add(new(CaseId.C09, "Composition", "SaveSurface", EvidenceVectorKey.Default, RequiredRunner.PostgreSql, OwningSlice.Slice9,
            "OptInRegistration_Should_ReplaceExactlySelectedStores"));
        entries.Add(new(CaseId.C10, "Composition", "Draft", EvidenceVectorKey.Default, RequiredRunner.Architecture, OwningSlice.Slice6,
            "Provider_Should_NotImplementLegacyDraftStore"));
        entries.Add(new(CaseId.C11, "Composition", "Rule", EvidenceVectorKey.Default, RequiredRunner.Architecture, OwningSlice.Slice6,
            "Provider_Should_NotDefineDataPermissionScopeStore"));
        entries.Add(new(CaseId.C12, "Composition", "AotScenarioVariant", EvidenceVectorKey.Default, RequiredRunner.Aot, OwningSlice.Slice11,
            "DurableControlPlaneReferenceDataAotFixture_Should_PublishLinkAndRun"));
        entries.Add(new(CaseId.C13, "Composition", "DescriptorPayloadVariant", EvidenceVectorKey.Default, RequiredRunner.Architecture, OwningSlice.Slice5,
            "DescriptorPayloadGraph_Should_HaveClosedAotPersistenceMapping"));
        entries.Add(new(CaseId.C14, "Composition", "Composition", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice9,
            "OptInWithoutBaseProvider_Should_FailWithClearCompositionError"));
        entries.Add(new(CaseId.C15, "Composition", "Composition", EvidenceVectorKey.Default, RequiredRunner.InMemory, OwningSlice.Slice9,
            "RepeatedBaseFirstOptIn_Should_RemainIdempotent"));

        return entries.AsReadOnly();
    }

    private static IReadOnlyList<EvidenceTuple> BuildEvidenceTuples()
    {
        var tuples = new List<EvidenceTuple>();
        foreach (var entry in AllCases)
        {
            foreach (var variant in ExpandVariants(entry))
            {
                var evidenceKey = EvidenceVectorExpansion
                    .Where(pair => pair.Key.CaseId == entry.CaseId
                        && string.Equals(pair.Key.Variant, EvidenceVariantAlias(entry.CaseId, variant), StringComparison.Ordinal))
                    .SelectMany(pair => pair.Value)
                    .DefaultIfEmpty(EvidenceVectorKey.Default);
                var runners = RunnerExpansion.TryGetValue(entry.CaseId, out var expanded)
                    ? expanded
                    : ImmutableArray.Create(entry.Runner);
                foreach (var key in evidenceKey)
                {
                    foreach (var runner in runners)
                    {
                        tuples.Add(new EvidenceTuple(
                            entry.CaseId,
                            entry.Surface,
                            variant,
                            key,
                            runner,
                            entry.NormativeTestName));
                    }
                }
            }
        }

        return tuples.AsReadOnly();
    }

    private static IReadOnlyList<string> ExpandVariants(CaseManifestEntry entry)
        => (entry.CaseId, entry.Variant) switch
        {
            (CaseId.O19, "ScopedKeyCollisionVariant") => Enum.GetNames<ScopedKeyCollisionVariant>(),
            (CaseId.O22, "OrganizationCreatedAtVariant") => Enum.GetNames<OrganizationCreatedAtVariant>(),
            _ => entry.Variant switch
            {
            "DescriptorPayloadVariant" => Enum.GetNames<DescriptorPayloadVariant>(),
            "DraftQueryVariant" => Enum.GetNames<DraftQueryVariant>(),
            "DraftValidatorOwnedInvalidVariant" => Enum.GetNames<DraftValidatorOwnedInvalidVariant>(),
            "OrganizationIdentitySurface" => Enum.GetNames<OrganizationIdentitySurface>(),
            "OrganizationQuerySurface" => Enum.GetNames<OrganizationQuerySurface>(),
            "OrganizationEntitySurface" => Enum.GetNames<OrganizationEntitySurface>(),
            "OrganizationReadSurface" => Enum.GetNames<OrganizationReadSurface>(),
            "MissingReferenceVariant" => Enum.GetNames<MissingReferenceVariant>(),
            "ScopedKeyCollisionVariant" => Enum.GetNames<ScopedKeyCollisionVariant>(),
            "OrganizationCreatedAtVariant" => Enum.GetNames<OrganizationCreatedAtVariant>(),
            "RuleExactEmptyVariant" => Enum.GetNames<RuleExactEmptyVariant>(),
            "PersistedRuleCorruptionVariant" => Enum.GetNames<PersistedRuleCorruptionVariant>(),
            "IdentityValidationVector" => Enum.GetNames<IdentityValidationVector>(),
            "RuleSentinelField" => Enum.GetNames<RuleSentinelField>(),
            "PersistedEnumSurface" => Enum.GetNames<PersistedEnumSurface>(),
            "StoreMethodSurface" => Enum.GetNames<StoreMethodSurface>(),
            "SaveSurface" => Enum.GetNames<SaveSurface>(),
            "PersistedSnapshotCorruptionVariant" => Enum.GetNames<PersistedSnapshotCorruptionVariant>(),
            "PersistedStructuredFieldVariant" => Enum.GetNames<PersistedStructuredFieldVariant>(),
            "AotScenarioVariant" => Enum.GetNames<AotScenarioVariant>(),
            _ => [entry.Variant]
            }
        };

    private static string EvidenceVariantAlias(string caseId, string variant)
        => caseId == CaseId.F09
            ? variant switch
            {
                "OrganizationUnitTenantScope" => "OrganizationUnit.TenantScope",
                "PositionTenantScope" => "Position.TenantScope",
                "MembershipTenantScope" => "Membership.TenantScope",
                "RoleAssignmentTenantScope" => "RoleAssignment.TenantScope",
                "OrganizationUnitParentId" => "OrganizationUnit.ParentId",
                "MembershipPositionId" => "Membership.PositionId",
                "RoleAssignmentOrganizationUnitId" => "RoleAssignment.OrganizationUnitId",
                _ => variant
            }
            : variant;

    private static IReadOnlyDictionary<(string CaseId, string Variant), ImmutableArray<EvidenceVectorKey>> BuildEvidenceVectorExpansion()
    {
        var dict = new Dictionary<(string, string), ImmutableArray<EvidenceVectorKey>>();

        dict[(CaseId.D08, "DraftIdBlank")] = ImmutableArray.Create(EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.D08, "DescriptorIdBlank")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.D08, "AuthorIdBlank")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.D08, "SupportedPayloadKindMismatch")] = ImmutableArray.Create(EvidenceVectorKey.WorkflowHeaderSchemaPayload);
        dict[(CaseId.D08, "DefinedNonPayloadKindMismatch")] = ImmutableArray.Create(EvidenceVectorKey.Unknown, EvidenceVectorKey.DynamicApiEndpoint, EvidenceVectorKey.McpTool, EvidenceVectorKey.AgentTool);
        dict[(CaseId.D08, "PayloadIdMismatch")] = ImmutableArray.Create(EvidenceVectorKey.Default);
        dict[(CaseId.D08, "ProposedVersionMissing")] = ImmutableArray.Create(EvidenceVectorKey.Create, EvidenceVectorKey.Update);
        dict[(CaseId.D08, "ProposedVersionNotInteger")] = ImmutableArray.Create(EvidenceVectorKey.Default);
        dict[(CaseId.D08, "ProposedVersionMismatch")] = ImmutableArray.Create(EvidenceVectorKey.Default);
        dict[(CaseId.D08, "CreateBaseVersionPresent")] = ImmutableArray.Create(EvidenceVectorKey.Default);
        dict[(CaseId.D08, "UpdateBaseVersionMissing")] = ImmutableArray.Create(EvidenceVectorKey.Default);
        dict[(CaseId.D08, "DeprecateBaseVersionMissing")] = ImmutableArray.Create(EvidenceVectorKey.Default);
        dict[(CaseId.D08, "RemoveBaseVersionMissing")] = ImmutableArray.Create(EvidenceVectorKey.Default);

        dict[(CaseId.V01, "DraftNullInstance")] = ImmutableArray.Create(EvidenceVectorKey.Null);
        dict[(CaseId.V01, "DraftNullTenantId")] = ImmutableArray.Create(EvidenceVectorKey.Null);
        dict[(CaseId.V01, "DraftNullDraftId")] = ImmutableArray.Create(EvidenceVectorKey.Null);
        dict[(CaseId.V01, "DraftNullPayload")] = ImmutableArray.Create(EvidenceVectorKey.Null);
        dict[(CaseId.V01, "DraftGetNullTenantId")] = ImmutableArray.Create(EvidenceVectorKey.Null);
        dict[(CaseId.V01, "DraftGetNullDraftId")] = ImmutableArray.Create(EvidenceVectorKey.Null);
        dict[(CaseId.V01, "DraftListNullTenantId")] = ImmutableArray.Create(EvidenceVectorKey.Null);
        dict[(CaseId.V01, "UnitNullInstance")] = ImmutableArray.Create(EvidenceVectorKey.Null);
        dict[(CaseId.V01, "PositionNullInstance")] = ImmutableArray.Create(EvidenceVectorKey.Null);
        dict[(CaseId.V01, "MembershipNullInstance")] = ImmutableArray.Create(EvidenceVectorKey.Null);
        dict[(CaseId.V01, "RoleAssignmentNullInstance")] = ImmutableArray.Create(EvidenceVectorKey.Null);
        dict[(CaseId.V01, "RuleNullInstance")] = ImmutableArray.Create(EvidenceVectorKey.Null);

        dict[(CaseId.V01, "UnitInvalidId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty);
        dict[(CaseId.V01, "PositionInvalidId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty);
        dict[(CaseId.V01, "MembershipInvalidId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty);
        dict[(CaseId.V01, "MembershipInvalidUserId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty);
        dict[(CaseId.V01, "MembershipInvalidOrganizationUnitId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty);
        dict[(CaseId.V01, "RoleAssignmentInvalidId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty);
        dict[(CaseId.V01, "RoleAssignmentInvalidUserId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty);
        dict[(CaseId.V01, "RoleAssignmentInvalidRoleId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty);
        dict[(CaseId.V01, "UnitPointReadInvalidId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty);
        dict[(CaseId.V01, "PositionPointReadInvalidId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty);
        dict[(CaseId.V01, "MembershipByUserInvalidUserId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty);
        dict[(CaseId.V01, "MembershipByUnitInvalidOrganizationUnitId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty);
        dict[(CaseId.V01, "RoleByUserInvalidUserId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty);
        dict[(CaseId.V01, "RuleInvalidResource")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty);

        dict[(CaseId.V01, "UnitInvalidNonNullTenant")] = ImmutableArray.Create(EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.V01, "PositionInvalidNonNullTenant")] = ImmutableArray.Create(EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.V01, "MembershipInvalidNonNullTenant")] = ImmutableArray.Create(EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.V01, "RoleAssignmentInvalidNonNullTenant")] = ImmutableArray.Create(EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.V01, "OrganizationQueryInvalidNonNullTenant")] = ImmutableArray.Create(EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.V01, "RuleInvalidNonNullTenant")] = ImmutableArray.Create(EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);

        // Optional fields: the store rejects only empty (IsNullOrEmpty) and only
        // when the value is non-null, so null and whitespace are not invalid vectors.
        dict[(CaseId.V01, "MembershipInvalidPositionId")] = ImmutableArray.Create(EvidenceVectorKey.Empty);
        dict[(CaseId.V01, "RoleAssignmentInvalidOrganizationUnitId")] = ImmutableArray.Create(EvidenceVectorKey.Empty);

        dict[(CaseId.O19, "StoreTenantDelimiter")] = ImmutableArray.Create(EvidenceVectorKey.Default);
        dict[(CaseId.O19, "StoreIdDelimiter")] = ImmutableArray.Create(EvidenceVectorKey.Default);
        dict[(CaseId.O22, "MembershipNonZeroOffset")] = ImmutableArray.Create(EvidenceVectorKey.Default);
        dict[(CaseId.O22, "MembershipHundredNanosecondOrder")] = ImmutableArray.Create(EvidenceVectorKey.Default);

        dict[(CaseId.F09, "OrganizationUnit.TenantScope")] = ImmutableArray.Create(EvidenceVectorKey.JsonGlobalColumnsExact, EvidenceVectorKey.JsonExactColumnsGlobal);
        dict[(CaseId.F09, "Position.TenantScope")] = ImmutableArray.Create(EvidenceVectorKey.JsonGlobalColumnsExact, EvidenceVectorKey.JsonExactColumnsGlobal);
        dict[(CaseId.F09, "Membership.TenantScope")] = ImmutableArray.Create(EvidenceVectorKey.JsonGlobalColumnsExact, EvidenceVectorKey.JsonExactColumnsGlobal);
        dict[(CaseId.F09, "RoleAssignment.TenantScope")] = ImmutableArray.Create(EvidenceVectorKey.JsonGlobalColumnsExact, EvidenceVectorKey.JsonExactColumnsGlobal);
        dict[(CaseId.F09, "OrganizationUnit.ParentId")] = ImmutableArray.Create(EvidenceVectorKey.JsonNullColumnNonNull, EvidenceVectorKey.JsonNonNullColumnNull);
        dict[(CaseId.F09, "Membership.PositionId")] = ImmutableArray.Create(EvidenceVectorKey.JsonNullColumnNonNull, EvidenceVectorKey.JsonNonNullColumnNull);
        dict[(CaseId.F09, "RoleAssignment.OrganizationUnitId")] = ImmutableArray.Create(EvidenceVectorKey.JsonNullColumnNonNull, EvidenceVectorKey.JsonNonNullColumnNull);

        foreach (var variant in Enum.GetNames<PersistedRuleCorruptionVariant>())
            dict[(CaseId.P13, variant)] = ImmutableArray.Create(
                EvidenceVectorKey.SchemaReject,
                EvidenceVectorKey.ProviderFailClosed);

        return dict;
    }
}
