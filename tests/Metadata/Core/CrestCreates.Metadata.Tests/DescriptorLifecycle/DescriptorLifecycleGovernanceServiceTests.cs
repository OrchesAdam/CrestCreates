using Xunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.DescriptorLifecycle;
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Metadata.Tests.DescriptorLifecycle;

public class DescriptorLifecycleGovernanceServiceTests
{
    private static readonly IDescriptorLifecycleGovernanceService Service =
        new DefaultDescriptorLifecycleGovernanceService();

    private static DescriptorRef TestRef => new("schema", "T1", 1);

    private static DescriptorLifecycleTransition MakeTransition(
        DescriptorLifecycleOperation operation,
        DescriptorRef? subject = null)
        => new()
        {
            Subject = subject ?? TestRef,
            Operation = operation
        };

    private static DescriptorLifecycleGovernanceRequest MakeRequest(
        DescriptorLifecycleTransition[]? transitions = null,
        ValidationReport? validationReport = null,
        RuntimeBindingReport? bindingReport = null,
        DescriptorTopologyDiagnostics? topologyDiagnostics = null,
        DescriptorImpactAnalysisReport? impactReport = null,
        DescriptorCompatibilityReport? compatibilityReport = null,
        DescriptorLifecycleGovernanceOptions? options = null)
        => new()
        {
            Transitions = transitions ?? new[] { MakeTransition(DescriptorLifecycleOperation.Activate) },
            ValidationReport = validationReport ?? ValidationReport.Empty,
            BindingReport = bindingReport ?? new RuntimeBindingReport(),
            TopologyDiagnostics = topologyDiagnostics ?? new DescriptorTopologyDiagnostics { All = Array.Empty<DescriptorTopologyDiagnostic>() },
            ImpactReport = impactReport ?? MakeCleanImpactReport(),
            CompatibilityReport = compatibilityReport ?? MakeCleanCompatibilityReport(),
            Options = options ?? new()
        };

    private static DescriptorImpactAnalysisReport MakeCleanImpactReport()
        => MakeImpactReport(MakeChangeSet(TestRef));

    private static DescriptorImpactAnalysisReport MakeImpactReport(
        DescriptorChangeSet changeSet,
        DescriptorImpactSeverity maxSeverity = DescriptorImpactSeverity.None,
        DescriptorImpactDiagnostic[]? diagnostics = null,
        AffectedDescriptor[]? affected = null)
        => new()
        {
            ChangeSet = changeSet,
            AffectedDescriptors = affected ?? Array.Empty<AffectedDescriptor>(),
            Paths = Array.Empty<DescriptorImpactPath>(),
            MaxSeverity = maxSeverity,
            Diagnostics = diagnostics ?? Array.Empty<DescriptorImpactDiagnostic>()
        };

    private static DescriptorCompatibilityReport MakeCleanCompatibilityReport()
        => MakeCompatibilityReport(MakeChangeSet(TestRef));

    private static DescriptorCompatibilityReport MakeCompatibilityReport(
        DescriptorChangeSet changeSet,
        DescriptorCompatibilityLevel maxLevel = DescriptorCompatibilityLevel.Compatible,
        DescriptorCompatibilityFinding[]? findings = null,
        DescriptorCompatibilityDiagnostic[]? diagnostics = null)
        => new()
        {
            ChangeSet = changeSet,
            ImpactReport = MakeImpactReport(changeSet),
            Findings = findings ?? Array.Empty<DescriptorCompatibilityFinding>(),
            MaxLevel = maxLevel,
            Diagnostics = diagnostics ?? Array.Empty<DescriptorCompatibilityDiagnostic>()
        };

    private static DescriptorChangeSet MakeChangeSet(params DescriptorRef[] refs)
        => new()
        {
            Changes = refs.Select(r => new DescriptorChange
            {
                Ref = r,
                Kind = DescriptorChangeKind.Updated
            }).ToArray()
        };

    // --- 10.1 Validation Policy ---

    [Fact]
    public void ValidationError_BlocksTransition()
    {
        var request = MakeRequest(
            validationReport: ValidationReport.FromIssues(
                new ValidationIssue(SeverityLevel.Error, "Something is wrong")));

        var report = Service.Evaluate(request);

        report.IsBlocked.Should().BeTrue();
        report.Decisions.Should().ContainSingle()
            .Which.Findings.Should().Contain(f =>
                f.Code == "LIFECYCLE_VALIDATION_ERROR" &&
                f.Severity == SeverityLevel.Blocker);
    }

