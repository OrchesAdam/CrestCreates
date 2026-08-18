using System.Collections.Immutable;

namespace CrestCreates.ControlPlane.ReferenceData.Persistence.Testing;

public static class ControlPlaneReferenceDataCaseManifest
{
    public static IReadOnlyList<CaseManifestEntry> AllCases { get; } = BuildAllCases();

    public static IReadOnlyDictionary<(string CaseId, string Variant), ImmutableArray<EvidenceVectorKey>> EvidenceVectorExpansion { get; }
        = BuildEvidenceVectorExpansion();

    public static IReadOnlyList<EvidenceTuple> EvidenceTuples { get; } = BuildEvidenceTuples();

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
                foreach (var key in evidenceKey)
                {
                    tuples.Add(new EvidenceTuple(
                        entry.CaseId,
                        entry.Surface,
                        variant,
                        key,
                        entry.Runner,
                        entry.NormativeTestName));
                }
            }
        }

        return tuples.AsReadOnly();
    }

    private static IReadOnlyList<string> ExpandVariants(CaseManifestEntry entry)
        => entry.Variant switch
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
        dict[(CaseId.D08, "ProposedVersionMissing")] = ImmutableArray.Create(EvidenceVectorKey.Create, EvidenceVectorKey.Update);

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

        dict[(CaseId.V01, "UnitInvalidId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.V01, "PositionInvalidId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.V01, "MembershipInvalidId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.V01, "MembershipInvalidUserId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.V01, "MembershipInvalidOrganizationUnitId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.V01, "RoleAssignmentInvalidId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.V01, "RoleAssignmentInvalidUserId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.V01, "RoleAssignmentInvalidRoleId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.V01, "UnitPointReadInvalidId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.V01, "PositionPointReadInvalidId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.V01, "MembershipByUserInvalidUserId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.V01, "MembershipByUnitInvalidOrganizationUnitId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.V01, "RoleByUserInvalidUserId")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.V01, "RuleInvalidResource")] = ImmutableArray.Create(EvidenceVectorKey.Null, EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);

        dict[(CaseId.V01, "UnitInvalidNonNullTenant")] = ImmutableArray.Create(EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.V01, "PositionInvalidNonNullTenant")] = ImmutableArray.Create(EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.V01, "MembershipInvalidNonNullTenant")] = ImmutableArray.Create(EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.V01, "RoleAssignmentInvalidNonNullTenant")] = ImmutableArray.Create(EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.V01, "OrganizationQueryInvalidNonNullTenant")] = ImmutableArray.Create(EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.V01, "RuleInvalidNonNullTenant")] = ImmutableArray.Create(EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);

        dict[(CaseId.V01, "MembershipInvalidPositionId")] = ImmutableArray.Create(EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);
        dict[(CaseId.V01, "RoleAssignmentInvalidOrganizationUnitId")] = ImmutableArray.Create(EvidenceVectorKey.Empty, EvidenceVectorKey.Whitespace);

        dict[(CaseId.F09, "OrganizationUnit.TenantScope")] = ImmutableArray.Create(EvidenceVectorKey.JsonGlobalColumnsExact, EvidenceVectorKey.JsonExactColumnsGlobal);
        dict[(CaseId.F09, "Position.TenantScope")] = ImmutableArray.Create(EvidenceVectorKey.JsonGlobalColumnsExact, EvidenceVectorKey.JsonExactColumnsGlobal);
        dict[(CaseId.F09, "Membership.TenantScope")] = ImmutableArray.Create(EvidenceVectorKey.JsonGlobalColumnsExact, EvidenceVectorKey.JsonExactColumnsGlobal);
        dict[(CaseId.F09, "RoleAssignment.TenantScope")] = ImmutableArray.Create(EvidenceVectorKey.JsonGlobalColumnsExact, EvidenceVectorKey.JsonExactColumnsGlobal);
        dict[(CaseId.F09, "OrganizationUnit.ParentId")] = ImmutableArray.Create(EvidenceVectorKey.JsonNullColumnNonNull, EvidenceVectorKey.JsonNonNullColumnNull);
        dict[(CaseId.F09, "Membership.PositionId")] = ImmutableArray.Create(EvidenceVectorKey.JsonNullColumnNonNull, EvidenceVectorKey.JsonNonNullColumnNull);
        dict[(CaseId.F09, "RoleAssignment.OrganizationUnitId")] = ImmutableArray.Create(EvidenceVectorKey.JsonNullColumnNonNull, EvidenceVectorKey.JsonNonNullColumnNull);

        return dict;
    }
}
