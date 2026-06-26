using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Metadata.Abstractions.Evidence;
using FluentAssertions;
using Moq;
using Xunit;

using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;
using DraftPackagePreview = CrestCreates.DescriptorDraft.Abstractions.DescriptorPackagePreview;
using DraftQuery = CrestCreates.DescriptorDraft.Abstractions.DraftQuery;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Regression tests for the second round of visibility review findings:
/// nested projection closure, descriptor identity matching, projection
/// failure semantics, draft comparison namespace validation, and
/// invalid vs denied DescriptorKind error codes.
/// </summary>
public class NestedProjectionRegressionTests : AgentControlPlaneTestBase
{
    // ── Finding 1/2: Nested projection closure + fail-open fix ──

    [Fact]
    public async Task ReviewDescriptorDraft_FiltersImpactAffectedDescriptors_ByVisibleKind()
    {
        var draft = CreateTestDraft(kind: DescriptorKind.Event);
        SetupDraftStoreGetReturns(draft);
        SetupCatalogGetAllReturns(
            CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event),
            CreateTestDescriptor("ns", "desc-002", DescriptorKind.Capability));
        SetupReviewServiceReturns(draft, affectedDescriptors: new[]
        {
            new AffectedDescriptor
            {
                Ref = new DescriptorRef("ns", "desc-001"),
                Kind = DescriptorKind.Event,
                Name = "EventDesc",
                Severity = DescriptorImpactSeverity.High,
                RuntimeAreas = Array.Empty<DescriptorImpactRuntimeArea>(),
                Paths = Array.Empty<DescriptorImpactPath>()
            },
            new AffectedDescriptor
            {
                Ref = new DescriptorRef("ns", "desc-002"),
                Kind = DescriptorKind.Capability,
                Name = "DeniedCap",
                Severity = DescriptorImpactSeverity.Critical,
                RuntimeAreas = new[] { DescriptorImpactRuntimeArea.Capability },
                Paths = Array.Empty<DescriptorImpactPath>()
            }
        });

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = ["Capability"]
        };
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.ReviewDescriptorDraft);
        var result = await service.ReviewDescriptorDraftAsync(context, draft.DraftId);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.ImpactAnalysisSummary.Should().NotBeNull();
        result.Value.ImpactAnalysisSummary!.AffectedDescriptors.Should()
            .ContainSingle().Which.Id.Should().Be("desc-001");
        result.Value.ImpactAnalysisSummary!.TotalAffectedCount.Should().Be(1);
    }

    [Fact]
    public async Task ReviewDescriptorDraft_FiltersCompatibilityFindings_AffectedRefs()
    {
        var draft = CreateTestDraft(kind: DescriptorKind.Event);
        SetupDraftStoreGetReturns(draft);
        SetupCatalogGetAllReturns(
            CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event),
            CreateTestDescriptor("ns", "desc-002", DescriptorKind.Capability));
        SetupReviewServiceReturns(draft, compatibilityFindings: new[]
        {
            new DescriptorCompatibilityFinding
            {
                Subject = new DescriptorRef("ns", "desc-001"),
                ChangeKind = DescriptorChangeKind.Updated,
                Level = DescriptorCompatibilityLevel.Risky,
                Kind = DescriptorCompatibilityFindingKind.Behavior,
                RuleId = "R001",
                Message = "OK",
                AffectedRefs = new[] { new DescriptorRef("ns", "desc-002") } // denied
            }
        });

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = ["Capability"]
        };
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.ReviewDescriptorDraft);
        var result = await service.ReviewDescriptorDraftAsync(context, draft.DraftId);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        // The finding's Subject is visible, so the finding is retained,
        // but its AffectedRefs must be stripped of denied refs.
        result.Value!.CompatibilitySummary.Should().NotBeNull();
        result.Value.CompatibilitySummary!.IncompatibilityCount.Should().Be(0);
        result.Value.CompatibilitySummary!.IsCompatible.Should().BeTrue();
    }

    // ── Finding 3: Descriptor identity matching (cross-namespace) ──

    [Fact]
    public async Task PackagePreview_CrossNamespace_SameId_IsAmbiguous()
    {
        var draft = CreateTestDraft(kind: DescriptorKind.Event, descriptorId: "desc-001");
        SetupDraftStoreGetReturns(draft);
        SetupCatalogGetAllReturns(
            CreateTestDescriptor("ns-a", "desc-001", DescriptorKind.Event),
            CreateTestDescriptor("ns-b", "desc-001", DescriptorKind.Event));
        SetupMaterializerReturns(draft, CreateTestDescriptor("ns-a", "desc-001", DescriptorKind.Event));
        SetupPackageBuilder();

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults;
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.PreviewDescriptorPackage);
        var result = await service.PreviewDescriptorPackageAsync(context, draft.DraftId);

        // Same bare ID in two namespaces → ambiguous, projection failure
        result.Status.Should().Be(AgentToolResultStatus.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "PACKAGE_PROJECTION_FAILURE");
    }

    [Fact]
    public async Task PackagePreview_SingleNamespace_SameId_Passes()
    {
        var draft = CreateTestDraft(kind: DescriptorKind.Event, descriptorId: "desc-001");
        SetupDraftStoreGetReturns(draft);
        SetupCatalogGetAllReturns(
            CreateTestDescriptor("ns-a", "desc-001", DescriptorKind.Event));
        SetupMaterializerReturns(draft, CreateTestDescriptor("ns-a", "desc-001", DescriptorKind.Event));
        SetupPackageBuilder();

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults;
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.PreviewDescriptorPackage);
        var result = await service.PreviewDescriptorPackageAsync(context, draft.DraftId);

        result.Status.Should().Be(AgentToolResultStatus.Success);
    }

    // ── Finding 4: Draft comparison namespace validation ──

    [Fact]
    public async Task CompareDescriptorDraft_ActiveInDifferentNamespace_IsMasked()
    {
        var draft = CreateTestDraft(
            kind: DescriptorKind.Event, descriptorId: "desc-001", draftId: "draft-comp-001");
        SetupDraftStoreGetReturns(draft);
        // Catalog returns a descriptor with same ID but different namespace
        var wrongNsDescriptor = CreateTestDescriptor("other-ns", "desc-001", DescriptorKind.Event);
        DescriptorCatalogMock.Setup(c => c.Get("desc-001")).Returns(wrongNsDescriptor);

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults;
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.CompareDescriptorDraft);
        var result = await service.CompareDescriptorDraftAsync(context, draft.DraftId);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        // Wrong namespace → active masked, shown as Added (draft-only)
        result.Value!.CurrentActiveDescriptor.Should().BeNull();
        result.Value.Differences.Should().Contain(d => d.Kind == DraftDifferenceKind.Added);
    }

    // ── Finding 5: Invalid vs denied DescriptorKind error semantics ──

    [Fact]
    public async Task ListDescriptorDrafts_InvalidDescriptorKind_ReturnsAuthUnavailable()
    {
        var options = AgentToolAuthorizationOptions.DevelopmentDefaults;
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.ListDescriptorDrafts);
        var query = new DraftQuery { DescriptorKind = (DescriptorKind)99 }; // invalid
        var result = await service.ListDescriptorDraftsAsync(context, query);

        result.Status.Should().Be(AgentToolResultStatus.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "AUTHORIZATION_CONTEXT_UNAVAILABLE");
    }

    [Fact]
    public async Task ListDescriptorDrafts_DeniedDescriptorKind_ReturnsDescKindDenied()
    {
        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = ["Capability"]
        };
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.ListDescriptorDrafts);
        var query = new DraftQuery { DescriptorKind = DescriptorKind.Capability };
        var result = await service.ListDescriptorDraftsAsync(context, query);

        result.Status.Should().Be(AgentToolResultStatus.Denied);
        result.Diagnostics.Should().Contain(d => d.Code == "DESC_KIND_DENIED");
    }

    [Fact]
    public async Task ListDescriptorDrafts_AllowedKind_ReturnsSuccess()
    {
        var options = AgentToolAuthorizationOptions.DevelopmentDefaults;
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.ListDescriptorDrafts);
        var query = new DraftQuery { DescriptorKind = DescriptorKind.Event };
        DraftStoreMock.Setup(s => s.ListAsync(TestTenantId, It.IsAny<DraftQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Draft>());
        var result = await service.ListDescriptorDraftsAsync(context, query);

        result.Status.Should().Be(AgentToolResultStatus.Success);
    }

    // ── Finding 1: Summary recalculation ──

    [Fact]
    public async Task ReviewDescriptorDraft_RecalculatesMaxSeverity_AfterFilteringDenied()
    {
        var draft = CreateTestDraft(kind: DescriptorKind.Event);
        SetupDraftStoreGetReturns(draft);
        SetupCatalogGetAllReturns(
            CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event),
            CreateTestDescriptor("ns", "desc-002", DescriptorKind.Capability));
        // Denied Capability has Critical severity — must not affect MaxSeverity
        SetupReviewServiceReturns(draft, affectedDescriptors: new[]
        {
            new AffectedDescriptor
            {
                Ref = new DescriptorRef("ns", "desc-001"), Kind = DescriptorKind.Event,
                Name = "E", Severity = DescriptorImpactSeverity.Low,
                RuntimeAreas = Array.Empty<DescriptorImpactRuntimeArea>(),
                Paths = Array.Empty<DescriptorImpactPath>()
            },
            new AffectedDescriptor
            {
                Ref = new DescriptorRef("ns", "desc-002"), Kind = DescriptorKind.Capability,
                Name = "C", Severity = DescriptorImpactSeverity.Critical,
                RuntimeAreas = new[] { DescriptorImpactRuntimeArea.Capability },
                Paths = Array.Empty<DescriptorImpactPath>()
            }
        });

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = ["Capability"]
        };
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.ReviewDescriptorDraft);
        var result = await service.ReviewDescriptorDraftAsync(context, draft.DraftId);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.ImpactAnalysisSummary!.Severity.Should().Be("Low");
    }

    [Fact]
    public async Task ReviewDescriptorDraft_RecalculatesMaxLevel_AfterFilteringDenied()
    {
        var draft = CreateTestDraft(kind: DescriptorKind.Event);
        SetupDraftStoreGetReturns(draft);
        SetupCatalogGetAllReturns(
            CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event));
        SetupReviewServiceReturns(draft, compatibilityFindings: new[]
        {
            // Visible — stays, MaxLevel = Breaking (highest)
            new DescriptorCompatibilityFinding
            {
                Subject = new DescriptorRef("ns", "desc-001"),
                ChangeKind = DescriptorChangeKind.Updated,
                Level = DescriptorCompatibilityLevel.Breaking,
                Kind = DescriptorCompatibilityFindingKind.Contract,
                RuleId = "R001", Message = "Visible"
            },
            // Denied (Subject = Capability kind in catalog, and Capability is denied)
            new DescriptorCompatibilityFinding
            {
                Subject = new DescriptorRef("ns", "desc-002"),
                ChangeKind = DescriptorChangeKind.Removed,
                Level = DescriptorCompatibilityLevel.Breaking,
                Kind = DescriptorCompatibilityFindingKind.Structural,
                RuleId = "R002", Message = "DeniedCapFinding"
            }
        });

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = ["Capability"]
        };
        // Catalog needs the denied-Capability descriptor so refLookup can resolve it
        SetupCatalogGetAllReturns(
            CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event),
            CreateTestDescriptor("ns", "desc-002", DescriptorKind.Capability));
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.ReviewDescriptorDraft);
        var result = await service.ReviewDescriptorDraftAsync(context, draft.DraftId);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        // The denied finding (Breaking) is removed; only the visible one (also
        // Breaking, but with visible Subject) remains, so compatibility shows 1 incompatibility.
        result.Value!.CompatibilitySummary!.IncompatibilityCount.Should().Be(1);
        result.Value.CompatibilitySummary!.IsCompatible.Should().BeFalse();
    }

    // ── Finding 2: Version-aware ref matching ──

    [Fact]
    public async Task ReviewDescriptorDraft_PinnedRefToDeniedVersion_IsFilteredFromResult()
    {
        // Draft kind is visible (Event NOT denied). Catalog has v1 (visible)
        // and v2 (Capability, denied). An AffectedDescriptor with pinned ref
        // to v2 must be filtered out because the exact version is denied.
        var draft = CreateTestDraft(kind: DescriptorKind.Event);
        SetupDraftStoreGetReturns(draft);
        var visibleV1 = new TestVersionedDescriptor("ns", "desc-001", version: 1, "VisibleV1")
        {
            Kind = DescriptorKind.Event,
            State = DescriptorState.Active
        };
        var deniedV2 = new TestVersionedDescriptor("ns", "desc-001", version: 2, "DeniedV2")
        {
            Kind = DescriptorKind.Capability,
            State = DescriptorState.Active
        };
        SetupCatalogGetAllReturns(visibleV1, deniedV2);
        SetupReviewServiceReturns(draft, affectedDescriptors: new[]
        {
            // Both have visible Kind=Event, so Kind filter passes for both.
            // V2 pinned ref must be filtered by VisibleRefLookup because the
            // catalog entry for v2 has Kind=Capability (denied).
            new AffectedDescriptor
            {
                Ref = new DescriptorRef("ns", "desc-001", Version: 2),
                Kind = DescriptorKind.Event,
                Name = "PinnedToDeniedV2",
                Severity = DescriptorImpactSeverity.Critical,
                RuntimeAreas = new[] { DescriptorImpactRuntimeArea.Event },
                Paths = Array.Empty<DescriptorImpactPath>()
            },
            new AffectedDescriptor
            {
                Ref = new DescriptorRef("ns", "desc-001", Version: 1),
                Kind = DescriptorKind.Event,
                Name = "PinnedToVisibleV1",
                Severity = DescriptorImpactSeverity.Low,
                RuntimeAreas = Array.Empty<DescriptorImpactRuntimeArea>(),
                Paths = Array.Empty<DescriptorImpactPath>()
            }
        });

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = ["Capability"]
        };
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.ReviewDescriptorDraft);
        var result = await service.ReviewDescriptorDraftAsync(context, draft.DraftId);

        // Draft kind (Event) is visible → review proceeds. V2 is denied
        // (Capability) so its pinned ref must be filtered out. V1 is visible
        // (Event) so it passes.
        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.ImpactAnalysisSummary!.AffectedDescriptors.Should()
            .ContainSingle().Which.Version.Should().Be(1);
    }

    // ── Finding 3: BaseVersion in comparison ──

    [Fact]
    public async Task CompareDescriptorDraft_BaseVersionMismatch_IsMasked()
    {
        var draft = CreateTestDraft(
            kind: DescriptorKind.Event, descriptorId: "desc-001", draftId: "draft-bv-001");
        draft = draft with { BaseVersion = "1" };
        SetupDraftStoreGetReturns(draft);
        // Catalog returns v2 descriptor — matches namespace but NOT BaseVersion "1"
        var v2Descriptor = new TestVersionedDescriptor("test", "desc-001", version: 2, "V2Desc")
        {
            Kind = DescriptorKind.Event,
            State = DescriptorState.Active
        };
        DescriptorCatalogMock.Setup(c => c.Get("desc-001")).Returns(v2Descriptor);

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults;
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.CompareDescriptorDraft);
        var result = await service.CompareDescriptorDraftAsync(context, draft.DraftId);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        // BaseVersion mismatch → active masked, shown as Added
        result.Value!.CurrentActiveDescriptor.Should().BeNull();
    }

    [Fact]
    public async Task CompareDescriptorDraft_BaseVersionMatch_ShowsModified()
    {
        var draft = CreateTestDraft(
            kind: DescriptorKind.Event, descriptorId: "desc-001", draftId: "draft-bvmatch-001");
        draft = draft with { BaseVersion = "1" };
        SetupDraftStoreGetReturns(draft);
        // Catalog returns v1 descriptor matching draft payload namespace + BaseVersion
        var v1Descriptor = new TestVersionedDescriptor("event", "desc-001", version: 1, "V1Desc")
        {
            Kind = DescriptorKind.Event,
            State = DescriptorState.Active
        };
        DescriptorCatalogMock.Setup(c => c.Get("desc-001")).Returns(v1Descriptor);

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults;
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.CompareDescriptorDraft);
        var result = await service.CompareDescriptorDraftAsync(context, draft.DraftId);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.CurrentActiveDescriptor.Should().NotBeNull();
    }

    // ── Finding 6: Empty path filtering ──

    [Fact]
    public async Task ReviewDescriptorDraft_EmptySegments_PathIsRemoved()
    {
        var draft = CreateTestDraft(kind: DescriptorKind.Event);
        SetupDraftStoreGetReturns(draft);
        // desc-002 and desc-003 exist in the catalog but as denied Capability kind.
        // Their segments must be stripped by VisibleRefLookup because the descriptors
        // are present in AllTenantDescriptors but absent from VisibleDescriptors.
        SetupCatalogGetAllReturns(
            CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event),
            CreateTestDescriptor("ns", "desc-002", DescriptorKind.Capability),
            CreateTestDescriptor("ns", "desc-003", DescriptorKind.Capability));
        SetupReviewServiceReturnsFull(draft,
            impactPaths: new[]
            {
                new DescriptorImpactPath
                {
                    SourceChange = new DescriptorRef("ns", "desc-001"),
                    Affected = new DescriptorRef("ns", "desc-001"),
                    Segments = new[]
                    {
                        new DescriptorImpactPathSegment
                        {
                            From = new DescriptorRef("ns", "desc-002"),
                            To = new DescriptorRef("ns", "desc-003"),
                            Kind = RelationshipKind.Uses,
                            Strength = RelationshipStrength.Strong,
                            IsRuntimeBinding = false
                        }
                    }
                }
            });

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = ["Capability"]
        };
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.ReviewDescriptorDraft);
        var result = await service.ReviewDescriptorDraftAsync(context, draft.DraftId);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        // Both segment endpoints are Capability (denied) — all segments stripped,
        // path removed entirely.
        result.Value!.ImpactAnalysisSummary.Should().NotBeNull();
        result.Value.ImpactAnalysisSummary!.AffectedDescriptors.Should().BeEmpty();
    }

    // ── Finding 4: Projection failure prevents mutation ──

    [Fact]
    public async Task ReviewDescriptorDraft_PackageProjectionFailure_ReturnsFailed()
    {
        var draft = CreateTestDraft(kind: DescriptorKind.Event, draftId: "draft-pf-001");
        SetupDraftStoreGetReturns(draft);
        SetupCatalogGetAllReturns(
            CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event),
            CreateTestDescriptor("ns", "desc-002", DescriptorKind.Capability));
        SetupReviewServiceReturnsFull(draft,
            packagePreview: new DraftPackagePreview
            {
                PackageManifestHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "v1", ArtifactKind = CanonicalHashArtifactNames.Descriptor, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.Contract, ContractVersion = "v1", CanonicalShapeVersion = "v1", Value = "mh-1" },
                PackageEvidenceHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "v1", ArtifactKind = CanonicalHashArtifactNames.Descriptor, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.Contract, ContractVersion = "v1", CanonicalShapeVersion = "v1", Value = "eh-1" },
                PackageEvidenceEnvelopeHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "v1", ArtifactKind = CanonicalHashArtifactNames.Descriptor, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.Contract, ContractVersion = "v1", CanonicalShapeVersion = "v1", Value = "env-1" },
                DescriptorIds = new[] { "desc-002" } // known Capability descriptor
            });

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = ["Capability"]
        };
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.ReviewDescriptorDraft);
        var result = await service.ReviewDescriptorDraftAsync(context, draft.DraftId);

        // Package projection failure → Failed (not Success, not Denied)
        result.Status.Should().Be(AgentToolResultStatus.Failed);
        result.Diagnostics.Should().Contain(d => d.Code == "PACKAGE_PROJECTION_FAILURE");
        // Verify: draft was NOT saved as Reviewed
        DraftStoreMock.Verify(
            s => s.SaveAsync(It.Is<Draft>(d => d.Status == DraftAbstractions.DescriptorDraftStatus.Reviewed), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── I3: Successful package preview through ProjectReview ──

    [Fact]
    public async Task ReviewDescriptorDraft_PackagePreviewAllVisible_PassesAndReturnsPreview()
    {
        var draft = CreateTestDraft(kind: DescriptorKind.Event, draftId: "draft-pp-ok");
        SetupDraftStoreGetReturns(draft);
        SetupCatalogGetAllReturns(
            CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event));
        SetupReviewServiceReturnsFull(draft,
            packagePreview: new DraftPackagePreview
            {
                PackageManifestHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "v1", ArtifactKind = CanonicalHashArtifactNames.Descriptor, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.Contract, ContractVersion = "v1", CanonicalShapeVersion = "v1", Value = "mh-1" },
                PackageEvidenceHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "v1", ArtifactKind = CanonicalHashArtifactNames.Descriptor, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.Contract, ContractVersion = "v1", CanonicalShapeVersion = "v1", Value = "eh-1" },
                PackageEvidenceEnvelopeHash = new CanonicalHash { Algorithm = "SHA-256", AlgorithmVersion = "v1", ArtifactKind = CanonicalHashArtifactNames.Descriptor, Scope = CanonicalHashScopeNames.InternalFull, Purpose = CanonicalHashPurposeNames.Contract, ContractVersion = "v1", CanonicalShapeVersion = "v1", Value = "env-1" },
                DescriptorIds = new[] { "desc-001" }
            });

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults;
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.ReviewDescriptorDraft);
        var result = await service.ReviewDescriptorDraftAsync(context, draft.DraftId);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.PackagePreview.Should().NotBeNull();
        result.Value.PackagePreview!.DescriptorIds.Should().Contain("desc-001");
    }

    // ── I4: Evidence projection ──

    [Fact]
    public async Task BuildPackageEvidencePreview_FiltersDeniedFindings()
    {
        var draft = CreateTestDraft(kind: DescriptorKind.Event, draftId: "draft-ev");
        SetupDraftStoreGetReturns(draft);
        SetupCatalogGetAllReturns(
            CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event),
            CreateTestDescriptor("ns", "desc-002", DescriptorKind.Capability));
        // Review result with no PackagePreview — ProjectReview succeeds,
        // evidence is built separately with denied findings to filter.
        SetupReviewServiceReturns(draft); // default: no PackagePreview, no denied refs
        SetupMaterializerReturns(draft, CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event));
        SetupPackageBuilderWithEvidence(new[]
        {
            new EvidenceFinding
            {
                Source = "Topology", Code = "E1", Severity = "Breaking",
                Subject = new DescriptorRef("ns", "desc-001"), Message = "Visible breaking"
            },
            new EvidenceFinding
            {
                Source = "Compatibility", Code = "E2", Severity = "Breaking",
                Subject = new DescriptorRef("ns", "desc-002"), Message = "Denied breaking"
            }
        });

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = ["Capability"]
        };
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.BuildPackageEvidencePreview);
        var result = await service.BuildPackageEvidencePreviewAsync(context, draft.DraftId);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        // The denied-capability finding must be filtered out
        result.Value!.Evidence.NormalizedFindings.Should().ContainSingle()
            .Which.Subject!.Value.Id.Should().Be("desc-001");
        // BreakingFindingCount recalculated from filtered findings
        result.Value.Evidence.BreakingFindingCount.Should().Be(1);
        // PackageFindingCount recalculated
        result.Value.Evidence.PackageFindingCount.Should().Be(1);
        // RequiresReview recalculated (has Breaking finding)
        result.Value.Evidence.RequiresReview.Should().BeTrue();
    }

    // ── I5: BaseVersion null is skipped ──

    [Fact]
    public async Task CompareDescriptorDraft_NullBaseVersion_SkippedAndMatches()
    {
        var draft = CreateTestDraft(
            kind: DescriptorKind.Event, descriptorId: "desc-001", draftId: "draft-nobv");
        draft = draft with { BaseVersion = null };
        SetupDraftStoreGetReturns(draft);
        // Catalog returns v2 descriptor — normally would mismatch, but null
        // BaseVersion skips version check entirely
        var v2Descriptor = new TestVersionedDescriptor("event", "desc-001", version: 2, "V2Desc")
        {
            Kind = DescriptorKind.Event,
            State = DescriptorState.Active
        };
        DescriptorCatalogMock.Setup(c => c.Get("desc-001")).Returns(v2Descriptor);

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults;
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.CompareDescriptorDraft);
        var result = await service.CompareDescriptorDraftAsync(context, draft.DraftId);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        // BaseVersion is null → version check skipped, v2 matches by namespace+id
        result.Value!.CurrentActiveDescriptor.Should().NotBeNull();
    }

    // ── M1: AffectedDescriptors count verification ──

    [Fact]
    public async Task ReviewDescriptorDraft_AffectedDescriptors_HasCorrectCount()
    {
        var draft = CreateTestDraft(kind: DescriptorKind.Event);
        SetupDraftStoreGetReturns(draft);
        SetupCatalogGetAllReturns(
            CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event),
            CreateTestDescriptor("ns", "desc-002", DescriptorKind.Capability));
        SetupReviewServiceReturns(draft, affectedDescriptors: new[]
        {
            new AffectedDescriptor
            {
                Ref = new DescriptorRef("ns", "desc-001"), Kind = DescriptorKind.Event,
                Name = "Visible", Severity = DescriptorImpactSeverity.High,
                RuntimeAreas = Array.Empty<DescriptorImpactRuntimeArea>(),
                Paths = Array.Empty<DescriptorImpactPath>()
            },
            new AffectedDescriptor
            {
                Ref = new DescriptorRef("ns", "desc-002"), Kind = DescriptorKind.Capability,
                Name = "Denied", Severity = DescriptorImpactSeverity.Critical,
                RuntimeAreas = new[] { DescriptorImpactRuntimeArea.Capability },
                Paths = Array.Empty<DescriptorImpactPath>()
            }
        });

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = ["Capability"]
        };
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.ReviewDescriptorDraft);
        var result = await service.ReviewDescriptorDraftAsync(context, draft.DraftId);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        // Not empty, not two — exactly one Event descriptor remains
        result.Value!.ImpactAnalysisSummary!.AffectedDescriptors.Should()
            .ContainSingle().Which.Id.Should().Be("desc-001");
    }

    // ── Helpers ──

    private void SetupDraftStoreGetReturns(Draft draft)
    {
        DraftStoreMock
            .Setup(s => s.GetAsync(draft.TenantId, draft.DraftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);
        DraftStoreMock
            .Setup(s => s.ListAsync(draft.TenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { draft });
    }

    private void SetupCatalogGetAllReturns(params IDescriptor[] descriptors)
    {
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns(descriptors);
    }

    private void SetupMaterializerReturns(Draft draft, params IDescriptor[] proposed)
    {
        DraftMaterializerMock
            .Setup(m => m.Materialize(draft, It.IsAny<IReadOnlyList<IDescriptor>>()))
            .Returns(DescriptorDraftMaterializationResult.Success(proposed));
    }

    private void SetupReviewServiceReturns(
        Draft draft,
        IReadOnlyList<AffectedDescriptor>? affectedDescriptors = null,
        IReadOnlyList<DescriptorCompatibilityFinding>? compatibilityFindings = null)
    {
        var reviewResult = new DescriptorDraftReviewResult
        {
            DraftId = draft.DraftId,
            TenantId = draft.TenantId,
            ValidationResult = DescriptorDraftValidationResult.Success(),
            ProposedInventory = Array.Empty<IDescriptor>(),
            ImpactAnalysisResult = new DescriptorImpactAnalysisReport
            {
                ChangeSet = new DescriptorChangeSet { Changes = Array.Empty<DescriptorChange>() },
                AffectedDescriptors = affectedDescriptors ?? Array.Empty<AffectedDescriptor>(),
                Paths = Array.Empty<DescriptorImpactPath>(),
                MaxSeverity = DescriptorImpactSeverity.Low,
                Diagnostics = Array.Empty<DescriptorImpactDiagnostic>()
            },
            CompatibilityResult = new DescriptorCompatibilityReport
            {
                ChangeSet = new DescriptorChangeSet { Changes = Array.Empty<DescriptorChange>() },
                ImpactReport = new DescriptorImpactAnalysisReport
                {
                    ChangeSet = new DescriptorChangeSet { Changes = Array.Empty<DescriptorChange>() },
                    AffectedDescriptors = Array.Empty<AffectedDescriptor>(),
                    Paths = Array.Empty<DescriptorImpactPath>(),
                    MaxSeverity = DescriptorImpactSeverity.Low,
                    Diagnostics = Array.Empty<DescriptorImpactDiagnostic>()
                },
                Findings = compatibilityFindings ?? Array.Empty<DescriptorCompatibilityFinding>(),
                MaxLevel = DescriptorCompatibilityLevel.Compatible,
                Diagnostics = Array.Empty<DescriptorCompatibilityDiagnostic>()
            },
            GovernanceDecision = new DescriptorLifecycleGovernanceReport
            {
                Decisions = Array.Empty<DescriptorLifecycleDecision>(),
                MaxDecision = DescriptorLifecycleDecisionKind.Allowed,
                PackageFindings = Array.Empty<DescriptorLifecycleFinding>()
            },
            Diagnostics = Array.Empty<DescriptorDraftDiagnostic>(),
            IsActivationEligible = true
        };

        DraftReviewServiceMock
            .Setup(s => s.ReviewAsync(draft, It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reviewResult);
    }

    private void SetupReviewServiceReturnsFull(
        Draft draft,
        IReadOnlyList<DescriptorImpactPath>? impactPaths = null,
        DraftPackagePreview? packagePreview = null)
    {
        var reviewResult = new DescriptorDraftReviewResult
        {
            DraftId = draft.DraftId,
            TenantId = draft.TenantId,
            ValidationResult = DescriptorDraftValidationResult.Success(),
            ProposedInventory = Array.Empty<IDescriptor>(),
            ImpactAnalysisResult = new DescriptorImpactAnalysisReport
            {
                ChangeSet = new DescriptorChangeSet { Changes = Array.Empty<DescriptorChange>() },
                AffectedDescriptors = Array.Empty<AffectedDescriptor>(),
                Paths = impactPaths ?? Array.Empty<DescriptorImpactPath>(),
                MaxSeverity = DescriptorImpactSeverity.Low,
                Diagnostics = Array.Empty<DescriptorImpactDiagnostic>()
            },
            CompatibilityResult = new DescriptorCompatibilityReport
            {
                ChangeSet = new DescriptorChangeSet { Changes = Array.Empty<DescriptorChange>() },
                ImpactReport = new DescriptorImpactAnalysisReport
                {
                    ChangeSet = new DescriptorChangeSet { Changes = Array.Empty<DescriptorChange>() },
                    AffectedDescriptors = Array.Empty<AffectedDescriptor>(),
                    Paths = Array.Empty<DescriptorImpactPath>(),
                    MaxSeverity = DescriptorImpactSeverity.Low,
                    Diagnostics = Array.Empty<DescriptorImpactDiagnostic>()
                },
                Findings = Array.Empty<DescriptorCompatibilityFinding>(),
                MaxLevel = DescriptorCompatibilityLevel.Compatible,
                Diagnostics = Array.Empty<DescriptorCompatibilityDiagnostic>()
            },
            GovernanceDecision = new DescriptorLifecycleGovernanceReport
            {
                Decisions = Array.Empty<DescriptorLifecycleDecision>(),
                MaxDecision = DescriptorLifecycleDecisionKind.Allowed,
                PackageFindings = Array.Empty<DescriptorLifecycleFinding>()
            },
            PackagePreview = packagePreview,
            Diagnostics = Array.Empty<DescriptorDraftDiagnostic>(),
            IsActivationEligible = true
        };

        DraftReviewServiceMock
            .Setup(s => s.ReviewAsync(draft, It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reviewResult);
    }

    private new void SetupPackageBuilder()
    {
        SetupPackageBuilderWithEvidence(Array.Empty<EvidenceFinding>());
    }

    private void SetupPackageBuilderWithEvidence(IReadOnlyList<EvidenceFinding> findings)
    {
        PackageBuilderMock.Setup(b => b.Build(It.IsAny<DescriptorPackageBuildRequest>()))
            .Returns((DescriptorPackageBuildRequest req) =>
            {
                var entries = req.Descriptors
                    .Select(d => new DescriptorManifestEntry
                    {
                        Ref = new DescriptorRef(d.Namespace, d.Id),
                        Kind = d.Kind,
                        Name = d.Name,
                        State = d.State,
                    })
                    .ToList().AsReadOnly();

                var manifest = new DescriptorManifest
                {
                    PackageId = req.PackageId,
                    PackageVersion = req.PackageVersion,
                    DescriptorEntries = entries
                };

                return new DescriptorPackage
                {
                    Manifest = manifest,
                    Snapshot = new DescriptorSnapshot(),
                    Evidence = new DescriptorPackageEvidence
                    {
                        NormalizedFindings = findings,
                        BreakingFindingCount = findings.Count(f =>
                            StringComparer.Ordinal.Equals(f.Severity, "Breaking")),
                        SecuritySensitiveFindingCount = findings.Count(f =>
                            StringComparer.Ordinal.Equals(f.Severity, "SecuritySensitive")),
                        UnsupportedFindingCount = findings.Count(f =>
                            StringComparer.Ordinal.Equals(f.Severity, "Unsupported")),
                        RequiresReview = findings.Any(f =>
                            StringComparer.Ordinal.Equals(f.Severity, "Breaking") ||
                            StringComparer.Ordinal.Equals(f.Severity, "SecuritySensitive")),
                        PackageFindingCount = findings.Count
                    }
                };
            });
    }

    // ── Finding: ReviewDescriptorDraft must pass visible inventory to ReviewAsync ──

    /// <summary>
    /// Verifies that ReviewDescriptorDraftAsync passes only visible descriptors
    /// (not the full catalog) to the review service. This prevents denied
    /// descriptors from influencing review computation before projection.
    /// </summary>
    [Fact]
    public async Task ReviewDescriptorDraft_UsesVisibleInventoryForReview()
    {
        var draft = CreateTestDraft(kind: DescriptorKind.Event);
        SetupDraftStoreGetReturns(draft);
        SetupCatalogGetAllReturns(
            CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event),
            CreateTestDescriptor("ns", "desc-002", DescriptorKind.Capability));

        // Capture the inventory argument passed to ReviewAsync
        IReadOnlyList<IDescriptor>? capturedInventory = null;
        DraftReviewServiceMock
            .Setup(r => r.ReviewAsync(It.IsAny<Draft>(), It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
            .Callback<Draft, IReadOnlyList<IDescriptor>, CancellationToken>((_, inventory, _) =>
                capturedInventory = inventory)
            .ReturnsAsync((Draft d, IReadOnlyList<IDescriptor> _, CancellationToken __) =>
                new DescriptorDraftReviewResult
                {
                    DraftId = d.DraftId,
                    TenantId = d.TenantId,
                    ValidationResult = DescriptorDraftValidationResult.Success(),
                    ProposedInventory = Array.Empty<IDescriptor>(),
                    ImpactAnalysisResult = new DescriptorImpactAnalysisReport
                    {
                        ChangeSet = new DescriptorChangeSet { Changes = Array.Empty<DescriptorChange>() },
                        AffectedDescriptors = Array.Empty<AffectedDescriptor>(),
                        Paths = Array.Empty<DescriptorImpactPath>(),
                        MaxSeverity = DescriptorImpactSeverity.Low,
                        Diagnostics = Array.Empty<DescriptorImpactDiagnostic>()
                    },
                    CompatibilityResult = new DescriptorCompatibilityReport
                    {
                        ChangeSet = new DescriptorChangeSet { Changes = Array.Empty<DescriptorChange>() },
                        ImpactReport = new DescriptorImpactAnalysisReport
                        {
                            ChangeSet = new DescriptorChangeSet { Changes = Array.Empty<DescriptorChange>() },
                            AffectedDescriptors = Array.Empty<AffectedDescriptor>(),
                            Paths = Array.Empty<DescriptorImpactPath>(),
                            MaxSeverity = DescriptorImpactSeverity.Low,
                            Diagnostics = Array.Empty<DescriptorImpactDiagnostic>()
                        },
                        Findings = Array.Empty<DescriptorCompatibilityFinding>(),
                        MaxLevel = DescriptorCompatibilityLevel.Compatible,
                        Diagnostics = Array.Empty<DescriptorCompatibilityDiagnostic>()
                    },
                    GovernanceDecision = new DescriptorLifecycleGovernanceReport
                    {
                        Decisions = Array.Empty<DescriptorLifecycleDecision>(),
                        MaxDecision = DescriptorLifecycleDecisionKind.Allowed,
                        PackageFindings = Array.Empty<DescriptorLifecycleFinding>()
                    },
                    Diagnostics = Array.Empty<DescriptorDraftDiagnostic>(),
                    IsActivationEligible = true
                });

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = ["Capability"]
        };
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.ReviewDescriptorDraft);
        await service.ReviewDescriptorDraftAsync(context, draft.DraftId);

        capturedInventory.Should().NotBeNull();
        capturedInventory!.Should().OnlyContain(d => d.Kind != DescriptorKind.Capability,
            "denied kinds must not be passed to ReviewAsync");
        capturedInventory.Should().Contain(d => d.Kind == DescriptorKind.Event,
            "allowed kinds must be included in the review inventory");
    }
}
