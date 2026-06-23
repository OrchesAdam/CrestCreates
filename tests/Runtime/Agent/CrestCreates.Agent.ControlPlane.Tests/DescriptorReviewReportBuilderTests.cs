using CrestCreates.Agent.ControlPlane;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using FluentAssertions;
using Xunit;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Tests;

public class DescriptorReviewReportBuilderTests
{
    private readonly DefaultDescriptorReviewMessageTemplateCatalog _templateCatalog = new();
    private readonly DefaultDescriptorReviewReportBuilder _builder;

    public DescriptorReviewReportBuilderTests()
    {
        _builder = new DefaultDescriptorReviewReportBuilder(_templateCatalog);
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

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
        string code = "TEST_DIAG",
        DraftAbstractions.DescriptorDraftDiagnosticSeverity severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Warning,
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
        section.OverallSeverity.Should().Be(DescriptorReviewSeverity.Info);
    }

    // ─────────────────────────────────────────────────────────────────
    // Test 3: Blocked draft returns activation eligible section with blockers
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_BlockedDraft_ReturnsActivationEligibleSectionWithBlockers()
    {
        var draft = CreateDraft();
        var blocker = CreateDiagnostic("DRAFT_BLOCKER", DraftAbstractions.DescriptorDraftDiagnosticSeverity.Blocker, "Blocked reason");
        var reviewResult = CreateReviewResult(
            isActivationEligible: false,
            diagnostics: new[] { blocker });
        var request = CreateRequest(reviewResult, draft);

        var report = _builder.Build(request);

        var section = report.ActivationEligibilitySection;
        section.IsEmpty.Should().BeFalse();
        section.OverallSeverity.Should().Be(DescriptorReviewSeverity.Blocker);
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
        var infoDiag = CreateDiagnostic("INFO_001", DraftAbstractions.DescriptorDraftDiagnosticSeverity.Info, "Info message");
        var warnDiag = CreateDiagnostic("WARN_001", DraftAbstractions.DescriptorDraftDiagnosticSeverity.Warning, "Warning message");
        var errorDiag = CreateDiagnostic("ERR_001", DraftAbstractions.DescriptorDraftDiagnosticSeverity.Error, "Error message");
        var blockerDiag = CreateDiagnostic("BLOCK_001", DraftAbstractions.DescriptorDraftDiagnosticSeverity.Blocker, "Blocker message");

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
        section.Items.Should().Contain(i => i.ReasonCode == "INFO_001" && i.Severity == DescriptorReviewSeverity.Info);
        section.Items.Should().Contain(i => i.ReasonCode == "WARN_001" && i.Severity == DescriptorReviewSeverity.Warning);
        section.Items.Should().Contain(i => i.ReasonCode == "ERR_001" && i.Severity == DescriptorReviewSeverity.Error);
        section.Items.Should().Contain(i => i.ReasonCode == "BLOCK_001" && i.Severity == DescriptorReviewSeverity.Blocker);
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
            ManifestHash = "mh-001",
            SnapshotHash = "sh-001",
            EvidenceHash = "eh-001",
            EnvelopeHash = "env-001",
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
        item.Parameters["SnapshotHash"].Should().Be("sh-001");
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

    // ─────────────────────────────────────────────────────────────────
    // Test 11: BlocksActivationUntilResolved is explanation, not gate
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_BlocksActivationUntilResolved_IsExplanationNotGate()
    {
        var draft = CreateDraft();
        var blocker1 = CreateDiagnostic("BLOCK_A", DraftAbstractions.DescriptorDraftDiagnosticSeverity.Blocker, "First blocker");
        var blocker2 = CreateDiagnostic("BLOCK_B", DraftAbstractions.DescriptorDraftDiagnosticSeverity.Error, "Error message");
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
        var blocker = CreateDiagnostic("BLOCK_001", DraftAbstractions.DescriptorDraftDiagnosticSeverity.Blocker, "Blocker");
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
        var blocker = CreateDiagnostic("BLOCK_001", DraftAbstractions.DescriptorDraftDiagnosticSeverity.Blocker, "Blocker");
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
            ManifestHash = "mh-001",
            SnapshotHash = "sh-001",
            EvidenceHash = "eh-001",
            EnvelopeHash = "env-001",
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

        var diag = CreateDiagnostic("DIAG_001", DraftAbstractions.DescriptorDraftDiagnosticSeverity.Info, "Info diagnostic");

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
}
