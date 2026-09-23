using CrestCreates.Agent.ControlPlane;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.DescriptorDraft.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Localization.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;
using DraftCanonicalHashing = CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.ControlPlane.Tests;

public class DescriptorReviewReportBuilderTests
{
    private readonly DefaultDescriptorReviewMessageTemplateCatalog _templateCatalog = new();
    private readonly Mock<DraftCanonicalHashing.IDescriptorDraftReviewHashService> _reviewHashServiceMock = new();
    private readonly DefaultDescriptorReviewReportBuilder _builder;

    public DescriptorReviewReportBuilderTests()
    {
        _reviewHashServiceMock
            .Setup(x => x.ComputeSourceReviewHash(It.IsAny<DraftCanonicalHashing.DescriptorDraftReviewHashInput>()))
            .Returns(new CanonicalHash
            {
                Algorithm = "SHA-256",
                AlgorithmVersion = "v1",
                ArtifactKind = CanonicalHashArtifactNames.Descriptor,
                Scope = CanonicalHashScopeNames.InternalFull,
                Purpose = CanonicalHashPurposeNames.SourceBinding,
                ContractVersion = "v1",
                CanonicalShapeVersion = "v1",
                Value = "test-source-review-hash"
            });
        _builder = new DefaultDescriptorReviewReportBuilder(_templateCatalog, _reviewHashServiceMock.Object);
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    private static CanonicalHash CreateCanonicalHash(string value) => new()
    {
        Algorithm = "SHA-256",
        AlgorithmVersion = "v1",
        ArtifactKind = CanonicalHashArtifactNames.Descriptor,
        Scope = CanonicalHashScopeNames.InternalFull,
        Purpose = CanonicalHashPurposeNames.Contract,
        ContractVersion = "v1",
        CanonicalShapeVersion = "v1",
        Value = value
    };

    private static Draft CreateDraft(
        string draftId = "draft-001",
        string tenantId = "tenant-001",
        string descriptorId = "test.desc-001",
        string? proposedVersion = "1.0",
        DescriptorKind kind = DescriptorKind.Event,
        DraftAbstractions.DescriptorDraftOperation operation = DraftAbstractions.DescriptorDraftOperation.Create,
        DraftAbstractions.DescriptorDraftStatus status = DraftAbstractions.DescriptorDraftStatus.Created)
    {
        return new Draft
        {
            TenantId = tenantId,
            DraftId = draftId,
            DescriptorKind = kind,
            DescriptorId = descriptorId,
            Operation = operation,
            AuthorKind = DraftAbstractions.DescriptorDraftAuthorKind.Agent,
            AuthorId = "actor-001",
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Payload = new TestDraftPayload(kind, descriptorId, "TestDraft"),
            Status = status,
            ProposedVersion = proposedVersion,
        };
    }

    private static DraftAbstractions.DescriptorDraftReviewResult CreateReviewResult(
        string draftId = "draft-001",
        string tenantId = "tenant-001",
        DraftAbstractions.DescriptorDraftValidationResult? validationResult = null,
        bool isActivationEligible = true,
        IReadOnlyList<DraftAbstractions.DescriptorDraftDiagnostic>? diagnostics = null)
    {
        return new DraftAbstractions.DescriptorDraftReviewResult
        {
            DraftId = draftId,
            TenantId = tenantId,
            ValidationResult = validationResult ?? DraftAbstractions.DescriptorDraftValidationResult.Success(),
            Diagnostics = diagnostics ?? Array.Empty<DraftAbstractions.DescriptorDraftDiagnostic>(),
            IsActivationEligible = isActivationEligible,
        };
    }

    private static DraftAbstractions.DescriptorDraftDiagnostic CreateDiagnostic(
        DiagnosticCode code,
        SeverityLevel severity,
        string message = "Test diagnostic")
    {
        return new DraftAbstractions.DescriptorDraftDiagnostic
        {
            Code = code,
            Severity = severity,
            Message = message,
        };
    }

    private static DescriptorReviewReportBuildRequest CreateRequest(
        DraftAbstractions.DescriptorDraftReviewResult reviewResult,
        Draft draft,
        bool visibilityApplied = true)
    {
        return new DescriptorReviewReportBuildRequest
        {
            ReviewResult = reviewResult,
            Draft = draft,
            VisibilityApplied = visibilityApplied,
        };
    }

    [Fact]
    public void DescriptorGovernanceLocalization_Should_Preserve_ReasonCode()
    {
        var (english, chinese) = BuildLocalizedReports();

        GetItems(english).Select(item => item.ReasonCode).Should()
            .Equal(GetItems(chinese).Select(item => item.ReasonCode));
    }

    [Fact]
    public void DescriptorGovernanceLocalization_Should_Preserve_MessageTemplateId()
    {
        var (english, chinese) = BuildLocalizedReports();

        GetItems(english).Select(item => item.MessageTemplateId).Should()
            .Equal(GetItems(chinese).Select(item => item.MessageTemplateId));
    }

    [Fact]
    public void DescriptorGovernanceLocalization_Should_Preserve_Parameters()
    {
        var (english, chinese) = BuildLocalizedReports();

        GetItems(english).Select(item => item.Parameters).Should()
            .BeEquivalentTo(GetItems(chinese).Select(item => item.Parameters));
    }

    [Fact]
    public void DescriptorGovernanceLocalization_Should_Not_Change_CanonicalHash_OrDecision()
    {
        var (english, chinese) = BuildLocalizedReports();

        GetMessages(english).Should().NotEqual(GetMessages(chinese));
        english.Should().BeEquivalentTo(
            chinese,
            options => options.Excluding(context =>
                context.Path.EndsWith(".Message", StringComparison.Ordinal)),
            "culture may change only the already-projected presentation Message");
    }

    private (DescriptorReviewReportDto English, DescriptorReviewReportDto Chinese) BuildLocalizedReports()
    {
        var request = CreateRichRequestForDeniedKindTests();
        var transition = new DescriptorLifecycleTransition
        {
            Subject = new DescriptorRef("test", "desc-001", 1),
            Operation = DescriptorLifecycleOperation.SubmitForReview,
        };
        var governanceReport = new DescriptorLifecycleGovernanceReport
        {
            Decisions =
            [
                new DescriptorLifecycleDecision
                {
                    Transition = transition,
                    Decision = DescriptorLifecycleDecisionKind.ReviewRequired,
                    Findings = Array.Empty<DescriptorLifecycleFinding>()
                }
            ],
            MaxDecision = DescriptorLifecycleDecisionKind.ReviewRequired,
            PackageFindings = Array.Empty<DescriptorLifecycleFinding>()
        };
        request = request with
        {
            ReviewResult = request.ReviewResult with { GovernanceDecision = governanceReport }
        };
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero));
        var englishBuilder = new DefaultDescriptorReviewReportBuilder(
            new DefaultDescriptorReviewMessageTemplateCatalog(
                new KeyReturningLocalizationService("en"),
                NullLogger<DefaultDescriptorReviewMessageTemplateCatalog>.Instance),
            _reviewHashServiceMock.Object,
            clock);
        var chineseBuilder = new DefaultDescriptorReviewReportBuilder(
            new DefaultDescriptorReviewMessageTemplateCatalog(
                new KeyReturningLocalizationService("zh-CN"),
                NullLogger<DefaultDescriptorReviewMessageTemplateCatalog>.Instance),
            _reviewHashServiceMock.Object,
            clock);

