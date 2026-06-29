using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Projections;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.Abstractions.Evidence;
using CrestCreates.Metadata.ContextPack.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Regression tests for visibility closure gaps identified in the test coverage audit.
/// Each test targets a specific invariant that lacked coverage and would not catch
/// a regression if the fix were reverted.
/// </summary>
public class VisibilityClosureRegressionTests : AgentControlPlaneTestBase
{
    // ── Gap 1: IsActivationEligible re-derivation after projection ──

    /// <summary>
    /// When a review returns IsActivationEligible = false due to a governance blocker
    /// from a denied kind, the projector must re-derive IsActivationEligible = true
    /// after filtering out the denied blocker. A regression that copies
    /// source.IsActivationEligible would fail this test.
    /// </summary>
    [Fact]
    public async Task ReviewDescriptorDraft_ReDerivesIsActivationEligible_AfterFilteringDeniedBlockers()
    {
        var draft = CreateTestDraft(kind: DescriptorKind.Event);
        SetupDraftStoreGetReturns(draft);
        SetupCatalogGetAllReturns(
            CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event),
            CreateTestDescriptor("ns", "desc-002", DescriptorKind.Capability));

        // Review returns IsActivationEligible = false with a governance blocker
        // from a denied-kind descriptor
        DraftReviewServiceMock
            .Setup(r => r.ReviewAsync(It.IsAny<Draft>(), It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
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
                        Decisions = new[]
                        {
                            new DescriptorLifecycleDecision
                            {
                                Transition = new DescriptorLifecycleTransition
                                {
                                    Subject = new DescriptorRef("ns", "desc-002"),
                                    Operation = DescriptorLifecycleOperation.Activate
                                },
                                Decision = DescriptorLifecycleDecisionKind.Blocked,
                                Findings = Array.Empty<DescriptorLifecycleFinding>()
                            }
                        },
                        MaxDecision = DescriptorLifecycleDecisionKind.Blocked,
                        PackageFindings = Array.Empty<DescriptorLifecycleFinding>()
                    },
                    Diagnostics = Array.Empty<DescriptorDraftDiagnostic>(),
                    IsActivationEligible = false // Blocked by denied-kind governance
                });

        SetupPackageBuilder();

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = ["Capability"]
        };
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.ReviewDescriptorDraft);
        var result = await service.ReviewDescriptorDraftAsync(context, draft.DraftId);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        // After projection, the denied-kind governance blocker is filtered out,
        // so IsActivationEligible must be re-derived as true
        result.Value!.IsActivationEligible.Should().BeTrue(
            "denied-kind governance blocker must not influence IsActivationEligible after projection");
    }

    // ── Gap 2: Topology rebuild guard — don't invent topology for null source ──

    /// <summary>
    /// When the review result has TopologySnapshot = null (e.g. validation early-stop),
    /// the projector must NOT rebuild topology from the visible universe.
    /// A regression that removes the null guard would invent topology for failure states.
    /// </summary>
    [Fact]
    public async Task ReviewDescriptorDraft_NullTopology_DoesNotRebuildTopology()
    {
        var draft = CreateTestDraft(kind: DescriptorKind.Event);
        SetupDraftStoreGetReturns(draft);
        SetupCatalogGetAllReturns(
            CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event));

        DraftReviewServiceMock
            .Setup(r => r.ReviewAsync(It.IsAny<Draft>(), It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Draft d, IReadOnlyList<IDescriptor> _, CancellationToken __) =>
                new DescriptorDraftReviewResult
                {
                    DraftId = d.DraftId,
                    TenantId = d.TenantId,
                    ValidationResult = DescriptorDraftValidationResult.Success(),
                    ProposedInventory = Array.Empty<IDescriptor>(),
                    TopologySnapshot = null, // Validation early-stop — no topology
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

        SetupPackageBuilder();

        var service = CreateService(AgentToolAuthorizationOptions.DevelopmentDefaults);

        var context = CreateContext(AgentToolName.ReviewDescriptorDraft);
        var result = await service.ReviewDescriptorDraftAsync(context, draft.DraftId);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        // TopologyBuilder.Build must NOT be called when source topology is null
        TopologyBuilderMock.Verify(
            b => b.Build(It.IsAny<IReadOnlyList<IDescriptor>>()),
            Times.Never,
            "topology must not be rebuilt when source.TopologySnapshot is null");
    }

    // ── Gap 3: CompareDescriptorDraft with denied-kind active descriptor ──

    /// <summary>
    /// When the active descriptor for a draft's DescriptorId is of a denied kind,
    /// CompareDescriptorDraft must null out CurrentActiveDescriptor to prevent
    /// leaking denied descriptor data. A regression that removes the
    /// scope.IsVisible(currentActive.Kind) check would fail this test.
    /// </summary>
    [Fact]
    public async Task CompareDescriptorDraft_DeniedKindActive_NullsCurrentActiveDescriptor()
    {
        var draft = CreateTestDraft(
            kind: DescriptorKind.Event,
            descriptorId: "ns.desc-002");

        SetupDraftStoreGetReturns(draft);

        // Catalog returns a Capability descriptor for the draft's DescriptorId
        var deniedActive = CreateTestDescriptor("ns", "desc-002", DescriptorKind.Capability);
        DescriptorCatalogMock.Setup(c => c.Get("ns.desc-002")).Returns(deniedActive);
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns(new[] { deniedActive });

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = ["Capability"]
        };
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.CompareDescriptorDraft);
        var result = await service.CompareDescriptorDraftAsync(context, draft.DraftId);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.CurrentActiveDescriptor.Should().BeNull(
            "denied-kind active descriptor must be masked in comparison result");
    }

    // ── Gap 4: MaxCompatibilityLevel when all findings filtered ──

    /// <summary>
    /// When all compatibility findings reference denied-kind descriptors and are
    /// filtered out, MaxLevel must be Compatible (not Unsupported).
    /// A regression that uses All() on empty findings (which returns true)
    /// would produce Unsupported instead of Compatible.
    /// </summary>
    [Fact]
    public async Task ReviewDescriptorDraft_AllCompatibilityFindingsDenied_MaxLevelIsCompatible()
    {
        var draft = CreateTestDraft(kind: DescriptorKind.Event);
        SetupDraftStoreGetReturns(draft);
        SetupCatalogGetAllReturns(
            CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event),
            CreateTestDescriptor("ns", "desc-002", DescriptorKind.Capability));

        DraftReviewServiceMock
            .Setup(r => r.ReviewAsync(It.IsAny<Draft>(), It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
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
                        Findings = new[]
                        {
                            new DescriptorCompatibilityFinding
                            {
                                Subject = new DescriptorRef("ns", "desc-002"),
                                ChangeKind = DescriptorChangeKind.Updated,
                                Level = DescriptorCompatibilityLevel.Breaking,
                                Kind = DescriptorCompatibilityFindingKind.Structural,
                                RuleId = "COMPAT_001",
                                Message = "Breaking incompatibility"
                            }
                        },
                        MaxLevel = DescriptorCompatibilityLevel.Breaking,
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

        SetupPackageBuilder();

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = ["Capability"]
        };
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.ReviewDescriptorDraft);
        var result = await service.ReviewDescriptorDraftAsync(context, draft.DraftId);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        // After filtering all denied-kind findings, compatibility must show no incompatibilities
        result.Value!.CompatibilitySummary.IsCompatible.Should().BeTrue(
            "empty compatibility findings after filtering must default to Compatible");
        result.Value!.CompatibilitySummary.IncompatibilityCount.Should().Be(0,
            "denied-kind compatibility findings must be filtered out");
    }

    // ── Gap 5: Context pack builders receive only visible descriptors ──

    /// <summary>
    /// BuildMetadataContextPackAsync must pass only visible descriptors to the
    /// context pack builder. A regression that passes the full catalog would
    /// leak denied descriptor data through the context pack.
    /// </summary>
    [Fact]
    public async Task BuildMetadataContextPack_PassesOnlyVisibleDescriptors_ToBuilder()
    {
        SetupCatalogGetAllReturns(
            CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event),
            CreateTestDescriptor("ns", "desc-002", DescriptorKind.Capability));

        SetupTopologySnapshot();

        // Capture the descriptors argument passed to ContextPackBuilder.Build
        IReadOnlyList<IDescriptor>? capturedDescriptors = null;
        ContextPackBuilderMock
            .Setup(b => b.Build(It.IsAny<MetadataContextPackRequest>(), It.IsAny<DescriptorTopologySnapshot>(), It.IsAny<IReadOnlyList<IDescriptor>>()))
            .Callback<MetadataContextPackRequest, DescriptorTopologySnapshot, IReadOnlyList<IDescriptor>>((_, _, descriptors) =>
                capturedDescriptors = descriptors)
            .Returns(new MetadataContextPack
            {
                Request = new MetadataContextPackRequest
                {
                    Scope = MetadataContextPackScope.FocusOnly,
                    FocusDescriptors = Array.Empty<DescriptorRef>()
                },
                Descriptors = Array.Empty<MetadataContextPackDescriptorEntry>(),
                Relationships = Array.Empty<MetadataContextPackRelationshipEntry>(),
                Summary = new MetadataContextPackSummary
                {
                    TotalDescriptorCount = 0,
                    DescriptorCountsByKind = new Dictionary<DescriptorKind, int>(),
                    TotalRelationshipCount = 0,
                    RelationshipCountsByKind = new Dictionary<RelationshipKind, int>(),
                    FocusRefs = Array.Empty<DescriptorRef>(),
                    WasTruncated = false,
                    TruncatedAtCount = null,
                    TraversalDepthReached = 0
                },
                Diagnostics = Array.Empty<MetadataContextPackDiagnostic>()
            });

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = ["Capability"]
        };
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.BuildMetadataContextPack);
        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.FocusOnly,
            FocusDescriptors = Array.Empty<DescriptorRef>()
        };

        var result = await service.BuildMetadataContextPackAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        capturedDescriptors.Should().NotBeNull();
        capturedDescriptors!.Should().OnlyContain(d => d.Kind != DescriptorKind.Capability,
            "denied kinds must not be passed to ContextPackBuilder.Build");
    }

    /// <summary>
    /// BuildRuntimeScenarioContextPackAsync must also pass only visible descriptors.
    /// Same invariant as BuildMetadataContextPack but for the runtime scenario path.
    /// </summary>
    [Fact]
    public async Task BuildRuntimeScenarioContextPack_PassesOnlyVisibleDescriptors_ToBuilder()
    {
        SetupCatalogGetAllReturns(
            CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event),
            CreateTestDescriptor("ns", "desc-002", DescriptorKind.Capability));

        SetupTopologySnapshot();

        IReadOnlyList<IDescriptor>? capturedDescriptors = null;
        ContextPackBuilderMock
            .Setup(b => b.Build(It.IsAny<MetadataContextPackRequest>(), It.IsAny<DescriptorTopologySnapshot>(), It.IsAny<IReadOnlyList<IDescriptor>>()))
            .Callback<MetadataContextPackRequest, DescriptorTopologySnapshot, IReadOnlyList<IDescriptor>>((_, _, descriptors) =>
                capturedDescriptors = descriptors)
            .Returns(new MetadataContextPack
            {
                Request = new MetadataContextPackRequest
                {
                    Scope = MetadataContextPackScope.RuntimeScenario,
                    FocusDescriptors = Array.Empty<DescriptorRef>()
                },
                Descriptors = Array.Empty<MetadataContextPackDescriptorEntry>(),
                Relationships = Array.Empty<MetadataContextPackRelationshipEntry>(),
                Summary = new MetadataContextPackSummary
                {
                    TotalDescriptorCount = 0,
                    DescriptorCountsByKind = new Dictionary<DescriptorKind, int>(),
                    TotalRelationshipCount = 0,
                    RelationshipCountsByKind = new Dictionary<RelationshipKind, int>(),
                    FocusRefs = Array.Empty<DescriptorRef>(),
                    WasTruncated = false,
                    TruncatedAtCount = null,
                    TraversalDepthReached = 0
                },
                Diagnostics = Array.Empty<MetadataContextPackDiagnostic>()
            });

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = ["Capability"]
        };
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.BuildRuntimeScenarioContextPack);
        var request = new MetadataContextPackRequest
        {
            Scope = MetadataContextPackScope.RuntimeScenario,
            FocusDescriptors = Array.Empty<DescriptorRef>()
        };

        var result = await service.BuildRuntimeScenarioContextPackAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        capturedDescriptors.Should().NotBeNull();
        capturedDescriptors!.Should().OnlyContain(d => d.Kind != DescriptorKind.Capability,
            "denied kinds must not be passed to ContextPackBuilder.Build");
    }

    // ── Gap 6: Topology builder receives only visible descriptors ──

    /// <summary>
    /// GetTopologySummaryAsync must build topology from visible descriptors only.
    /// A regression that passes the full catalog to TopologyBuilder.Build would
    /// include denied-kind nodes and edges in the topology.
    /// </summary>
    [Fact]
    public async Task GetTopologySummary_BuildsTopologyFromVisibleDescriptorsOnly()
    {
        SetupCatalogGetAllReturns(
            CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event),
            CreateTestDescriptor("ns", "desc-002", DescriptorKind.Capability));

        // Capture the descriptors argument passed to TopologyBuilder.Build
        // Note: BuildVisible calls TopologyBuilder.Build internally
        IReadOnlyList<IDescriptor>? capturedDescriptors = null;
        TopologyBuilderMock
            .Setup(b => b.Build(It.IsAny<IReadOnlyList<IDescriptor>>()))
            .Callback<IReadOnlyList<IDescriptor>>(descriptors => capturedDescriptors = descriptors)
            .Returns(new DescriptorTopologySnapshot(
                new Dictionary<DescriptorRef, DescriptorNode>(),
                new List<DescriptorEdge>(),
                new DescriptorTopologyDiagnostics { All = Array.Empty<DescriptorTopologyDiagnostic>() },
                new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>(),
                new Dictionary<(DescriptorIdentity, int), List<(DescriptorRef, DescriptorEdge)>>(),
                new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>(),
                DateTimeOffset.UtcNow));

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = ["Capability"]
        };
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.GetTopologySummary);
        var result = await service.GetTopologySummaryAsync(context);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        capturedDescriptors.Should().NotBeNull();
        capturedDescriptors!.Should().OnlyContain(d => d.Kind != DescriptorKind.Capability,
            "denied kinds must not be passed to TopologyBuilder.Build");
    }

    // ── Gap 7: Evidence MaxImpactSeverity recomputation ──

    /// <summary>
    /// When evidence contains findings with Critical severity from a denied subject
    /// and Low severity from a visible subject, MaxImpactSeverity must be Low
    /// after projection, not Critical. A regression that copies source
    /// MaxImpactSeverity would fail this test.
    /// </summary>
    [Fact]
    public async Task BuildPackageEvidencePreview_RecalculatesMaxImpactSeverity_AfterFiltering()
    {
        var draft = CreateTestDraft(kind: DescriptorKind.Event);
        SetupDraftStoreGetReturns(draft);
        SetupCatalogGetAllReturns(
            CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event),
            CreateTestDescriptor("ns", "desc-002", DescriptorKind.Capability));

        DraftReviewServiceMock
            .Setup(r => r.ReviewAsync(It.IsAny<Draft>(), It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
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
                        MaxSeverity = DescriptorImpactSeverity.Critical,
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

        // Package builder returns evidence with findings from both visible and denied subjects
        PackageBuilderMock.Setup(b => b.Build(It.IsAny<DescriptorPackageBuildRequest>()))
            .Returns((DescriptorPackageBuildRequest req) =>
            {
                var findings = new List<EvidenceFinding>
                {
                    new()
                    {
                        Source = "impact",
                        Code = new DiagnosticCode("IMP_001"),
                        Severity = SeverityLevel.Error,
                        Subject = new DescriptorRef("ns", "desc-002"), // denied kind
                        Message = "Critical impact on denied descriptor"
                    },
                    new()
                    {
                        Source = "impact",
                        Code = new DiagnosticCode("IMP_002"),
                        Severity = SeverityLevel.Info,
                        Subject = new DescriptorRef("ns", "desc-001"), // visible kind
                        Message = "Low impact on visible descriptor"
                    }
                };

                return new DescriptorPackage
                {
                    Manifest = new DescriptorManifest
                    {
                        PackageId = "pkg-001",
                        PackageVersion = "1",
                        DescriptorEntries = Array.Empty<DescriptorManifestEntry>()
                    },
                    Snapshot = new DescriptorSnapshot(),
                    Evidence = new DescriptorPackageEvidence
                    {
                        NormalizedFindings = findings.AsReadOnly(),
                        MaxImpactSeverity = DescriptorImpactSeverity.Critical, // from full inventory
                        BreakingFindingCount = 0,
                        SecuritySensitiveFindingCount = 0,
                        UnsupportedFindingCount = 0,
                        RequiresReview = false,
                        PackageFindingCount = findings.Count
                    }
                };
            });

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = ["Capability"]
        };
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.BuildPackageEvidencePreview);
        var result = await service.BuildPackageEvidencePreviewAsync(context, draft.DraftId);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        // After projection, the Critical finding from denied subject is filtered out,
        // so MaxImpactSeverity must reflect only the visible Info finding
        result.Value!.Evidence.MaxImpactSeverity.Should().Be(DescriptorImpactSeverity.Info,
            "MaxImpactSeverity must be recalculated from visible findings only, not copied from source");
    }

    // ── Gap 8: Evidence MaxCompatibilityLevel recomputation ──

    /// <summary>
    /// When evidence contains compatibility findings with Breaking level from a
    /// denied subject and Compatible from a visible subject, MaxCompatibilityLevel
    /// must reflect only the visible finding after projection.
    /// </summary>
    [Fact]
    public async Task BuildPackageEvidencePreview_RecalculatesMaxCompatibilityLevel_AfterFiltering()
    {
        var draft = CreateTestDraft(kind: DescriptorKind.Event);
        SetupDraftStoreGetReturns(draft);
        SetupCatalogGetAllReturns(
            CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event),
            CreateTestDescriptor("ns", "desc-002", DescriptorKind.Capability));

        DraftReviewServiceMock
            .Setup(r => r.ReviewAsync(It.IsAny<Draft>(), It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
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

        PackageBuilderMock.Setup(b => b.Build(It.IsAny<DescriptorPackageBuildRequest>()))
            .Returns((DescriptorPackageBuildRequest req) =>
            {
                var findings = new List<EvidenceFinding>
                {
                    new()
                    {
                        Source = "compatibility",
                        Code = new DiagnosticCode("COMPAT_001"),
                        Severity = SeverityLevel.Error,
                        Subject = new DescriptorRef("ns", "desc-002"), // denied kind
                        Message = "Breaking compatibility with denied descriptor"
                    },
                    new()
                    {
                        Source = "compatibility",
                        Code = new DiagnosticCode("COMPAT_002"),
                        Severity = SeverityLevel.Info,
                        Subject = new DescriptorRef("ns", "desc-001"), // visible kind
                        Message = "Compatible with visible descriptor"
                    }
                };

                return new DescriptorPackage
                {
                    Manifest = new DescriptorManifest
                    {
                        PackageId = "pkg-001",
                        PackageVersion = "1",
                        DescriptorEntries = Array.Empty<DescriptorManifestEntry>()
                    },
                    Snapshot = new DescriptorSnapshot(),
                    Evidence = new DescriptorPackageEvidence
                    {
                        NormalizedFindings = findings.AsReadOnly(),
                        MaxCompatibilityLevel = DescriptorCompatibilityLevel.Breaking, // from full inventory
                        BreakingFindingCount = 1,
                        SecuritySensitiveFindingCount = 0,
                        UnsupportedFindingCount = 0,
                        RequiresReview = true,
                        PackageFindingCount = findings.Count
                    }
                };
            });

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = ["Capability"]
        };
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.BuildPackageEvidencePreview);
        var result = await service.BuildPackageEvidencePreviewAsync(context, draft.DraftId);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        // After projection, the Breaking finding from denied subject is filtered out,
        // so MaxCompatibilityLevel must reflect only the visible Compatible finding
        result.Value!.Evidence.MaxCompatibilityLevel.Should().Be(DescriptorCompatibilityLevel.Compatible,
            "MaxCompatibilityLevel must be recalculated from visible findings only");
    }

    // ── Gap 9: Evidence safe defaults for uncalculable fields ──

    /// <summary>
    /// When evidence has TopologyNodeCount/EdgeCount/ImpactPathCount from the full
    /// inventory, projection must set them to safe defaults (0) rather than copying
    /// from source. These fields cannot be reliably recomputed from flat findings.
    /// </summary>
    [Fact]
    public async Task BuildPackageEvidencePreview_SetsUncalculableFields_ToSafeDefaults()
    {
        var draft = CreateTestDraft(kind: DescriptorKind.Event);
        SetupDraftStoreGetReturns(draft);
        SetupCatalogGetAllReturns(
            CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event),
            CreateTestDescriptor("ns", "desc-002", DescriptorKind.Capability));

        DraftReviewServiceMock
            .Setup(r => r.ReviewAsync(It.IsAny<Draft>(), It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
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

        // Package builder returns evidence with non-zero topology/impact counts
        PackageBuilderMock.Setup(b => b.Build(It.IsAny<DescriptorPackageBuildRequest>()))
            .Returns((DescriptorPackageBuildRequest _) =>
                new DescriptorPackage
                {
                    Manifest = new DescriptorManifest
                    {
                        PackageId = "pkg-001",
                        PackageVersion = "1",
                        DescriptorEntries = Array.Empty<DescriptorManifestEntry>()
                    },
                    Snapshot = new DescriptorSnapshot(),
                    Evidence = new DescriptorPackageEvidence
                    {
                        NormalizedFindings = Array.Empty<EvidenceFinding>(),
                        TopologyNodeCount = 100,  // From full inventory
                        TopologyEdgeCount = 50,   // From full inventory
                        ImpactPathCount = 25,     // From full inventory
                        MaxImpactSeverity = DescriptorImpactSeverity.Low,
                        BreakingFindingCount = 0,
                        SecuritySensitiveFindingCount = 0,
                        UnsupportedFindingCount = 0,
                        RequiresReview = false,
                        PackageFindingCount = 0
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
        // These fields cannot be reliably recomputed from flat findings,
        // so they must be set to safe defaults (0), not copied from source
        result.Value!.Evidence.TopologyNodeCount.Should().Be(0,
            "TopologyNodeCount must use safe default, not copy from full-inventory source");
        result.Value!.Evidence.TopologyEdgeCount.Should().Be(0,
            "TopologyEdgeCount must use safe default, not copy from full-inventory source");
        result.Value!.Evidence.ImpactPathCount.Should().Be(0,
            "ImpactPathCount must use safe default, not copy from full-inventory source");
    }

    // ── Gap 10: Validation early-stop doesn't produce misleading Success ──

    /// <summary>
    /// When ReviewAsync returns a ValidationResult that is not valid (early-stop),
    /// BuildPackageEvidencePreviewAsync must return Failed, not fall through to
    /// produce a misleading Success with stale data.
    /// </summary>
    [Fact]
    public async Task ReviewDescriptorDraft_ValidationEarlyStop_DoesNotProduceMisleadingSuccess()
    {
        var draft = CreateTestDraft(kind: DescriptorKind.Event);
        SetupDraftStoreGetReturns(draft);
        SetupCatalogGetAllReturns(
            CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event));

        DraftReviewServiceMock
            .Setup(r => r.ReviewAsync(It.IsAny<Draft>(), It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Draft d, IReadOnlyList<IDescriptor> _, CancellationToken __) =>
                new DescriptorDraftReviewResult
                {
                    DraftId = d.DraftId,
                    TenantId = d.TenantId,
                    ValidationResult = DescriptorDraftValidationResult.Failure(
                        new DescriptorDraftDiagnostic
                        {
                            Code = new DiagnosticCode("VALIDATION_FAILED"),
                            Severity = SeverityLevel.Error,
                            Message = "Draft validation failed"
                        }),
                    ProposedInventory = null, // Not materialized
                    ImpactAnalysisResult = null,
                    CompatibilityResult = null,
                    GovernanceDecision = null,
                    Diagnostics = Array.Empty<DescriptorDraftDiagnostic>(),
                    IsActivationEligible = true // Would be true if validation passed
                });

        var service = CreateService(AgentToolAuthorizationOptions.DevelopmentDefaults);

        var context = CreateContext(AgentToolName.BuildPackageEvidencePreview);
        var result = await service.BuildPackageEvidencePreviewAsync(context, draft.DraftId);

        // Must not return Success with stale/empty evidence
        result.Status.Should().NotBe(AgentToolResultStatus.Success,
            "validation early-stop must not produce misleading Success");
    }

    // ── Gap 11: PreviewDescriptorPackage uses visible inventory for materialization ──

    /// <summary>
    /// PreviewDescriptorPackageAsync must pass only visible descriptors to the
    /// materializer. A regression that passes the full catalog would allow denied
    /// descriptors to influence materialization success/failure, diagnostics,
    /// replacement logic, and duplicate/version conflict detection.
    /// </summary>
    [Fact]
    public async Task PreviewDescriptorPackage_UsesVisibleInventoryForMaterialization()
    {
        var draft = CreateTestDraft(kind: DescriptorKind.Event);
        SetupDraftStoreGetReturns(draft);
        SetupCatalogGetAllReturns(
            CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event),
            CreateTestDescriptor("ns", "desc-002", DescriptorKind.Capability));

        // Capture the inventory argument passed to Materialize
        IReadOnlyList<IDescriptor>? capturedInventory = null;
        DraftMaterializerMock
            .Setup(m => m.Materialize(It.IsAny<Draft>(), It.IsAny<IReadOnlyList<IDescriptor>>()))
            .Callback<Draft, IReadOnlyList<IDescriptor>>((_, inventory) =>
                capturedInventory = inventory)
            .Returns(DraftAbstractions.DescriptorDraftMaterializationResult.Success(
                new List<IDescriptor> { CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event) }.AsReadOnly()));

        SetupPackageBuilder();

        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = ["Capability"]
        };
        var service = CreateService(options);

        var context = CreateContext(AgentToolName.PreviewDescriptorPackage);
        var result = await service.PreviewDescriptorPackageAsync(context, draft.DraftId);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        capturedInventory.Should().NotBeNull();
        capturedInventory!.Should().OnlyContain(d => d.Kind != DescriptorKind.Capability,
            "materializer must receive only visible descriptors, not the full catalog");
    }

    // ── Gap 12: AgentReviewResultDtoProjection — denied kinds don't leak into projected DTO ──

    /// <summary>
    /// When a DescriptorDraftReviewResult contains denied-kind descriptors in
    /// ProposedInventory, TopologySnapshot, and ImpactAnalysisResult, the
    /// projection must NOT expose them in the summary DTO fields. This ensures
    /// the projection does not re-introduce hidden-universe derived values.
    /// </summary>
    [Fact]
    public void AgentReviewResultDtoProjection_DeniedKinds_DoNot_Appear_In_ProjectedSummary()
    {
        const DescriptorKind deniedKind = DescriptorKind.Schema;
        const string deniedNs = "ns";
        const string deniedId = "schema-desc-001";

        // ── Descriptors for ProposedInventory ──
        var visibleDescriptor = CreateTestDescriptor("ns", "desc-001", DescriptorKind.Event);
        var deniedDescriptor = CreateTestDescriptor(deniedNs, deniedId, deniedKind);

        // ── TopologySnapshot with nodes of both visible and denied kinds ──
        var visibleNode = new DescriptorNode
        {
            Ref = new DescriptorRef("ns", "desc-001"),
            Kind = DescriptorKind.Event,
            Name = "VisibleNode",
            State = DescriptorState.Active,
            OutgoingEdgeIndices = new HashSet<int> { 0, 1 },
            IncomingEdgeIndices = new HashSet<int> { 0 }
        };
        var deniedNode = new DescriptorNode
        {
            Ref = new DescriptorRef(deniedNs, deniedId),
            Kind = deniedKind,
            Name = "DeniedNode",
            State = DescriptorState.Active,
            OutgoingEdgeIndices = new HashSet<int>(),
            IncomingEdgeIndices = new HashSet<int> { 1 }
        };

        var nodes = new Dictionary<DescriptorRef, DescriptorNode>
        {
            [visibleNode.Ref] = visibleNode,
            [deniedNode.Ref] = deniedNode
        };

        // Edges: visible→visible (should survive filtering), visible→denied (must be filtered out)
        var visibleToVisibleEdge = new DescriptorEdge
        {
            Index = 0,
            From = visibleNode.Ref,
            To = visibleNode.Ref,
            Kind = RelationshipKind.DependsOn,
            Strength = RelationshipStrength.Strong,
            IsRuntimeBinding = false
        };
        var visibleToDeniedEdge = new DescriptorEdge
        {
            Index = 1,
            From = visibleNode.Ref,
            To = deniedNode.Ref,
            Kind = RelationshipKind.Uses,
            Strength = RelationshipStrength.Strong,
            IsRuntimeBinding = false
        };
        var edges = new List<DescriptorEdge> { visibleToVisibleEdge, visibleToDeniedEdge };

        var topologySnapshot = new DescriptorTopologySnapshot(
            nodes,
            edges,
            new DescriptorTopologyDiagnostics { All = Array.Empty<DescriptorTopologyDiagnostic>() },
            new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>(),
            new Dictionary<(DescriptorIdentity, int), List<(DescriptorRef, DescriptorEdge)>>(),
            new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>(),
            DateTimeOffset.UtcNow);

        // ── ImpactAnalysisResult with affected descriptors of both kinds ──
        var visibleAffected = new AffectedDescriptor
        {
            Ref = new DescriptorRef("ns", "desc-001"),
            Kind = DescriptorKind.Event,
            Name = "VisibleAffected",
            Severity = DescriptorImpactSeverity.Low,
            RuntimeAreas = Array.Empty<DescriptorImpactRuntimeArea>(),
            Paths = Array.Empty<DescriptorImpactPath>()
        };
        var deniedAffected = new AffectedDescriptor
        {
            Ref = new DescriptorRef(deniedNs, deniedId),
            Kind = deniedKind,
            Name = "DeniedAffected",
            Severity = DescriptorImpactSeverity.High,
            RuntimeAreas = Array.Empty<DescriptorImpactRuntimeArea>(),
            Paths = Array.Empty<DescriptorImpactPath>()
        };

        var impactReport = new DescriptorImpactAnalysisReport
        {
            ChangeSet = new DescriptorChangeSet { Changes = Array.Empty<DescriptorChange>() },
            AffectedDescriptors = new[] { visibleAffected, deniedAffected },
            Paths = Array.Empty<DescriptorImpactPath>(),
            MaxSeverity = DescriptorImpactSeverity.High,
            Diagnostics = Array.Empty<DescriptorImpactDiagnostic>()
        };

        // ── Full review result ──
        var source = new DescriptorDraftReviewResult
        {
            DraftId = "draft-gap12-001",
            TenantId = TestTenantId,
            ValidationResult = DescriptorDraftValidationResult.Success(),
            ProposedInventory = new IDescriptor[] { visibleDescriptor, deniedDescriptor },
            TopologySnapshot = topologySnapshot,
            ImpactAnalysisResult = impactReport,
            CompatibilityResult = new DescriptorCompatibilityReport
            {
                ChangeSet = new DescriptorChangeSet { Changes = Array.Empty<DescriptorChange>() },
                ImpactReport = impactReport,
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
        };

        // ── Act: project to AgentReviewResultDto with denied kinds ──
        var deniedKinds = new HashSet<DescriptorKind> { deniedKind };
        var result = AgentReviewResultDtoProjection.Project(source, deniedKinds);

        // ── Assert: denied kinds must NOT appear in projected summaries ──
        result.ProposedInventorySummary.Should().NotBeNull();
        result.ProposedInventorySummary!.CountsByKind.Should().NotContainKey(deniedKind,
            "projection must not expose denied-kind counts in ProposedInventorySummary");

        result.TopologySummary.Should().NotBeNull();
        result.TopologySummary!.NodeCountsByKind.Should().NotContainKey(deniedKind,
            "projection must not expose denied-kind nodes in TopologySummary");
        // Edge counts must also be filtered: the visible→visible edge survives,
        // the visible→denied edge must be filtered out because denied nodes are invisible.
        result.TopologySummary.TotalEdgeCount.Should().Be(1,
            "only the visible→visible edge should survive, the visible→denied edge must be filtered");
        result.TopologySummary.EdgeCountsByKind.Should().ContainKey(RelationshipKind.DependsOn,
            "visible→visible edge kind must survive filtering");
        result.TopologySummary.EdgeCountsByKind.Should().NotContainKey(RelationshipKind.Uses,
            "visible→denied edge kind must not appear in filtered topology");

        result.ImpactAnalysisSummary.Should().NotBeNull();
        result.ImpactAnalysisSummary!.AffectedDescriptors.Should()
            .NotContain(r => r.Id == deniedId && r.Namespace == deniedNs,
                "projection must not expose denied-kind affected descriptor refs in ImpactAnalysisSummary");
    }

    // ── Helper overrides ──

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

    private new void SetupPackageBuilder()
    {
        PackageBuilderMock.Setup(b => b.Build(It.IsAny<DescriptorPackageBuildRequest>()))
            .Returns(new DescriptorPackage
            {
                    Manifest = new DescriptorManifest
                    {
                        PackageId = "pkg-001",
                        PackageVersion = "1",
                        DescriptorEntries = Array.Empty<DescriptorManifestEntry>()
                    },
                Snapshot = new DescriptorSnapshot(),
                Evidence = new DescriptorPackageEvidence()
            });
    }

    private void SetupTopologySnapshot()
    {
        var topology = new DescriptorTopologySnapshot(
            new Dictionary<DescriptorRef, DescriptorNode>(),
            new List<DescriptorEdge>(),
            new DescriptorTopologyDiagnostics { All = Array.Empty<DescriptorTopologyDiagnostic>() },
            new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>(),
            new Dictionary<(DescriptorIdentity, int), List<(DescriptorRef, DescriptorEdge)>>(),
            new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>(),
            DateTimeOffset.UtcNow);

        TopologyBuilderMock.Setup(b => b.Build(It.IsAny<IReadOnlyList<IDescriptor>>()))
            .Returns(topology);
    }
}