    [Fact]
    public void ValidationWarning_AllowsByDefault()
    {
        var request = MakeRequest(
            validationReport: ValidationReport.FromIssues(
                new ValidationIssue(SeverityLevel.Warning, "Minor issue")));

        var report = Service.Evaluate(request);

        report.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void ValidationWarning_ReviewRequired_WhenOptionEnabled()
    {
        var request = MakeRequest(
            validationReport: ValidationReport.FromIssues(
                new ValidationIssue(SeverityLevel.Warning, "Minor issue")),
            options: new DescriptorLifecycleGovernanceOptions
            {
                TreatValidationWarningsAsReviewRequired = true
            });

        var report = Service.Evaluate(request);

        report.RequiresReview.Should().BeTrue();
    }

    // --- 10.2 Binding Policy — ValidateDraft ---

    [Fact]
    public void ValidateDraft_DoesNotRequireRuntimeBinding()
    {
        var bindingReport = new RuntimeBindingReport
        {
            Descriptors = new[]
            {
                new DescriptorBindingReport
                {
                    DescriptorId = TestRef.FullId,
                    DescriptorKind = DescriptorKind.Schema,
                    Status = DescriptorBindingStatus.Unbound
                }
            }
        };

        var request = MakeRequest(
            transitions: new[] { MakeTransition(DescriptorLifecycleOperation.ValidateDraft) },
            bindingReport: bindingReport);

        var report = Service.Evaluate(request);

        // ValidateDraft should not block on binding issues
        report.IsBlocked.Should().BeFalse();
    }

    // --- 10.3 Binding Policy — SubmitForReview ---

    [Fact]
    public void SubmitForReview_BindingInvalid_Blocks()
    {
        var bindingReport = new RuntimeBindingReport
        {
            Descriptors = new[]
            {
                new DescriptorBindingReport
                {
                    DescriptorId = TestRef.FullId,
                    DescriptorKind = DescriptorKind.Schema,
                    Status = DescriptorBindingStatus.Invalid
                }
            }
        };

        var request = MakeRequest(
            transitions: new[] { MakeTransition(DescriptorLifecycleOperation.SubmitForReview) },
            bindingReport: bindingReport);

        var report = Service.Evaluate(request);

        report.IsBlocked.Should().BeTrue();
    }

    [Fact]
    public void SubmitForReview_BindingUnbound_ReviewRequiredByDefault()
    {
        var bindingReport = new RuntimeBindingReport
        {
            Descriptors = new[]
            {
                new DescriptorBindingReport
                {
                    DescriptorId = TestRef.FullId,
                    DescriptorKind = DescriptorKind.Schema,
                    Status = DescriptorBindingStatus.Unbound
                }
            }
        };

        var request = MakeRequest(
            transitions: new[] { MakeTransition(DescriptorLifecycleOperation.SubmitForReview) },
            bindingReport: bindingReport);

        var report = Service.Evaluate(request);

        report.RequiresReview.Should().BeTrue();
        report.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public void SubmitForReview_BindingUnbound_Blocks_WhenOptionEnabled()
    {
        var bindingReport = new RuntimeBindingReport
        {
            Descriptors = new[]
            {
                new DescriptorBindingReport
                {
                    DescriptorId = TestRef.FullId,
                    DescriptorKind = DescriptorKind.Schema,
                    Status = DescriptorBindingStatus.Unbound
                }
            }
        };

        var request = MakeRequest(
            transitions: new[] { MakeTransition(DescriptorLifecycleOperation.SubmitForReview) },
            bindingReport: bindingReport,
            options: new DescriptorLifecycleGovernanceOptions
            {
                BlockSubmitForReviewOnUnboundBinding = true
            });

        var report = Service.Evaluate(request);

        report.IsBlocked.Should().BeTrue();
    }

    [Fact]
    public void SubmitForReview_BindingUnsupported_ReviewRequiredByDefault()
    {
        var bindingReport = new RuntimeBindingReport
        {
            Descriptors = new[]
            {
                new DescriptorBindingReport
                {
                    DescriptorId = TestRef.FullId,
                    DescriptorKind = DescriptorKind.Schema,
                    Status = DescriptorBindingStatus.Unsupported
                }
            }
        };

        var request = MakeRequest(
            transitions: new[] { MakeTransition(DescriptorLifecycleOperation.SubmitForReview) },
            bindingReport: bindingReport);

        var report = Service.Evaluate(request);

        report.RequiresReview.Should().BeTrue();
        report.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public void SubmitForReview_BindingUnsupported_Blocks_WhenOptionEnabled()
    {
        var bindingReport = new RuntimeBindingReport
        {
            Descriptors = new[]
            {
                new DescriptorBindingReport
                {
                    DescriptorId = TestRef.FullId,
                    DescriptorKind = DescriptorKind.Schema,
                    Status = DescriptorBindingStatus.Unsupported
                }
            }
        };

        var request = MakeRequest(
            transitions: new[] { MakeTransition(DescriptorLifecycleOperation.SubmitForReview) },
            bindingReport: bindingReport,
            options: new DescriptorLifecycleGovernanceOptions
            {
                BlockSubmitForReviewOnUnsupportedBinding = true
            });

        var report = Service.Evaluate(request);

        report.IsBlocked.Should().BeTrue();
    }

    [Fact]
    public void SubmitForReview_BindingPartiallyBound_ReviewRequiredByDefault()
    {
        var bindingReport = new RuntimeBindingReport
        {
            Descriptors = new[]
            {
                new DescriptorBindingReport
                {
                    DescriptorId = TestRef.FullId,
                    DescriptorKind = DescriptorKind.Schema,
                    Status = DescriptorBindingStatus.PartiallyBound
                }
            }
        };

        var request = MakeRequest(
            transitions: new[] { MakeTransition(DescriptorLifecycleOperation.SubmitForReview) },
            bindingReport: bindingReport);

        var report = Service.Evaluate(request);

        report.RequiresReview.Should().BeTrue();
        report.IsBlocked.Should().BeFalse();
    }

    // --- 10.4 Binding Policy — Activate ---

    [Fact]
    public void Activate_BindingInvalid_Blocks()
    {
        var bindingReport = new RuntimeBindingReport
        {
            Descriptors = new[]
            {
                new DescriptorBindingReport
                {
                    DescriptorId = TestRef.FullId,
                    DescriptorKind = DescriptorKind.Schema,
                    Status = DescriptorBindingStatus.Invalid
                }
            }
        };

        var request = MakeRequest(
            transitions: new[] { MakeTransition(DescriptorLifecycleOperation.Activate) },
            bindingReport: bindingReport);

        var report = Service.Evaluate(request);

        report.IsBlocked.Should().BeTrue();
    }

    [Fact]
    public void Activate_BindingUnbound_BlocksByDefault()
    {
        var bindingReport = new RuntimeBindingReport
        {
            Descriptors = new[]
            {
                new DescriptorBindingReport
                {
                    DescriptorId = TestRef.FullId,
                    DescriptorKind = DescriptorKind.Schema,
                    Status = DescriptorBindingStatus.Unbound
                }
            }
        };

        var request = MakeRequest(
            transitions: new[] { MakeTransition(DescriptorLifecycleOperation.Activate) },
            bindingReport: bindingReport);

        var report = Service.Evaluate(request);

        report.IsBlocked.Should().BeTrue();
    }

    [Fact]
    public void Activate_BindingUnsupported_BlocksByDefault()
    {
        var bindingReport = new RuntimeBindingReport
        {
            Descriptors = new[]
            {
                new DescriptorBindingReport
                {
                    DescriptorId = TestRef.FullId,
                    DescriptorKind = DescriptorKind.Schema,
                    Status = DescriptorBindingStatus.Unsupported
                }
            }
        };

        var request = MakeRequest(
            transitions: new[] { MakeTransition(DescriptorLifecycleOperation.Activate) },
            bindingReport: bindingReport);

        var report = Service.Evaluate(request);

        report.IsBlocked.Should().BeTrue();
    }

    [Fact]
    public void Activate_BindingPartiallyBound_AllowsByDefault()
    {
        var bindingReport = new RuntimeBindingReport
        {
            Descriptors = new[]
            {
                new DescriptorBindingReport
                {
                    DescriptorId = TestRef.FullId,
                    DescriptorKind = DescriptorKind.Schema,
                    Status = DescriptorBindingStatus.PartiallyBound
                }
            }
        };

        var request = MakeRequest(
            transitions: new[] { MakeTransition(DescriptorLifecycleOperation.Activate) },
            bindingReport: bindingReport);

        var report = Service.Evaluate(request);

        report.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Activate_BindingPartiallyBound_ReviewRequired_WhenOptionEnabled()
    {
        var bindingReport = new RuntimeBindingReport
        {
            Descriptors = new[]
            {
                new DescriptorBindingReport
                {
                    DescriptorId = TestRef.FullId,
                    DescriptorKind = DescriptorKind.Schema,
                    Status = DescriptorBindingStatus.PartiallyBound
                }
            }
        };

        var request = MakeRequest(
            transitions: new[] { MakeTransition(DescriptorLifecycleOperation.Activate) },
            bindingReport: bindingReport,
            options: new DescriptorLifecycleGovernanceOptions
            {
                TreatBindingPartialAsReviewRequired = true
            });

        var report = Service.Evaluate(request);

        report.RequiresReview.Should().BeTrue();
    }

    // --- 10.5 Binding Policy — Reject ---

    [Fact]
    public void Reject_DoesNotRequireRuntimeBinding()
    {
        var bindingReport = new RuntimeBindingReport
        {
            Descriptors = new[]
            {
                new DescriptorBindingReport
                {
                    DescriptorId = TestRef.FullId,
                    DescriptorKind = DescriptorKind.Schema,
                    Status = DescriptorBindingStatus.Invalid
                }
            }
        };

        var request = MakeRequest(
            transitions: new[] { MakeTransition(DescriptorLifecycleOperation.Reject) },
            bindingReport: bindingReport);

        var report = Service.Evaluate(request);

        report.IsBlocked.Should().BeFalse();
    }

    // --- 10.6 Topology Policy ---

    [Fact]
    public void TopologyError_BlocksByDefault()
    {
        var topologyDiag = new DescriptorTopologyDiagnostics
        {
            All = new[]
            {
                new DescriptorTopologyDiagnostic(
                    SeverityLevel.Error,
                    new DiagnosticCode("MISSING_TARGET"),
                    "Target missing",
                    TestRef,
                    null)
            }
        };

        var request = MakeRequest(topologyDiagnostics: topologyDiag);

        var report = Service.Evaluate(request);

        report.IsBlocked.Should().BeTrue();
    }

    [Fact]
    public void TopologyWarning_ReviewRequired()
    {
        var topologyDiag = new DescriptorTopologyDiagnostics
        {
            All = new[]
            {
                new DescriptorTopologyDiagnostic(
                    SeverityLevel.Warning,
                    new DiagnosticCode("SOME_WARNING"),
                    "Some warning",
                    TestRef,
                    null)
            }
        };

        var request = MakeRequest(topologyDiagnostics: topologyDiag);

        var report = Service.Evaluate(request);

        report.RequiresReview.Should().BeTrue();
    }

    // --- 10.7 Impact Policy ---

    [Fact]
    public void ImpactDiagnosticError_BlocksByDefault()
    {
        var impactReport = MakeImpactReport(
            MakeChangeSet(TestRef),
            diagnostics: new[]
            {
                new DescriptorImpactDiagnostic(
SeverityLevel.Error, new DiagnosticCode("IMPACT_TEST"),
                    "Impact error",
                    TestRef,
                    null)
            });

        var compatReport = MakeCompatibilityReport(MakeChangeSet(TestRef));

        var request = MakeRequest(impactReport: impactReport, compatibilityReport: compatReport);

        var report = Service.Evaluate(request);

        report.IsBlocked.Should().BeTrue();
    }

    [Fact]
    public void ImpactDiagnosticWarning_ReviewRequired()
    {
        var impactReport = MakeImpactReport(
            MakeChangeSet(TestRef),
            diagnostics: new[]
            {
                new DescriptorImpactDiagnostic(
SeverityLevel.Warning, new DiagnosticCode("IMPACT_TEST"),
                    "Impact warning",
                    TestRef,
                    null)
            });

        var compatReport = MakeCompatibilityReport(MakeChangeSet(TestRef));

        var request = MakeRequest(impactReport: impactReport, compatibilityReport: compatReport);

        var report = Service.Evaluate(request);

        report.RequiresReview.Should().BeTrue();
    }

    [Fact]
    public void ImpactCritical_ReviewRequired_ButNotBreaking()
    {
        var impactReport = MakeImpactReport(
            MakeChangeSet(TestRef),
            maxSeverity: DescriptorImpactSeverity.Critical);

        var compatReport = MakeCompatibilityReport(MakeChangeSet(TestRef));

        var request = MakeRequest(impactReport: impactReport, compatibilityReport: compatReport);

        var report = Service.Evaluate(request);

        report.RequiresReview.Should().BeTrue();
        // Impact review findings must NOT be labeled as breaking/Blocker
        report.Decisions.Should().ContainSingle()
            .Which.Findings.Should().NotContain(f =>
                f.Code == "LIFECYCLE_IMPACT_SEVERITY_THRESHOLD" &&
                f.Severity == SeverityLevel.Blocker);
    }

    // --- 10.8 Compatibility Policy ---

    [Fact]
    public void CompatibilityBreaking_ReviewRequiredByDefault()
    {
        var changeSet = MakeChangeSet(TestRef);
        var compatReport = MakeCompatibilityReport(
            changeSet,
            maxLevel: DescriptorCompatibilityLevel.Breaking,
            findings: new[]
            {
                new DescriptorCompatibilityFinding
                {
                    Subject = TestRef,
                    ChangeKind = DescriptorChangeKind.Updated,
                    Level = DescriptorCompatibilityLevel.Breaking,
                    Kind = DescriptorCompatibilityFindingKind.Structural,
                    RuleId = "TEST_RULE",
                    Message = "Breaking change"
                }
            });

        var request = MakeRequest(compatibilityReport: compatReport);

        var report = Service.Evaluate(request);

        report.RequiresReview.Should().BeTrue();
    }

    [Fact]
    public void CompatibilityBreaking_BlocksActivate_WhenOptionEnabled()
    {
        var changeSet = MakeChangeSet(TestRef);
        var compatReport = MakeCompatibilityReport(
            changeSet,
            maxLevel: DescriptorCompatibilityLevel.Breaking,
            findings: new[]
            {
                new DescriptorCompatibilityFinding
                {
                    Subject = TestRef,
                    ChangeKind = DescriptorChangeKind.Updated,
                    Level = DescriptorCompatibilityLevel.Breaking,
                    Kind = DescriptorCompatibilityFindingKind.Structural,
                    RuleId = "TEST_RULE",
                    Message = "Breaking change"
                }
            });

        var request = MakeRequest(
            transitions: new[] { MakeTransition(DescriptorLifecycleOperation.Activate) },
            compatibilityReport: compatReport,
            options: new DescriptorLifecycleGovernanceOptions
            {
                BlockActivateOnBreakingCompatibility = true
            });

        var report = Service.Evaluate(request);

        report.IsBlocked.Should().BeTrue();
    }

    [Fact]
    public void CompatibilitySecuritySensitive_ReviewRequired()
    {
        var changeSet = MakeChangeSet(TestRef);
        var compatReport = MakeCompatibilityReport(
            changeSet,
            maxLevel: DescriptorCompatibilityLevel.SecuritySensitive,
            findings: new[]
            {
                new DescriptorCompatibilityFinding
                {
                    Subject = TestRef,
                    ChangeKind = DescriptorChangeKind.Updated,
                    Level = DescriptorCompatibilityLevel.SecuritySensitive,
                    Kind = DescriptorCompatibilityFindingKind.Security,
                    RuleId = "TEST_RULE",
                    Message = "Security-sensitive"
                }
            });

        var request = MakeRequest(compatibilityReport: compatReport);

        var report = Service.Evaluate(request);

        report.RequiresReview.Should().BeTrue();
    }

    [Fact]
    public void CompatibilityRisky_ReviewRequired()
    {
        var changeSet = MakeChangeSet(TestRef);
        var compatReport = MakeCompatibilityReport(
            changeSet,
            maxLevel: DescriptorCompatibilityLevel.Risky,
            findings: new[]
            {
                new DescriptorCompatibilityFinding
                {
                    Subject = TestRef,
                    ChangeKind = DescriptorChangeKind.Updated,
                    Level = DescriptorCompatibilityLevel.Risky,
                    Kind = DescriptorCompatibilityFindingKind.Behavior,
                    RuleId = "TEST_RULE",
                    Message = "Risky"
                }
            });

        var request = MakeRequest(compatibilityReport: compatReport);

        var report = Service.Evaluate(request);

        report.RequiresReview.Should().BeTrue();
    }

    [Fact]
    public void CompatibilityUnsupported_ReviewRequired_NotBreaking()
    {
        var changeSet = MakeChangeSet(TestRef);
        var compatReport = MakeCompatibilityReport(
            changeSet,
            maxLevel: DescriptorCompatibilityLevel.Unsupported,
            findings: new[]
            {
                new DescriptorCompatibilityFinding
                {
                    Subject = TestRef,
                    ChangeKind = DescriptorChangeKind.Updated,
                    Level = DescriptorCompatibilityLevel.Unsupported,
                    Kind = DescriptorCompatibilityFindingKind.Analysis,
                    RuleId = "TEST_RULE",
                    Message = "Unsupported"
                }
            });

        var request = MakeRequest(compatibilityReport: compatReport);

        var report = Service.Evaluate(request);

        report.RequiresReview.Should().BeTrue();
        // Unsupported must NOT produce a Blocker finding
        report.Decisions.Should().ContainSingle()
            .Which.Findings.Should().NotContain(f =>
                f.Code == "LIFECYCLE_COMPAT_UNSUPPORTED" &&
                f.Severity == SeverityLevel.Blocker);
    }

    [Fact]
    public void CompatibilityCompatible_Allows()
    {
        var changeSet = MakeChangeSet(TestRef);
        var compatReport = MakeCompatibilityReport(changeSet);

        var request = MakeRequest(compatibilityReport: compatReport);

        var report = Service.Evaluate(request);

        report.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void CompatibilityDiagnosticError_BlocksByDefault()
    {
        var changeSet = MakeChangeSet(TestRef);
        var compatReport = MakeCompatibilityReport(
            changeSet,
            diagnostics: new[]
            {
                new DescriptorCompatibilityDiagnostic(
                    SeverityLevel.Error,
                    new DiagnosticCode("COMPAT_TEST"),
                    "Compat error",
                    TestRef,
                    null)
            });

        var request = MakeRequest(compatibilityReport: compatReport);

        var report = Service.Evaluate(request);

        report.IsBlocked.Should().BeTrue();
    }

    // --- 10.9 Consistency & Edge Cases ---

    [Fact]
    public void CompatibilityChangeSetMismatch_BlocksOrPackageFinding()
    {
        var impactChangeSet = MakeChangeSet(TestRef);
        var compatChangeSet = MakeChangeSet(new DescriptorRef("other", "O1", 1));

        var impactReport = MakeImpactReport(impactChangeSet);
        var compatReport = MakeCompatibilityReport(compatChangeSet);

        var request = MakeRequest(
            impactReport: impactReport,
            compatibilityReport: compatReport);

        var report = Service.Evaluate(request);

        report.PackageFindings.Should().Contain(f =>
            f.Code == "LIFECYCLE_CHANGESET_MISMATCH");
    }

    [Fact]
    public void Activate_WithCleanReports_Allows()
    {
        var request = MakeRequest(
            transitions: new[] { MakeTransition(DescriptorLifecycleOperation.Activate) });

        var report = Service.Evaluate(request);

        report.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Deprecate_WithAffectedConsumers_ReviewRequired()
    {
        var changeSet = MakeChangeSet(TestRef);
        var compatReport = MakeCompatibilityReport(
            changeSet,
            maxLevel: DescriptorCompatibilityLevel.Risky,
            findings: new[]
            {
                new DescriptorCompatibilityFinding
                {
                    Subject = TestRef,
                    ChangeKind = DescriptorChangeKind.Deprecated,
                    Level = DescriptorCompatibilityLevel.Risky,
                    Kind = DescriptorCompatibilityFindingKind.Behavior,
                    RuleId = "TEST_RULE",
                    Message = "Affected consumers"
                }
            });

        var request = MakeRequest(
            transitions: new[] { MakeTransition(DescriptorLifecycleOperation.Deprecate) },
            compatibilityReport: compatReport);

        var report = Service.Evaluate(request);

        report.RequiresReview.Should().BeTrue();
    }

    [Fact]
    public void Retire_WithBreakingCompatibility_ReviewRequired()
    {
        var changeSet = MakeChangeSet(TestRef);
        var compatReport = MakeCompatibilityReport(
            changeSet,
            maxLevel: DescriptorCompatibilityLevel.Breaking,
            findings: new[]
            {
                new DescriptorCompatibilityFinding
                {
                    Subject = TestRef,
                    ChangeKind = DescriptorChangeKind.Removed,
                    Level = DescriptorCompatibilityLevel.Breaking,
                    Kind = DescriptorCompatibilityFindingKind.Structural,
                    RuleId = "TEST_RULE",
                    Message = "Breaking removal"
                }
            });

        var request = MakeRequest(
            transitions: new[] { MakeTransition(DescriptorLifecycleOperation.Retire) },
            compatibilityReport: compatReport);

        var report = Service.Evaluate(request);

        report.RequiresReview.Should().BeTrue();
    }

    [Fact]
    public void EmptyTransitions_ReturnsAllowedReport()
    {
        var request = MakeRequest(transitions: Array.Empty<DescriptorLifecycleTransition>());

        var report = Service.Evaluate(request);

        report.IsAllowed.Should().BeTrue();
        report.PackageFindings.Should().Contain(f =>
            f.Code == "LIFECYCLE_NO_TRANSITIONS");
    }

    [Fact]
    public void DecisionOrdering_BlockedBeatsReviewRequired()
    {
        var bindingReport = new RuntimeBindingReport
        {
            Descriptors = new[]
            {
                new DescriptorBindingReport
                {
                    DescriptorId = TestRef.FullId,
                    DescriptorKind = DescriptorKind.Schema,
                    Status = DescriptorBindingStatus.Unbound
                }
            }
        };

        var topologyDiag = new DescriptorTopologyDiagnostics
        {
            All = new[]
            {
                new DescriptorTopologyDiagnostic(
                    SeverityLevel.Warning,
                    new DiagnosticCode("WARN"),
                    "Topology warning",
                    TestRef,
                    null)
            }
        };

        var request = MakeRequest(
            transitions: new[] { MakeTransition(DescriptorLifecycleOperation.Activate) },
            bindingReport: bindingReport,
            topologyDiagnostics: topologyDiag);

        var report = Service.Evaluate(request);

        // Binding Unbound blocks Activate by default; topology warning is ReviewRequired
        // Blocked beats ReviewRequired
        report.IsBlocked.Should().BeTrue();
    }

    [Fact]
    public void Report_IsAllowed_RequiresReview_IsBlocked_AreConsistent()
    {
        // Test Allowed state
        var allowedRequest = MakeRequest();
        var allowedReport = Service.Evaluate(allowedRequest);
        allowedReport.IsAllowed.Should().BeTrue();
        allowedReport.RequiresReview.Should().BeFalse();
        allowedReport.IsBlocked.Should().BeFalse();

        // Test Blocked state
        var blockedRequest = MakeRequest(
            validationReport: ValidationReport.FromIssues(
                new ValidationIssue(SeverityLevel.Error, "Error")));
        var blockedReport = Service.Evaluate(blockedRequest);
        blockedReport.IsAllowed.Should().BeFalse();
        blockedReport.RequiresReview.Should().BeFalse();
        blockedReport.IsBlocked.Should().BeTrue();
    }

    [Fact]
    public void DoesNotMutateDescriptorsOrReports()
    {
        var originalValidation = ValidationReport.FromIssues(
            new ValidationIssue(SeverityLevel.Warning, "Original"));
        var originalBinding = new RuntimeBindingReport
        {
            Descriptors = new[]
            {
                new DescriptorBindingReport
                {
                    DescriptorId = TestRef.FullId,
                    DescriptorKind = DescriptorKind.Schema,
                    Status = DescriptorBindingStatus.PartiallyBound
                }
            }
        };

        var request = MakeRequest(
            validationReport: originalValidation,
            bindingReport: originalBinding);

        var report = Service.Evaluate(request);

        // Verify inputs unchanged
        originalValidation.Issues.Should().HaveCount(1);
        originalBinding.Descriptors.Should().HaveCount(1);
        originalBinding.Descriptors[0].Status.Should().Be(DescriptorBindingStatus.PartiallyBound);
    }

    [Fact]
    public void ChangeDrivenTransition_SubjectNotInChangeSet_ProducesPackageFinding()
    {
        var otherRef = new DescriptorRef("other", "O1", 1);
        var changeSet = MakeChangeSet(otherRef);

        var impactReport = MakeImpactReport(changeSet);
        var compatReport = MakeCompatibilityReport(changeSet);

        var request = MakeRequest(
            transitions: new[] { MakeTransition(DescriptorLifecycleOperation.Activate) },
            impactReport: impactReport,
            compatibilityReport: compatReport);

        var report = Service.Evaluate(request);

        report.PackageFindings.Should().Contain(f =>
            f.Code == "LIFECYCLE_SUBJECT_NOT_IN_CHANGESET");
    }

    [Fact]
    public void ValidateDraft_SubjectNotInChangeSet_Allowed()
    {
        var otherRef = new DescriptorRef("other", "O1", 1);
        var changeSet = MakeChangeSet(otherRef);

        var impactReport = MakeImpactReport(changeSet);
        var compatReport = MakeCompatibilityReport(changeSet);

        var request = MakeRequest(
            transitions: new[] { MakeTransition(DescriptorLifecycleOperation.ValidateDraft) },
            impactReport: impactReport,
            compatibilityReport: compatReport);

        var report = Service.Evaluate(request);

        report.PackageFindings.Should().NotContain(f =>
            f.Code == "LIFECYCLE_SUBJECT_NOT_IN_CHANGESET");
    }

    [Fact]
    public void Reject_SubjectNotInChangeSet_Allowed()
    {
        var otherRef = new DescriptorRef("other", "O1", 1);
        var changeSet = MakeChangeSet(otherRef);

        var impactReport = MakeImpactReport(changeSet);
        var compatReport = MakeCompatibilityReport(changeSet);

        var request = MakeRequest(
            transitions: new[] { MakeTransition(DescriptorLifecycleOperation.Reject) },
            impactReport: impactReport,
            compatibilityReport: compatReport);

        var report = Service.Evaluate(request);

        report.PackageFindings.Should().NotContain(f =>
            f.Code == "LIFECYCLE_SUBJECT_NOT_IN_CHANGESET");
    }

    [Fact]
    public void BindingIdUnresolvable_ProducesPackageFinding()
    {
        var bindingReport = new RuntimeBindingReport
        {
            Descriptors = new[]
            {
                new DescriptorBindingReport
                {
                    DescriptorId = "nondotid",  // No dot → unresolvable
                    DescriptorKind = DescriptorKind.Schema,
                    Status = DescriptorBindingStatus.RuntimeReady
                }
            }
        };

        var request = MakeRequest(bindingReport: bindingReport);

        var report = Service.Evaluate(request);

        report.PackageFindings.Should().Contain(f =>
            f.Code == "LIFECYCLE_BINDING_ID_UNRESOLVABLE");
    }

    [Fact]
    public void BindingKindMismatch_ProducesPackageFinding()
    {
        // Namespace "event" does not match canonical namespace "schema" for DescriptorKind.Schema
        var bindingReport = new RuntimeBindingReport
        {
            Descriptors = new[]
            {
                new DescriptorBindingReport
                {
                    DescriptorId = "event.T1",
                    DescriptorKind = DescriptorKind.Schema,
                    Status = DescriptorBindingStatus.RuntimeReady
                }
            }
        };

        var request = MakeRequest(bindingReport: bindingReport);

        var report = Service.Evaluate(request);

        report.PackageFindings.Should().Contain(f =>
            f.Code == "LIFECYCLE_BINDING_KIND_MISMATCH");
    }

    [Fact]
    public void BindingVersionAmbiguity_ProducesPackageFinding()
    {
        var bindingReport = new RuntimeBindingReport
        {
            Descriptors = new[]
            {
                new DescriptorBindingReport
                {
                    DescriptorId = "schema.T1",
                    DescriptorKind = DescriptorKind.Schema,
                    Status = DescriptorBindingStatus.RuntimeReady
                },
                new DescriptorBindingReport
                {
                    DescriptorId = "schema.T1",
                    DescriptorKind = DescriptorKind.Schema,
                    Status = DescriptorBindingStatus.Unbound
                }
            }
        };

        var request = MakeRequest(bindingReport: bindingReport);

        var report = Service.Evaluate(request);

        report.PackageFindings.Should().Contain(f =>
            f.Code == "LIFECYCLE_BINDING_VERSION_AMBIGUITY");
    }

    // --- 10.9a Fix 1: Package-level findings upgrade MaxDecision ---

    [Fact]
    public void PackageFinding_ReviewSeverity_UpgradesMaxDecision_ToReviewRequired()
    {
        var impactChangeSet = MakeChangeSet(TestRef);
        var compatChangeSet = MakeChangeSet(new DescriptorRef("other", "O1", 1));

        var impactReport = MakeImpactReport(impactChangeSet);
        var compatReport = MakeCompatibilityReport(compatChangeSet);

        var request = MakeRequest(
            impactReport: impactReport,
            compatibilityReport: compatReport);

        var report = Service.Evaluate(request);

        report.PackageFindings.Should().Contain(f =>
            f.Code == "LIFECYCLE_CHANGESET_MISMATCH" &&
            f.Severity == SeverityLevel.Review);
        report.MaxDecision.Should().Be(DescriptorLifecycleDecisionKind.ReviewRequired);
    }

    [Fact]
    public void PackageFinding_BlockerSeverity_UpgradesMaxDecision_ToBlocked()
    {
        // Validation error produces a per-transition Blocker finding;
        // combined with a package-level Review finding, MaxDecision must be Blocked
        var impactChangeSet = MakeChangeSet(TestRef);
        var compatChangeSet = MakeChangeSet(new DescriptorRef("other", "O1", 1));

        var impactReport = MakeImpactReport(impactChangeSet);
        var compatReport = MakeCompatibilityReport(compatChangeSet);

        var request = MakeRequest(
            validationReport: ValidationReport.FromIssues(
                new ValidationIssue(SeverityLevel.Error, "Error")),
            impactReport: impactReport,
            compatibilityReport: compatReport);

        var report = Service.Evaluate(request);

        report.PackageFindings.Should().Contain(f =>
            f.Code == "LIFECYCLE_CHANGESET_MISMATCH");
        report.MaxDecision.Should().Be(DescriptorLifecycleDecisionKind.Blocked);
    }

    // --- 10.9b Fix 2: Compatibility cross-contamination ---

    [Fact]
    public void CompatibilityBreakingForOtherSubject_DoesNotAffectTransition()
    {
        var otherRef = new DescriptorRef("event", "E1", 1);
        var changeSet = MakeChangeSet(TestRef, otherRef);

        var impactReport = MakeImpactReport(changeSet);
        var compatReport = MakeCompatibilityReport(
            changeSet,
            maxLevel: DescriptorCompatibilityLevel.Breaking,
            findings: new[]
            {
                new DescriptorCompatibilityFinding
                {
                    Subject = otherRef,
                    ChangeKind = DescriptorChangeKind.Removed,
                    Level = DescriptorCompatibilityLevel.Breaking,
                    Kind = DescriptorCompatibilityFindingKind.Structural,
                    RuleId = "TEST_RULE",
                    Message = "Breaking for other"
                }
            });

        var request = MakeRequest(
            transitions: new[] { MakeTransition(DescriptorLifecycleOperation.Activate) },
            impactReport: impactReport,
            compatibilityReport: compatReport);

        var report = Service.Evaluate(request);

        report.IsAllowed.Should().BeTrue();
        report.Decisions.Should().ContainSingle()
            .Which.Findings.Should().NotContain(f =>
                f.Code == "LIFECYCLE_COMPAT_BREAKING");
    }

    // --- 10.9c Fix 3: Namespace/kind mismatch in binding report ---

    [Fact]
    public void BindingNamespaceKindMismatch_ProducesKindMismatchFinding()
    {
        var bindingReport = new RuntimeBindingReport
        {
            Descriptors = new[]
            {
                new DescriptorBindingReport
                {
                    DescriptorId = "workflow.T1",
                    DescriptorKind = DescriptorKind.Event,
                    Status = DescriptorBindingStatus.RuntimeReady
                }
            }
        };

        var request = MakeRequest(bindingReport: bindingReport);

        var report = Service.Evaluate(request);

        report.PackageFindings.Should().Contain(f =>
            f.Code == "LIFECYCLE_BINDING_KIND_MISMATCH");
    }

    [Fact]
    public void BindingNamespaceMatchesKind_NoMismatchFinding()
    {
        var bindingReport = new RuntimeBindingReport
        {
            Descriptors = new[]
            {
                new DescriptorBindingReport
                {
                    DescriptorId = "capability.C1",
                    DescriptorKind = DescriptorKind.Capability,
                    Status = DescriptorBindingStatus.RuntimeReady
                }
            }
        };

        var request = MakeRequest(bindingReport: bindingReport);

        var report = Service.Evaluate(request);

        report.PackageFindings.Should().NotContain(f =>
            f.Code == "LIFECYCLE_BINDING_KIND_MISMATCH");
    }

    // --- 10.10 DI ---

    [Fact]
    public void DI_RegistersLifecycleGovernanceService()
    {
        var services = new ServiceCollection();
        services.AddDescriptorLifecycleGovernance();

        var sp = services.BuildServiceProvider();
        var service = sp.GetService<IDescriptorLifecycleGovernanceService>();

        service.Should().NotBeNull();
        service.Should().BeOfType<DefaultDescriptorLifecycleGovernanceService>();
    }
}