        var english = englishBuilder.Build(request);
        var chinese = chineseBuilder.Build(request);

        return (english, chinese);
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 1: Build with VisibilityApplied=false throws
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_WithVisibilityAppliedFalse_ThrowsInvalidOperationException()
    {
        var draft = CreateDraft();
        var reviewResult = CreateReviewResult();
        var request = CreateRequest(reviewResult, draft, visibilityApplied: false);

        var act = () => _builder.Build(request);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*visibility*not been applied*");
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 2: Activation eligible draft returns eligible section
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_ActivationEligibleDraft_ReturnsActivationEligibleSection()
    {
        var draft = CreateDraft();
        var reviewResult = CreateReviewResult(isActivationEligible: true);
        var request = CreateRequest(reviewResult, draft);

        var report = _builder.Build(request);

        var section = report.ActivationEligibilitySection;
        section.Should().NotBeNull();
        section.IsEmpty.Should().BeFalse();
        section.Items.Should().Contain(i => i.ReasonCode == "activation_eligible");
        section.OverallSeverity.Should().Be(SeverityLevel.Info);
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 3: Blocked draft returns activation eligible section with blockers
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_BlockedDraft_ReturnsActivationEligibleSectionWithBlockers()
    {
        var draft = CreateDraft();
        var blocker = CreateDiagnostic(new DiagnosticCode("DRAFT_BLOCKER"), SeverityLevel.Blocker, "Blocked reason");
        var reviewResult = CreateReviewResult(
            isActivationEligible: false,
            diagnostics: new[] { blocker });
        var request = CreateRequest(reviewResult, draft);

        var report = _builder.Build(request);

        var section = report.ActivationEligibilitySection;
        section.IsEmpty.Should().BeFalse();
        section.OverallSeverity.Should().Be(SeverityLevel.Blocker);
        section.Items.Should().Contain(i => i.ReasonCode == "activation_blocked");
        // Parameters should contain blocking reasons
        var blockedItem = section.Items.First(i => i.ReasonCode == "activation_blocked");
        blockedItem.Parameters["BlockingReasons"].Should().Contain("DRAFT_BLOCKER");
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 4: All 13 sections present
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_AllSectionsPresent()
    {
        var draft = CreateDraft();
        var reviewResult = CreateReviewResult();
        var request = CreateRequest(reviewResult, draft);

        var report = _builder.Build(request);

        report.SummarySection.Should().NotBeNull();
        report.DraftIdentitySection.Should().NotBeNull();
        report.ProposedChangesSection.Should().NotBeNull();
        report.ImpactAnalysisSection.Should().NotBeNull();
        report.DependencySummarySection.Should().NotBeNull();
        report.CompatibilitySection.Should().NotBeNull();
        report.GovernanceSection.Should().NotBeNull();
        report.RequiredHumanReviewSection.Should().NotBeNull();
        report.ActivationEligibilitySection.Should().NotBeNull();
        report.DiagnosticsSection.Should().NotBeNull();
        report.RecommendationsSection.Should().NotBeNull();
        report.PackagePreviewSection.Should().NotBeNull();
        report.StableHashesSection.Should().NotBeNull();
        report.Recommendations.Should().NotBeNull();
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 5: Sections have deterministic order
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_SectionsHaveDeterministicOrder()
    {
        var draft = CreateDraft();
        var reviewResult = CreateReviewResult();
        var request = CreateRequest(reviewResult, draft);

        var report = _builder.Build(request);

        var sections = new[]
        {
            report.SummarySection,
            report.DraftIdentitySection,
            report.ProposedChangesSection,
            report.ImpactAnalysisSection,
            report.DependencySummarySection,
            report.CompatibilitySection,
            report.GovernanceSection,
            report.RequiredHumanReviewSection,
            report.ActivationEligibilitySection,
            report.DiagnosticsSection,
            report.RecommendationsSection,
            report.PackagePreviewSection,
            report.StableHashesSection,
        };

        for (int i = 0; i < sections.Length; i++)
        {
            sections[i].Order.Should().Be(i + 1, $"section {sections[i].Title} should have order {i + 1}");
            sections[i].Kind.Should().Be((DescriptorReviewReportSectionKind)(i + 1),
                $"section {sections[i].Title} kind should match its order");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 6: Diagnostics grouped by severity
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_DiagnosticsGroupedBySeverity()
    {
        var draft = CreateDraft();
        var infoDiag = CreateDiagnostic(new DiagnosticCode("INFO_001"), SeverityLevel.Info, "Info message");
        var warnDiag = CreateDiagnostic(new DiagnosticCode("WARN_001"), SeverityLevel.Warning, "Warning message");
        var errorDiag = CreateDiagnostic(new DiagnosticCode("ERR_001"), SeverityLevel.Error, "Error message");
        var blockerDiag = CreateDiagnostic(new DiagnosticCode("BLOCK_001"), SeverityLevel.Blocker, "Blocker message");

        var validationResult = DraftAbstractions.DescriptorDraftValidationResult.Failure(
            infoDiag, warnDiag, errorDiag, blockerDiag);
        var reviewResult = CreateReviewResult(
            validationResult: validationResult,
            isActivationEligible: false,
            diagnostics: new[] { infoDiag, warnDiag });
        var request = CreateRequest(reviewResult, draft);

        var report = _builder.Build(request);

        var section = report.DiagnosticsSection;
        section.IsEmpty.Should().BeFalse();

        // All diagnostics should be present
        section.Items.Should().HaveCountGreaterThanOrEqualTo(4);
        section.Items.Should().Contain(i => i.ReasonCode == "INFO_001" && i.Severity == SeverityLevel.Info);
        section.Items.Should().Contain(i => i.ReasonCode == "WARN_001" && i.Severity == SeverityLevel.Warning);
        section.Items.Should().Contain(i => i.ReasonCode == "ERR_001" && i.Severity == SeverityLevel.Error);
        section.Items.Should().Contain(i => i.ReasonCode == "BLOCK_001" && i.Severity == SeverityLevel.Blocker);
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 7: Empty diagnostics returns empty diagnostics section
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_EmptyDiagnostics_ReturnsEmptyDiagnosticsSection()
    {
        var draft = CreateDraft();
        var reviewResult = CreateReviewResult(
            validationResult: DraftAbstractions.DescriptorDraftValidationResult.Success(),
            diagnostics: Array.Empty<DraftAbstractions.DescriptorDraftDiagnostic>());
        var request = CreateRequest(reviewResult, draft);

        var report = _builder.Build(request);

        // Diagnostics section: when no diagnostics exist, the items list is empty.
        // The section's IsEmpty flag is set when items.Count == 0.
        report.DiagnosticsSection.Items.Should().BeEmpty();
        report.DiagnosticsSection.IsEmpty.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 8: Stable hashes match source
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_StableHashesMatchSource()
    {
        var draft = CreateDraft();
        var stableHashes = new DescriptorStableHashes
        {
            ContractHash = new CanonicalHash
            {
                Value = "contract-hash-abc",
                Algorithm = "SHA-256",
                AlgorithmVersion = "sha256-pipe-delimited-v0",
                ArtifactKind = "Descriptor",
                DescriptorKind = "Schema",
                Scope = "InternalFull",
                Purpose = "Contract",
                ContractVersion = "0",
                CanonicalShapeVersion = "1"
            },
            DefinitionHash = new CanonicalHash
            {
                Value = "definition-hash-def",
                Algorithm = "SHA-256",
                AlgorithmVersion = "sha256-pipe-delimited-v0",
                ArtifactKind = "Descriptor",
                DescriptorKind = "Schema",
                Scope = "InternalFull",
                Purpose = "Definition",
                ContractVersion = "0",
                CanonicalShapeVersion = "1"
            },
            RuntimeHash = new CanonicalHash
            {
                Value = "runtime-hash-ghi",
                Algorithm = "SHA-256",
                AlgorithmVersion = "sha256-pipe-delimited-v0",
                ArtifactKind = "ReviewResult",
                DescriptorKind = null,
                Scope = "InternalFull",
                Purpose = "SourceBinding",
                ContractVersion = "0",
                CanonicalShapeVersion = "1"
            },
            BindingHash = new CanonicalHash
            {
                Value = "binding-hash-jkl",
                Algorithm = "SHA-256",
                AlgorithmVersion = "sha256-pipe-delimited-v0",
                ArtifactKind = "ReviewResult",
                DescriptorKind = null,
                Scope = "InternalFull",
                Purpose = "SourceBinding",
                ContractVersion = "0",
                CanonicalShapeVersion = "1"
            }
        };
        var reviewResult = CreateReviewResult() with { StableHashes = stableHashes };
        var request = CreateRequest(reviewResult, draft);

        var report = _builder.Build(request);

        var section = report.StableHashesSection;
        section.IsEmpty.Should().BeFalse();
        section.Items.Should().HaveCount(1);
        var item = section.Items[0];
        item.Parameters["ContractHash"].Should().Be("contract-hash-abc");
        item.Parameters["DefinitionHash"].Should().Be("definition-hash-def");
        item.Parameters["RuntimeHash"].Should().Be("runtime-hash-ghi");
        item.Parameters["BindingHash"].Should().Be("binding-hash-jkl");
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 9: Package preview section reflects input
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_PackagePreviewSectionPresent()
    {
        var draft = CreateDraft();
        var packagePreview = new DraftAbstractions.DescriptorPackagePreview
        {
            PackageManifestHash = CreateCanonicalHash("mh-001"),
            PackageEvidenceHash = CreateCanonicalHash("eh-001"),
            PackageEvidenceEnvelopeHash = CreateCanonicalHash("env-001"),
            DescriptorIds = new[] { "desc-1", "desc-2", "desc-3" },
        };
        var reviewResult = CreateReviewResult() with { PackagePreview = packagePreview };
        var request = CreateRequest(reviewResult, draft);

        var report = _builder.Build(request);

        var section = report.PackagePreviewSection;
        section.IsEmpty.Should().BeFalse();
        section.Items.Should().HaveCount(1);
        var item = section.Items[0];
        item.Parameters["ManifestHash"].Should().Be("mh-001");
        item.Parameters["DescriptorCount"].Should().Be("3");
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 10: VisibilityApplied flag preserved (builder works when true)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_VisibilityAppliedTrue_BuildsSuccessfully()
    {
        var draft = CreateDraft();
        var reviewResult = CreateReviewResult();
        var request = CreateRequest(reviewResult, draft, visibilityApplied: true);

        var report = _builder.Build(request);

        report.Should().NotBeNull();
        report.ReportId.Should().NotBeNullOrEmpty();
        report.DraftId.Should().Be("draft-001");
        report.TenantId.Should().Be("tenant-001");
    }

    [Fact]
    public void CapturedSnapshot_SourceGeneratedRoundTrip_RendersSameCompleteRichReport()
    {
        var request = CreateRichRequestForDeniedKindTests();
        var diagnostic = CreateDiagnostic(new DiagnosticCode("DRAFT_BLOCKED"), SeverityLevel.Blocker, "Blocked by review");
        var descriptorOne = CreateDescriptor("first", "First descriptor", DescriptorKind.Event);
        var descriptorTwo = CreateDescriptor("second", "Second descriptor", DescriptorKind.Capability);
        var transition = new DescriptorLifecycleTransition
        {
            Subject = new DescriptorRef("test", "second", 2),
            Operation = DescriptorLifecycleOperation.SubmitForReview,
            FromState = DescriptorState.Draft,
            ToState = DescriptorState.Active,
            Reason = "explicit test transition",
        };
        var governance = new DescriptorLifecycleGovernanceReport
        {
            Decisions =
            [
                new DescriptorLifecycleDecision
                {
                    Transition = transition,
                    Decision = DescriptorLifecycleDecisionKind.Blocked,
                    Findings = [],
                }
            ],
            MaxDecision = DescriptorLifecycleDecisionKind.Blocked,
            PackageFindings =
            [
                new DescriptorLifecycleFinding
                {
                    Severity = SeverityLevel.Blocker,
                    Code = new DiagnosticCode("PKG_BLOCK"),
                    Message = "Blocked package descriptor",
                    Subject = new DescriptorRef("test", "pkg-desc"),
                }
            ],
        };
        var requestWithRichFacts = request with
        {
            ReviewResult = request.ReviewResult with
            {
                ValidationResult = DraftAbstractions.DescriptorDraftValidationResult.Failure(diagnostic),
                Diagnostics = [diagnostic],
                IsActivationEligible = false,
                MaterializationResult = new DraftAbstractions.DescriptorDraftMaterializationResult
                {
                    IsMaterialized = true,
                    ProposedInventory = [descriptorOne.Object, descriptorTwo.Object],
                    Diagnostics = [],
                },
                TopologySnapshot = CreateTopologySnapshot(),
                GovernanceDecision = governance,
            },
        };
        var fixedTime = new DateTimeOffset(2026, 9, 24, 3, 4, 5, TimeSpan.Zero);
        var builder = new DefaultDescriptorReviewReportBuilder(_templateCatalog, _reviewHashServiceMock.Object, new FixedTimeProvider(fixedTime));
        var captured = DescriptorReviewReportInputSnapshot.Capture(requestWithRichFacts);
        var typeInfo = DescriptorReviewReportInputSnapshotJsonSerializerContext.Default.DescriptorReviewReportInputSnapshot;
        var json = JsonSerializer.Serialize(captured, typeInfo);
        var restored = JsonSerializer.Deserialize(json, typeInfo);

        restored.Should().NotBeNull();
        restored!.Materialization!.ProposedDescriptors.Select(item => item.Id).Should().Equal("first", "second");
        restored.Topology!.NodeCount.Should().Be(2);
        restored.Impact!.AffectedDescriptors.Should().ContainSingle(item => item.Name == "ImpactedCapability");
        restored.CompatibilityFindings.Should().ContainSingle(item => item.SubjectId == "desc-compat-001");
        restored.GovernanceFirstDecision.Should().Be(DescriptorLifecycleDecisionKind.Blocked);
        restored.GovernanceFirstTransition.Should().BeEquivalentTo(transition);
        restored.GovernancePackageFindingSubjectIds.Should().Equal("pkg-desc");
        restored.PackagePreview!.DescriptorIds.Should().Equal("desc-pkg-1", "desc-pkg-2");
        restored.StableHashes!.ContractHash.Value.Should().Be("contract-hash-abc");
        restored.IsValid.Should().BeFalse();
        restored.IsActivationEligible.Should().BeFalse();
        restored.PackagePreview.EvidenceHash.Should().NotBeNull();

        var direct = builder.Build(requestWithRichFacts);
        var fromSnapshot = builder.Build(restored);
        fromSnapshot.Should().BeEquivalentTo(direct);
        var reorderedTopology = restored with
        {
            Topology = restored.Topology with
            {
                NodeCountsByKind = restored.Topology.NodeCountsByKind.Reverse().ToArray(),
                EdgeCountsByKind = restored.Topology.EdgeCountsByKind.Reverse().ToArray(),
            },
        };
        builder.Build(reorderedTopology).Should().BeEquivalentTo(fromSnapshot,
            "the original report sorted topology kind summaries regardless of input collection order");
        fromSnapshot.GeneratedAt.Should().Be(fixedTime);
        fromSnapshot.ProposedChangesSection.Items.Should().Contain(item => item.Parameters.GetValueOrDefault("DescriptorId") == "first");
        fromSnapshot.DependencySummarySection.Items.Single().Parameters["NodeCount"].Should().Be("2");
        fromSnapshot.ImpactAnalysisSection.Items.Should().Contain(item => item.Parameters.GetValueOrDefault("DescriptorName") == "ImpactedCapability");
        fromSnapshot.CompatibilitySection.Items.Should().Contain(item => item.Parameters.GetValueOrDefault("DescriptorId") == "desc-compat-001");
        fromSnapshot.GovernanceSection.Items.Should().Contain(item => item.Parameters.GetValueOrDefault("DescriptorId") == "pkg-desc");
        fromSnapshot.PackagePreviewSection.Items.Single().Parameters["ManifestHash"].Should().Be("mh-001");
    }

    [Fact]
    public void SnapshotCapture_CopiesMutableSourceCollections()
    {
        var diagnostic = CreateDiagnostic(new DiagnosticCode("ORIGINAL"), SeverityLevel.Warning, "original");
        var diagnostics = new List<DraftAbstractions.DescriptorDraftDiagnostic> { diagnostic };
        var inventory = new List<IDescriptor> { CreateDescriptor("before", "Before", DescriptorKind.Event).Object };
        var packageIds = new List<string> { "before-package" };
        var compatFindings = new List<DescriptorCompatibilityFinding>
        {
            new()
            {
                Subject = new DescriptorRef("test", "before-compat"),
                ChangeKind = DescriptorChangeKind.Updated,
                Level = DescriptorCompatibilityLevel.Risky,
                Kind = DescriptorCompatibilityFindingKind.Structural,
                RuleId = "RULE",
                Message = "before",
            }
        };
        var request = CreateRequest(CreateReviewResult(
            validationResult: new DraftAbstractions.DescriptorDraftValidationResult { IsValid = false, Diagnostics = diagnostics },
            diagnostics: diagnostics) with
        {
            MaterializationResult = new DraftAbstractions.DescriptorDraftMaterializationResult
            {
                IsMaterialized = true,
                ProposedInventory = inventory,
                Diagnostics = [],
            },
            CompatibilityResult = new DescriptorCompatibilityReport
            {
                ChangeSet = new DescriptorChangeSet { Changes = [] },
                ImpactReport = new DescriptorImpactAnalysisReport { ChangeSet = new DescriptorChangeSet { Changes = [] }, AffectedDescriptors = [], Paths = [], MaxSeverity = DescriptorImpactSeverity.None, Diagnostics = [] },
                Findings = compatFindings,
                MaxLevel = DescriptorCompatibilityLevel.Risky,
                Diagnostics = [],
            },
            PackagePreview = new DraftAbstractions.DescriptorPackagePreview { DescriptorIds = packageIds },
        }, CreateDraft());

        var snapshot = DescriptorReviewReportInputSnapshot.Capture(request);
        diagnostics[0] = CreateDiagnostic(new DiagnosticCode("MUTATED"), SeverityLevel.Error, "mutated");
        inventory.Clear();
        packageIds[0] = "mutated-package";
        compatFindings.Clear();

        snapshot.ValidationDiagnostics.Single().Code.ToString().Should().Be("ORIGINAL");
        snapshot.ReviewDiagnostics.Single().Message.Should().Be("original");
        snapshot.Materialization!.ProposedDescriptors.Single().Id.Should().Be("before");
        snapshot.CompatibilityFindings.Should().ContainSingle(item => item.SubjectId == "before-compat");
        snapshot.PackagePreview!.DescriptorIds.Should().Equal("before-package");
    }

    [Fact]
    public void SnapshotCapture_AllowsFailedReviewAndNullOptionalAnalyses()
    {
        var warning = CreateDiagnostic(new DiagnosticCode("FAILED_REVIEW"), SeverityLevel.Warning, "");
        var request = CreateRequest(CreateReviewResult(
            validationResult: DraftAbstractions.DescriptorDraftValidationResult.Failure(warning),
            isActivationEligible: false,
            diagnostics: [warning]), CreateDraft());

        var snapshot = DescriptorReviewReportInputSnapshot.Capture(request);
        var typeInfo = DescriptorReviewReportInputSnapshotJsonSerializerContext.Default.DescriptorReviewReportInputSnapshot;
        var json = JsonSerializer.Serialize(snapshot, typeInfo);
        var restored = JsonSerializer.Deserialize(json, typeInfo);
        restored.Should().NotBeNull();
        restored!.Materialization.Should().BeNull();
        restored.Impact.Should().BeNull();
        restored.CompatibilityFindings.Should().BeNull();
        restored.GovernanceMaxDecision.Should().BeNull();
        restored.PackagePreview.Should().BeNull();
        var report = _builder.Build(restored!);

        snapshot.IsValid.Should().BeFalse();
        snapshot.IsActivationEligible.Should().BeFalse();
        snapshot.Materialization.Should().BeNull();
        snapshot.Topology.Should().BeNull();
        snapshot.Impact.Should().BeNull();
        snapshot.CompatibilityFindings.Should().BeNull();
        snapshot.GovernanceMaxDecision.Should().BeNull();
        snapshot.PackagePreview.Should().BeNull();
        report.ActivationEligibilitySection.IsEmpty.Should().BeFalse();
        report.DiagnosticsSection.Items.Should().Contain(item => item.Message == "");
    }

    [Fact]
    public void Build_Snapshot_RejectsUnsupportedVersionAndMalformedStructure()
    {
        var snapshot = DescriptorReviewReportInputSnapshot.Capture(CreateRichRequestForDeniedKindTests());

        var unsupported = () => _builder.Build(snapshot with { Version = DescriptorReviewReportInputSnapshot.CurrentVersion + 1 });
        var inconsistentOwner = () => _builder.Build(snapshot with
        {
            Owner = snapshot.Owner with { DraftId = "different-draft" },
        });
        var inconsistentTopology = () => _builder.Build(snapshot with
        {
            Topology = new DescriptorReviewReportTopologyInput
            {
                NodeCount = 1,
                EdgeCount = 0,
                NodeCountsByKind = [new DescriptorReviewReportKindCountInput { Kind = DescriptorKind.Event, Count = 2 }],
                EdgeCountsByKind = [],
            },
        });
        var nullDiagnosticMessage = () => _builder.Build(snapshot with
        {
            ReviewDiagnostics = [new DraftAbstractions.DescriptorDraftDiagnostic
            {
                Code = new DiagnosticCode("NULL_MESSAGE"),
                Severity = SeverityLevel.Warning,
                Message = null!,
            }],
        });

        unsupported.Should().Throw<NotSupportedException>();
        inconsistentOwner.Should().Throw<ArgumentException>();
        inconsistentTopology.Should().Throw<ArgumentException>();
        nullDiagnosticMessage.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_Reports_KeepExistingV2SourceReviewHashAndReportId()
    {
        var review = CreateReviewResult("draft-1", "tenant-1");
        var request = CreateRequest(review, CreateDraft("draft-1", "tenant-1"));
        var hashService = new DefaultDescriptorDraftReviewHashService(new DefaultCanonicalHashComputer());
        var builder = new DefaultDescriptorReviewReportBuilder(_templateCatalog, hashService,
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero)));

        var report = builder.Build(request);

        report.SourceReviewHash.Should().Be("f60d35fe1819288f66a980da68dc45e3889627c645fb443a0fbf1de4ccb7acd2");
        var reportIdInput = $"tenant-1|draft-1|1.0|{report.SourceReviewHash}|{AgentControlPlaneContractVersion.Current}|{_templateCatalog.TemplateVersion}";
        report.ReportId.Should().Be(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(reportIdInput))));
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 11: BlocksActivationUntilResolved is explanation, not gate
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_BlocksActivationUntilResolved_IsExplanationNotGate()
    {
        var draft = CreateDraft();
        var blocker1 = CreateDiagnostic(new DiagnosticCode("BLOCK_A"), SeverityLevel.Blocker, "First blocker");
        var blocker2 = CreateDiagnostic(new DiagnosticCode("BLOCK_B"), SeverityLevel.Error, "Error message");
        var reviewResult = CreateReviewResult(
            isActivationEligible: false,
            diagnostics: new[] { blocker1, blocker2 });
        var request = CreateRequest(reviewResult, draft);

        var report = _builder.Build(request);

        var section = report.ActivationEligibilitySection;
        // The section contains the main "activation_blocked" item plus per-blocker explanation items.
        // Per-blocker items use pattern: activation_blocker_{Code}
        // These are explanations (derived from diagnostics), not independent gate decisions.
        section.Items.Should().Contain(i => i.ItemId.StartsWith("activation_blocker_BLOCK_A") && i.ReasonCode == "BLOCK_A");
        section.Items.Should().Contain(i => i.ItemId.StartsWith("activation_blocker_BLOCK_B") && i.ReasonCode == "BLOCK_B");

        // The main item carries the "activation_blocked" reason code (the gate summary)
        var mainItem = section.Items.First(i => i.ReasonCode == "activation_blocked");
        mainItem.Parameters["BlockingReasons"].Should().ContainAll("BLOCK_A", "BLOCK_B");
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 12: Governance decision reflected in recommendations
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_GovernanceDecisionReflectedInRecommendations()
    {
        var draft = CreateDraft();
        var transition = new DescriptorLifecycleTransition
        {
            Subject = new DescriptorRef("test", "desc-001", 1),
            Operation = DescriptorLifecycleOperation.SubmitForReview,
        };
        var decision = new DescriptorLifecycleDecision
        {
            Transition = transition,
            Decision = DescriptorLifecycleDecisionKind.ReviewRequired,
            Findings = Array.Empty<DescriptorLifecycleFinding>(),
        };
        var governanceReport = new DescriptorLifecycleGovernanceReport
        {
            Decisions = new[] { decision },
            MaxDecision = DescriptorLifecycleDecisionKind.ReviewRequired,
            PackageFindings = Array.Empty<DescriptorLifecycleFinding>(),
        };
        var reviewResult = CreateReviewResult() with
        {
            GovernanceDecision = governanceReport,
        };
        var request = CreateRequest(reviewResult, draft);

        var report = _builder.Build(request);

        // Top-level recommendations should include RequestHumanReview
        report.Recommendations.Should().Contain(r =>
            r.Kind == DescriptorReviewRecommendationKind.RequestHumanReview);

        // Governance section should reflect review required
        report.GovernanceSection.Items.Should().NotBeEmpty();
        report.GovernanceSection.Items.Should().Contain(i =>
            i.ReasonCode == "governance");
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 13: Activation eligible + governance approved → RequestActivationHandoff
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_ActivationEligibleAndGovernanceApproved_RecommendsActivationHandoff()
    {
        var draft = CreateDraft();
        var transition = new DescriptorLifecycleTransition
        {
            Subject = new DescriptorRef("test", "desc-001", 1),
            Operation = DescriptorLifecycleOperation.SubmitForReview,
        };
        var decision = new DescriptorLifecycleDecision
        {
            Transition = transition,
            Decision = DescriptorLifecycleDecisionKind.Allowed,
            Findings = Array.Empty<DescriptorLifecycleFinding>(),
        };
        var governanceReport = new DescriptorLifecycleGovernanceReport
        {
            Decisions = new[] { decision },
            MaxDecision = DescriptorLifecycleDecisionKind.Allowed,
            PackageFindings = Array.Empty<DescriptorLifecycleFinding>(),
        };
        var reviewResult = CreateReviewResult(isActivationEligible: true) with
        {
            GovernanceDecision = governanceReport,
        };
        var request = CreateRequest(reviewResult, draft);

        var report = _builder.Build(request);

        // Top-level recommendations should include RequestActivationHandoff
        report.Recommendations.Should().Contain(r =>
            r.Kind == DescriptorReviewRecommendationKind.RequestActivationHandoff);

        // The handoff recommendation should be actionable
        var handoffRec = report.Recommendations.First(r =>
            r.Kind == DescriptorReviewRecommendationKind.RequestActivationHandoff);
        handoffRec.IsActionable.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 14: IsActionable matches applicability (recommendations' actionable flag)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_RequiresManualAction_MatchesIsActionable()
    {
        var draft = CreateDraft();
        // Blockers produce ReviseDraft recommendation (actionable=true)
        var blocker = CreateDiagnostic(new DiagnosticCode("BLOCK_001"), SeverityLevel.Blocker, "Blocker");
        var validationResult = DraftAbstractions.DescriptorDraftValidationResult.Failure(blocker);
        var reviewResult = CreateReviewResult(
            validationResult: validationResult,
            isActivationEligible: false,
            diagnostics: new[] { blocker });
        var request = CreateRequest(reviewResult, draft);

        var report = _builder.Build(request);

        // ReviseDraft recommendation should be actionable (requires manual action)
        var reviseRec = report.Recommendations.First(r => r.Kind == DescriptorReviewRecommendationKind.ReviseDraft);
        reviseRec.IsActionable.Should().BeTrue();

        // NoAction recommendation (when no issues) should not be actionable
        var cleanReviewResult = CreateReviewResult(isActivationEligible: true);
        var cleanRequest = CreateRequest(cleanReviewResult, draft);
        var cleanReport = _builder.Build(cleanRequest);

        var noActionRec = cleanReport.Recommendations.First(r => r.Kind == DescriptorReviewRecommendationKind.NoAction);
        noActionRec.IsActionable.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 14: ReportId is a stable deterministic hash
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_ReportId_IsStableHash()
    {
        var draft = CreateDraft(proposedVersion: "1.0");
        var reviewResult = CreateReviewResult();
        var request = CreateRequest(reviewResult, draft);

        var report1 = _builder.Build(request);
        var report2 = _builder.Build(request);

        report1.ReportId.Should().Be(report2.ReportId,
            "same inputs should produce identical ReportId (deterministic SHA256 hash)");

        // Change DraftVersion — ReportId should change
        var draftV2 = CreateDraft(proposedVersion: "2.0");
        var requestV2 = CreateRequest(reviewResult, draftV2);
        var report3 = _builder.Build(requestV2);

        report3.ReportId.Should().NotBe(report1.ReportId,
            "changing DraftVersion should change the ReportId");
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 15: RequiresManualAction consistency with recommendation kinds
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_RequiresManualAction_ConsistentWithApplicability()
    {
        var draft = CreateDraft();

        // Scenario 1: Blocked draft → ReviseDraft recommendation → IsActionable=true
        var blocker = CreateDiagnostic(new DiagnosticCode("BLOCK_001"), SeverityLevel.Blocker, "Blocker");
        var blockedValidation = DraftAbstractions.DescriptorDraftValidationResult.Failure(blocker);
        var blockedReview = CreateReviewResult(
            validationResult: blockedValidation,
            isActivationEligible: false,
            diagnostics: new[] { blocker });
        var blockedReport = _builder.Build(CreateRequest(blockedReview, draft));

        var reviseRec = blockedReport.Recommendations.First(r => r.Kind == DescriptorReviewRecommendationKind.ReviseDraft);
        reviseRec.IsActionable.Should().BeTrue("ReviseDraft requires manual action");

        // Scenario 2: Clean draft → NoAction → IsActionable=false
        var cleanReview = CreateReviewResult(isActivationEligible: true);
        var cleanReport = _builder.Build(CreateRequest(cleanReview, draft));

        var noActionRec = cleanReport.Recommendations.First(r => r.Kind == DescriptorReviewRecommendationKind.NoAction);
        noActionRec.IsActionable.Should().BeFalse("NoAction does not require manual action");

        // Scenario 3: Governance requires review → RequestHumanReview → IsActionable=true
        var transition = new DescriptorLifecycleTransition
        {
            Subject = new DescriptorRef("test", "desc-001", 1),
            Operation = DescriptorLifecycleOperation.SubmitForReview,
        };
        var decision = new DescriptorLifecycleDecision
        {
            Transition = transition,
            Decision = DescriptorLifecycleDecisionKind.ReviewRequired,
            Findings = Array.Empty<DescriptorLifecycleFinding>(),
        };
        var governanceReport = new DescriptorLifecycleGovernanceReport
        {
            Decisions = new[] { decision },
            MaxDecision = DescriptorLifecycleDecisionKind.ReviewRequired,
            PackageFindings = Array.Empty<DescriptorLifecycleFinding>(),
        };
        var govReview = CreateReviewResult(isActivationEligible: true) with { GovernanceDecision = governanceReport };
        var govReport = _builder.Build(CreateRequest(govReview, draft));

        govReport.Recommendations.Should().Contain(r => r.Kind == DescriptorReviewRecommendationKind.RequestHumanReview);
        var humanReviewRec = govReport.Recommendations.First(r => r.Kind == DescriptorReviewRecommendationKind.RequestHumanReview);
        humanReviewRec.IsActionable.Should().BeTrue("RequestHumanReview requires manual action");
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 16–20: Denied descriptor kind absence (builder is pure projection)
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a review result with rich sub-results (impact analysis, compatibility,
    /// package preview, stable hashes, diagnostics) — all using clean, non-denied
    /// descriptor IDs. The builder is a pure projection: if the input is clean,
    /// the output must be clean (no "DeniedKind" leakage).
    /// </summary>
    private DescriptorReviewReportBuildRequest CreateRichRequestForDeniedKindTests()
    {
        var affected = new AffectedDescriptor
        {
            Ref = new DescriptorRef("test", "desc-impact-001"),
            Kind = DescriptorKind.Capability,
            Name = "ImpactedCapability",
            Severity = DescriptorImpactSeverity.Medium,
            RuntimeAreas = [],
            Paths = [],
            Reason = "Dependency chain",
        };

        var impactReport = new DescriptorImpactAnalysisReport
        {
            ChangeSet = new DescriptorChangeSet { Changes = [] },
            AffectedDescriptors = [affected],
            Paths = [],
            MaxSeverity = DescriptorImpactSeverity.Medium,
            Diagnostics = [],
        };

        var finding = new DescriptorCompatibilityFinding
        {
            Subject = new DescriptorRef("test", "desc-compat-001"),
            ChangeKind = DescriptorChangeKind.Updated,
            Level = DescriptorCompatibilityLevel.Risky,
            Kind = DescriptorCompatibilityFindingKind.Structural,
            RuleId = "RULE-001",
            Message = "Incompatible structure change",
        };

        var compatReport = new DescriptorCompatibilityReport
        {
            ChangeSet = new DescriptorChangeSet { Changes = [] },
            ImpactReport = impactReport,
            Findings = [finding],
            MaxLevel = DescriptorCompatibilityLevel.Risky,
            Diagnostics = [],
        };

        var packagePreview = new DraftAbstractions.DescriptorPackagePreview
        {
            PackageManifestHash = CreateCanonicalHash("mh-001"),
            PackageEvidenceHash = CreateCanonicalHash("eh-001"),
            PackageEvidenceEnvelopeHash = CreateCanonicalHash("env-001"),
            DescriptorIds = ["desc-pkg-1", "desc-pkg-2"],
        };

        var stableHashes = new DescriptorStableHashes
        {
            ContractHash = new CanonicalHash
            {
                Value = "contract-hash-abc",
                Algorithm = "SHA-256",
                AlgorithmVersion = "sha256-pipe-delimited-v0",
                ArtifactKind = "Descriptor",
                DescriptorKind = "Schema",
                Scope = "InternalFull",
                Purpose = "Contract",
                ContractVersion = "0",
                CanonicalShapeVersion = "1"
            },
            DefinitionHash = new CanonicalHash
            {
                Value = "definition-hash-def",
                Algorithm = "SHA-256",
                AlgorithmVersion = "sha256-pipe-delimited-v0",
                ArtifactKind = "Descriptor",
                DescriptorKind = "Schema",
                Scope = "InternalFull",
                Purpose = "Definition",
                ContractVersion = "0",
                CanonicalShapeVersion = "1"
            },
            RuntimeHash = new CanonicalHash
            {
                Value = "runtime-hash-ghi",
                Algorithm = "SHA-256",
                AlgorithmVersion = "sha256-pipe-delimited-v0",
                ArtifactKind = "ReviewResult",
                DescriptorKind = null,
                Scope = "InternalFull",
                Purpose = "SourceBinding",
                ContractVersion = "0",
                CanonicalShapeVersion = "1"
            },
            BindingHash = new CanonicalHash
            {
                Value = "binding-hash-jkl",
                Algorithm = "SHA-256",
                AlgorithmVersion = "sha256-pipe-delimited-v0",
                ArtifactKind = "ReviewResult",
                DescriptorKind = null,
                Scope = "InternalFull",
                Purpose = "SourceBinding",
                ContractVersion = "0",
                CanonicalShapeVersion = "1"
            }
        };

        var diag = CreateDiagnostic(new DiagnosticCode("DIAG_001"), SeverityLevel.Info, "Info diagnostic");

        var reviewResult = new DraftAbstractions.DescriptorDraftReviewResult
        {
            DraftId = "draft-001",
            TenantId = "tenant-001",
            ValidationResult = DraftAbstractions.DescriptorDraftValidationResult.Success(),
            Diagnostics = [diag],
            IsActivationEligible = true,
            ImpactAnalysisResult = impactReport,
            CompatibilityResult = compatReport,
            PackagePreview = packagePreview,
            StableHashes = stableHashes,
        };

        var draft = CreateDraft();
        return CreateRequest(reviewResult, draft);
    }

    [Fact]
    public void Build_DeniedDescriptorKind_NotPresent_InReportItems()
    {
        var request = CreateRichRequestForDeniedKindTests();
        var report = _builder.Build(request);

        var allSections = new[]
        {
            report.SummarySection, report.DraftIdentitySection, report.ProposedChangesSection,
            report.ImpactAnalysisSection, report.DependencySummarySection, report.CompatibilitySection,
            report.GovernanceSection, report.RequiredHumanReviewSection, report.ActivationEligibilitySection,
            report.DiagnosticsSection, report.RecommendationsSection, report.PackagePreviewSection,
            report.StableHashesSection,
        };

        foreach (var section in allSections)
        {
            foreach (var item in section.Items)
            {
                // No parameter value should contain "DeniedKind"
                foreach (var kvp in item.Parameters)
                {
                    kvp.Value.Should().NotContain("DeniedKind",
                        $"section '{section.Title}' item '{item.ItemId}' parameter '{kvp.Key}' should not contain DeniedKind");
                }
            }
        }
    }

    [Fact]
    public void Build_DeniedDescriptorKind_NotPresent_InRelatedDescriptorIds()
    {
        var request = CreateRichRequestForDeniedKindTests();
        var report = _builder.Build(request);

        var allSections = new[]
        {
            report.SummarySection, report.DraftIdentitySection, report.ProposedChangesSection,
            report.ImpactAnalysisSection, report.DependencySummarySection, report.CompatibilitySection,
            report.GovernanceSection, report.RequiredHumanReviewSection, report.ActivationEligibilitySection,
            report.DiagnosticsSection, report.RecommendationsSection, report.PackagePreviewSection,
            report.StableHashesSection,
        };

        foreach (var section in allSections)
        {
            foreach (var item in section.Items)
            {
                foreach (var id in item.RelatedDescriptorIds)
                {
                    id.Should().NotContain("DeniedKind",
                        $"section '{section.Title}' item '{item.ItemId}' RelatedDescriptorIds should not contain DeniedKind");
                }
            }
        }
    }

    [Fact]
    public void Build_DeniedDescriptorKind_NotPresent_InPackagePreviewSection()
    {
        var request = CreateRichRequestForDeniedKindTests();
        var report = _builder.Build(request);

        var section = report.PackagePreviewSection;
        section.IsEmpty.Should().BeFalse("rich request includes package preview");

        foreach (var item in section.Items)
        {
            foreach (var kvp in item.Parameters)
            {
                kvp.Value.Should().NotContain("DeniedKind",
                    $"PackagePreview item '{item.ItemId}' parameter '{kvp.Key}' should not contain DeniedKind");
            }
            foreach (var id in item.RelatedDescriptorIds)
            {
                id.Should().NotContain("DeniedKind",
                    $"PackagePreview item '{item.ItemId}' RelatedDescriptorIds should not contain DeniedKind");
            }
        }
    }

    [Fact]
    public void Build_DeniedDescriptorKind_NotPresent_InStableHashesSection()
    {
        var request = CreateRichRequestForDeniedKindTests();
        var report = _builder.Build(request);

        var section = report.StableHashesSection;
        section.IsEmpty.Should().BeFalse("rich request includes stable hashes");

        foreach (var item in section.Items)
        {
            foreach (var kvp in item.Parameters)
            {
                kvp.Value.Should().NotContain("DeniedKind",
                    $"StableHashes item '{item.ItemId}' parameter '{kvp.Key}' should not contain DeniedKind");
            }
            foreach (var id in item.RelatedDescriptorIds)
            {
                id.Should().NotContain("DeniedKind",
                    $"StableHashes item '{item.ItemId}' RelatedDescriptorIds should not contain DeniedKind");
            }
        }
    }

    [Fact]
    public void Build_DeniedDescriptorKind_NotPresent_InImpactAnalysisSection()
    {
        var request = CreateRichRequestForDeniedKindTests();
        var report = _builder.Build(request);

        var section = report.ImpactAnalysisSection;
        section.IsEmpty.Should().BeFalse("rich request includes impact analysis");

        foreach (var item in section.Items)
        {
            foreach (var kvp in item.Parameters)
            {
                kvp.Value.Should().NotContain("DeniedKind",
                    $"ImpactAnalysis item '{item.ItemId}' parameter '{kvp.Key}' should not contain DeniedKind");
            }
            foreach (var id in item.RelatedDescriptorIds)
            {
                id.Should().NotContain("DeniedKind",
                    $"ImpactAnalysis item '{item.ItemId}' RelatedDescriptorIds should not contain DeniedKind");
            }
        }
    }

    private static string[] GetMessages(DescriptorReviewReportDto report)
        => new[]
            {
                report.SummarySection,
                report.DraftIdentitySection,
                report.ProposedChangesSection,
                report.ImpactAnalysisSection,
                report.DependencySummarySection,
                report.CompatibilitySection,
                report.GovernanceSection,
                report.RequiredHumanReviewSection,
                report.ActivationEligibilitySection,
                report.DiagnosticsSection,
                report.RecommendationsSection,
                report.PackagePreviewSection,
                report.StableHashesSection
            }
            .SelectMany(section => section.Items.Select(item => item.Message))
            .Concat(report.Recommendations.Select(recommendation => recommendation.Message))
            .ToArray();

    private static Mock<IDescriptor> CreateDescriptor(string id, string name, DescriptorKind kind)
    {
        var descriptor = new Mock<IDescriptor>();
        descriptor.SetupGet(value => value.Namespace).Returns("test");
        descriptor.SetupGet(value => value.Id).Returns(id);
        descriptor.SetupGet(value => value.Name).Returns(name);
        descriptor.SetupGet(value => value.Kind).Returns(kind);
        descriptor.SetupGet(value => value.State).Returns(DescriptorState.Draft);
        descriptor.SetupGet(value => value.SupersededById).Returns((string?)null);
        return descriptor;
    }

    private static DescriptorTopologySnapshot CreateTopologySnapshot()
    {
        var firstRef = new DescriptorRef("test", "topology-first", 1);
        var secondRef = new DescriptorRef("test", "topology-second", 1);
        var firstNode = new DescriptorNode
        {
            Ref = firstRef,
            Kind = DescriptorKind.Event,
            Name = "First",
            State = DescriptorState.Active,
            OutgoingEdgeIndices = new HashSet<int> { 0 },
            IncomingEdgeIndices = new HashSet<int>(),
        };
        var secondNode = new DescriptorNode
        {
            Ref = secondRef,
            Kind = DescriptorKind.Capability,
            Name = "Second",
            State = DescriptorState.Active,
            OutgoingEdgeIndices = new HashSet<int>(),
            IncomingEdgeIndices = new HashSet<int> { 0 },
        };
        var edge = new DescriptorEdge
        {
            Index = 0,
            From = firstRef,
            To = secondRef,
            Kind = RelationshipKind.DependsOn,
            Strength = RelationshipStrength.Strong,
            IsRuntimeBinding = false,
        };
        return new DescriptorTopologySnapshot(
            new Dictionary<DescriptorRef, DescriptorNode> { [firstRef] = firstNode, [secondRef] = secondNode },
            [edge],
            new DescriptorTopologyDiagnostics { All = [] },
            new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>(),
            new Dictionary<(DescriptorIdentity, int), List<(DescriptorRef, DescriptorEdge)>>(),
            new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>(),
            DateTimeOffset.UnixEpoch);
    }

    private static DescriptorReviewReportItemDto[] GetItems(DescriptorReviewReportDto report)
        => new[]
            {
                report.SummarySection,
                report.DraftIdentitySection,
                report.ProposedChangesSection,
                report.ImpactAnalysisSection,
                report.DependencySummarySection,
                report.CompatibilitySection,
                report.GovernanceSection,
                report.RequiredHumanReviewSection,
                report.ActivationEligibilitySection,
                report.DiagnosticsSection,
                report.RecommendationsSection,
                report.PackagePreviewSection,
                report.StableHashesSection
            }
            .SelectMany(section => section.Items)
            .ToArray();

    private sealed class KeyReturningLocalizationService(string currentCulture) : ILocalizationService
    {
        public string CurrentCulture { get; } = currentCulture;
        public string GetString(string key) => key;
        public string GetString(string key, params object[] arguments) => key;
        public string GetString(string key, string cultureName) => key;
        public string GetString(string key, string cultureName, params object[] arguments) => key;
        public Task<string?> GetStringAsync(string key) => Task.FromResult<string?>(key);
        public Task<string?> GetStringAsync(string key, params object[] arguments) => Task.FromResult<string?>(key);
        public Task<string?> GetStringAsync(string key, string cultureName) => Task.FromResult<string?>(key);
        public Task<string?> GetStringAsync(string key, string cultureName, params object[] arguments) => Task.FromResult<string?>(key);
        public IDisposable ChangeCulture(string cultureName) => throw new NotSupportedException();
        public Task<IDisposable> ChangeCultureAsync(string cultureName) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
