using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata.DescriptorLifecycle;

public sealed class DefaultDescriptorLifecycleGovernanceService
    : IDescriptorLifecycleGovernanceService
{
    private static readonly Dictionary<DescriptorKind, string> KindToCanonicalNamespace = new()
    {
        [DescriptorKind.Schema] = "schema",
        [DescriptorKind.Capability] = "capability",
        [DescriptorKind.Event] = "event",
        [DescriptorKind.Workflow] = "workflow",
        [DescriptorKind.Form] = "form",
        [DescriptorKind.HumanTask] = "humantask"
    };

    public DescriptorLifecycleGovernanceReport Evaluate(
        DescriptorLifecycleGovernanceRequest request)
    {
        var options = request.Options;
        var packageFindings = new List<DescriptorLifecycleFinding>();
        var decisions = new List<DescriptorLifecycleDecision>();

        // Consistency checks
        ValidateReportConsistency(request, packageFindings);

        // Validation signals
        var validationFindings = EvaluateValidation(request.ValidationReport, options);

        // Topology signals
        var topologyFindings = EvaluateTopology(request.TopologyDiagnostics, options);

        // Impact signals
        var impactFindings = EvaluateImpact(request.ImpactReport, options);

        // Compatibility diagnostic signals
        var compatDiagFindings = EvaluateCompatibilityDiagnostics(
            request.CompatibilityReport, options);

        // Per-transition evaluation
        foreach (var transition in request.Transitions)
        {
            var findings = new List<DescriptorLifecycleFinding>();

            // Validation findings apply to all transitions
            findings.AddRange(validationFindings);

            // Topology findings apply to all transitions
            findings.AddRange(topologyFindings);

            // Impact findings
            findings.AddRange(impactFindings);

            // Compatibility diagnostic findings
            findings.AddRange(compatDiagFindings);

            // Binding findings (operation-specific)
            findings.AddRange(EvaluateBinding(
                transition, request.BindingReport, options));

            // Compatibility findings (operation-specific)
            findings.AddRange(EvaluateCompatibility(
                transition, request.CompatibilityReport, options));

            var decision = ComputeDecision(findings);
            decisions.Add(new DescriptorLifecycleDecision
            {
                Transition = transition,
                Decision = decision,
                Findings = findings
            });
        }

        // Empty transitions → allowed with info finding
        if (request.Transitions.Count == 0)
        {
            packageFindings.Add(new DescriptorLifecycleFinding
            {
                Severity = SeverityLevel.Info,
                Code = new DiagnosticCode("LIFECYCLE_NO_TRANSITIONS"),
                Message = "No transitions requested.",
                Source = "policy"
            });
        }

        var maxDecision = decisions.Count == 0
            ? DescriptorLifecycleDecisionKind.Allowed
            : decisions.Max(d => d.Decision);

        // Package-level findings with Review or Blocker severity must upgrade MaxDecision
        var packageMaxSeverity = packageFindings.Count == 0
            ? SeverityLevel.Info
            : packageFindings.Max(f => f.Severity);

        if (packageMaxSeverity == SeverityLevel.Blocker)
            maxDecision = DescriptorLifecycleDecisionKind.Blocked;
        else if (packageMaxSeverity == SeverityLevel.Review
                 && maxDecision == DescriptorLifecycleDecisionKind.Allowed)
            maxDecision = DescriptorLifecycleDecisionKind.ReviewRequired;

        return new DescriptorLifecycleGovernanceReport
        {
            Decisions = decisions,
            MaxDecision = maxDecision,
            PackageFindings = packageFindings
        };
    }

    private static void ValidateReportConsistency(
        DescriptorLifecycleGovernanceRequest request,
        List<DescriptorLifecycleFinding> packageFindings)
    {
        // 1. ChangeSet mismatch between impact and compatibility reports
        var impactChanges = request.ImpactReport.ChangeSet.Changes
            .Select(c => (c.Ref, c.Kind))
            .ToList();
        var compatChanges = request.CompatibilityReport.ChangeSet.Changes
            .Select(c => (c.Ref, c.Kind))
            .ToList();

        if (!impactChanges.SequenceEqual(compatChanges))
        {
            packageFindings.Add(new DescriptorLifecycleFinding
            {
                Severity = SeverityLevel.Review,
                Code = new DiagnosticCode("LIFECYCLE_CHANGESET_MISMATCH"),
                Message = "Impact and compatibility reports have different change sets.",
                Source = "policy"
            });
        }

        // 2. Change-driven transitions must appear in change set
        var changeDrivenOperations = new HashSet<DescriptorLifecycleOperation>
        {
            DescriptorLifecycleOperation.SubmitForReview,
            DescriptorLifecycleOperation.Approve,
            DescriptorLifecycleOperation.Activate,
            DescriptorLifecycleOperation.Deprecate,
            DescriptorLifecycleOperation.Retire
        };

        var changeSetRefs = request.ImpactReport.ChangeSet.Changes
            .Select(c => c.Ref)
            .ToHashSet();

        foreach (var transition in request.Transitions)
        {
            if (changeDrivenOperations.Contains(transition.Operation)
                && !changeSetRefs.Contains(transition.Subject))
            {
                packageFindings.Add(new DescriptorLifecycleFinding
                {
                    Severity = SeverityLevel.Review,
                    Code = new DiagnosticCode("LIFECYCLE_SUBJECT_NOT_IN_CHANGESET"),
                    Message = $"Transition subject {transition.Subject} with operation " +
                              $"{transition.Operation} is not in the change set.",
                    Source = "policy",
                    Subject = transition.Subject
                });
            }
        }

        // 3. Binding report matching
        ValidateBindingReportMatching(request, packageFindings);
    }

    private static void ValidateBindingReportMatching(
        DescriptorLifecycleGovernanceRequest request,
        List<DescriptorLifecycleFinding> packageFindings)
    {
        // Check for unresolvable DescriptorIds
        foreach (var br in request.BindingReport.Descriptors)
        {
            if (string.IsNullOrEmpty(br.DescriptorId))
            {
                packageFindings.Add(new DescriptorLifecycleFinding
                {
                    Severity = SeverityLevel.Review,
                    Code = new DiagnosticCode("LIFECYCLE_BINDING_ID_UNRESOLVABLE"),
                    Message = "Binding report has empty or null DescriptorId.",
                    Source = "binding"
                });
                continue;
            }

            // FullId format: "Namespace.Id" — must contain at least one dot
            var dotIndex = br.DescriptorId.IndexOf('.');
            if (dotIndex <= 0 || dotIndex == br.DescriptorId.Length - 1)
            {
                packageFindings.Add(new DescriptorLifecycleFinding
                {
                    Severity = SeverityLevel.Review,
                    Code = new DiagnosticCode("LIFECYCLE_BINDING_ID_UNRESOLVABLE"),
                    Message = $"Binding report DescriptorId '{br.DescriptorId}' " +
                              "cannot be parsed as Namespace.Id.",
                    Source = "binding"
                });
                continue;
            }

            // Namespace must match DescriptorKind's canonical namespace
            var ns = br.DescriptorId.Substring(0, dotIndex);
            if (KindToCanonicalNamespace.TryGetValue(br.DescriptorKind, out var expected)
                && !string.Equals(ns, expected, StringComparison.OrdinalIgnoreCase))
            {
                packageFindings.Add(new DescriptorLifecycleFinding
                {
                    Severity = SeverityLevel.Review,
                    Code = new DiagnosticCode("LIFECYCLE_BINDING_KIND_MISMATCH"),
                    Message = $"Binding report namespace '{ns}' does not match " +
                              $"canonical namespace '{expected}' for DescriptorKind {br.DescriptorKind}.",
                    Source = "binding"
                });
            }
        }

        // Check for kind mismatch against transition subjects
        // DescriptorRef lacks Kind; check DescriptorKind consistency per DescriptorId instead
        var kindGroups = request.BindingReport.Descriptors
            .Where(d => !string.IsNullOrEmpty(d.DescriptorId))
            .GroupBy(d => d.DescriptorId)
            .Where(g => g.Select(d => d.DescriptorKind).Distinct().Count() > 1);

        foreach (var group in kindGroups)
        {
            packageFindings.Add(new DescriptorLifecycleFinding
            {
                Severity = SeverityLevel.Review,
                Code = new DiagnosticCode("LIFECYCLE_BINDING_KIND_MISMATCH"),
                Message = $"Binding report has multiple DescriptorKind values for " +
                          $"DescriptorId '{group.Key}'.",
                Source = "binding"
            });
        }

        // Check for version ambiguity
        var groupsByVersion = request.BindingReport.Descriptors
            .GroupBy(d => (d.DescriptorKind, d.DescriptorId))
            .Where(g => g.Select(d => d.Status).Distinct().Count() > 1);

        foreach (var group in groupsByVersion)
        {
            packageFindings.Add(new DescriptorLifecycleFinding
            {
                Severity = SeverityLevel.Review,
                Code = new DiagnosticCode("LIFECYCLE_BINDING_VERSION_AMBIGUITY"),
                Message = $"Binding report has multiple statuses for " +
                          $"{group.Key.DescriptorKind}/{group.Key.DescriptorId}.",
                Source = "binding"
            });
        }
    }

    private static List<DescriptorLifecycleFinding> EvaluateValidation(
        ValidationReport report,
        DescriptorLifecycleGovernanceOptions options)
    {
        var findings = new List<DescriptorLifecycleFinding>();

        foreach (var issue in report.Issues)
        {
            if (issue.Severity == SeverityLevel.Error)
            {
                findings.Add(new DescriptorLifecycleFinding
                {
                    Severity = SeverityLevel.Blocker,
                    Code = new DiagnosticCode("LIFECYCLE_VALIDATION_ERROR"),
                    Message = issue.Message,
                    Source = "validation"
                });
            }
            else if (issue.Severity == SeverityLevel.Warning)
            {
                if (options.TreatValidationWarningsAsReviewRequired)
                {
                    findings.Add(new DescriptorLifecycleFinding
                    {
                        Severity = SeverityLevel.Review,
                        Code = new DiagnosticCode("LIFECYCLE_VALIDATION_WARNING"),
                        Message = issue.Message,
                        Source = "validation"
                    });
                }
                else
                {
                    findings.Add(new DescriptorLifecycleFinding
                    {
                        Severity = SeverityLevel.Warning,
                        Code = new DiagnosticCode("LIFECYCLE_VALIDATION_WARNING"),
                        Message = issue.Message,
                        Source = "validation"
                    });
                }
            }
            else
            {
                findings.Add(new DescriptorLifecycleFinding
                {
                    Severity = SeverityLevel.Info,
                    Code = new DiagnosticCode("LIFECYCLE_VALIDATION_INFO"),
                    Message = issue.Message,
                    Source = "validation"
                });
            }
        }

        return findings;
    }

    private static List<DescriptorLifecycleFinding> EvaluateBinding(
        DescriptorLifecycleTransition transition,
        RuntimeBindingReport bindingReport,
        DescriptorLifecycleGovernanceOptions options)
    {
        var findings = new List<DescriptorLifecycleFinding>();

        // Find binding report for this transition's subject
        var bindingForSubject = bindingReport.Descriptors
            .FirstOrDefault(d => d.DescriptorId == transition.Subject.FullId);

        if (bindingForSubject is null)
            return findings; // No binding data for this subject — skip

        var status = bindingForSubject.Status;

        switch (transition.Operation)
        {
            case DescriptorLifecycleOperation.ValidateDraft:
                // Report findings but don't block
                if (status != DescriptorBindingStatus.RuntimeReady)
                {
                    findings.Add(new DescriptorLifecycleFinding
                    {
                        Severity = SeverityLevel.Info,
                        Code = new DiagnosticCode("LIFECYCLE_BINDING_NOT_READY"),
                        Message = $"Descriptor binding status is {status}.",
                        Source = "binding",
                        Subject = transition.Subject
                    });
                }
                break;

            case DescriptorLifecycleOperation.SubmitForReview:
                EvaluateBindingForSubmitForReview(status, transition, options, findings);
                break;

            case DescriptorLifecycleOperation.Approve:
                EvaluateBindingForApprove(status, transition, findings);
                break;

            case DescriptorLifecycleOperation.Activate:
                EvaluateBindingForActivate(status, transition, options, findings);
                break;

            case DescriptorLifecycleOperation.Deprecate:
            case DescriptorLifecycleOperation.Retire:
                // Binding issues reported but not blocking; governance driven by
                // compatibility/impact/topology signals
                if (status != DescriptorBindingStatus.RuntimeReady)
                {
                    findings.Add(new DescriptorLifecycleFinding
                    {
                        Severity = SeverityLevel.Info,
                        Code = new DiagnosticCode("LIFECYCLE_BINDING_NOT_READY"),
                        Message = $"Descriptor binding status is {status}.",
                        Source = "binding",
                        Subject = transition.Subject
                    });
                }
                break;

            case DescriptorLifecycleOperation.Reject:
                // Should not require runtime readiness
                break;
        }

        return findings;
    }

    private static void EvaluateBindingForSubmitForReview(
        DescriptorBindingStatus status,
        DescriptorLifecycleTransition transition,
        DescriptorLifecycleGovernanceOptions options,
        List<DescriptorLifecycleFinding> findings)
    {
        switch (status)
        {
            case DescriptorBindingStatus.Invalid:
                findings.Add(new DescriptorLifecycleFinding
                {
                    Severity = SeverityLevel.Blocker,
                    Code = new DiagnosticCode("LIFECYCLE_BINDING_INVALID"),
                    Message = "Binding status is Invalid.",
                    Source = "binding",
                    Subject = transition.Subject
                });
                break;

            case DescriptorBindingStatus.Unbound:
                if (options.BlockSubmitForReviewOnUnboundBinding)
                {
                    findings.Add(new DescriptorLifecycleFinding
                    {
                        Severity = SeverityLevel.Blocker,
                        Code = new DiagnosticCode("LIFECYCLE_BINDING_UNBOUND"),
                        Message = "Binding status is Unbound.",
                        Source = "binding",
                        Subject = transition.Subject
                    });
                }
                else
                {
                    findings.Add(new DescriptorLifecycleFinding
                    {
                        Severity = SeverityLevel.Review,
                        Code = new DiagnosticCode("LIFECYCLE_BINDING_UNBOUND"),
                        Message = "Binding status is Unbound.",
                        Source = "binding",
                        Subject = transition.Subject
                    });
                }
                break;

            case DescriptorBindingStatus.Unsupported:
                if (options.BlockSubmitForReviewOnUnsupportedBinding)
                {
                    findings.Add(new DescriptorLifecycleFinding
                    {
                        Severity = SeverityLevel.Blocker,
                        Code = new DiagnosticCode("LIFECYCLE_BINDING_UNSUPPORTED"),
                        Message = "Binding status is Unsupported.",
                        Source = "binding",
                        Subject = transition.Subject
                    });
                }
                else if (options.TreatSubmitForReviewUnsupportedBindingAsReviewRequired)
                {
                    findings.Add(new DescriptorLifecycleFinding
                    {
                        Severity = SeverityLevel.Review,
                        Code = new DiagnosticCode("LIFECYCLE_BINDING_UNSUPPORTED"),
                        Message = "Binding status is Unsupported.",
                        Source = "binding",
                        Subject = transition.Subject
                    });
                }
                break;

            case DescriptorBindingStatus.PartiallyBound:
                if (options.TreatSubmitForReviewPartialBindingAsReviewRequired)
                {
                    findings.Add(new DescriptorLifecycleFinding
                    {
                        Severity = SeverityLevel.Review,
                        Code = new DiagnosticCode("LIFECYCLE_BINDING_PARTIAL"),
                        Message = "Binding status is PartiallyBound.",
                        Source = "binding",
                        Subject = transition.Subject
                    });
                }
                break;

            case DescriptorBindingStatus.RuntimeReady:
                // Allowed — no finding needed
                break;
        }
    }

    private static void EvaluateBindingForApprove(
        DescriptorBindingStatus status,
        DescriptorLifecycleTransition transition,
        List<DescriptorLifecycleFinding> findings)
    {
        switch (status)
        {
            case DescriptorBindingStatus.Invalid:
                findings.Add(new DescriptorLifecycleFinding
                {
                    Severity = SeverityLevel.Blocker,
                    Code = new DiagnosticCode("LIFECYCLE_BINDING_INVALID"),
                    Message = "Binding status is Invalid.",
                    Source = "binding",
                    Subject = transition.Subject
                });
                break;

            case DescriptorBindingStatus.Unbound:
            case DescriptorBindingStatus.Unsupported:
            case DescriptorBindingStatus.PartiallyBound:
                findings.Add(new DescriptorLifecycleFinding
                {
                    Severity = SeverityLevel.Review,
                    Code = new DiagnosticCode($"LIFECYCLE_BINDING_{status.ToString().ToUpperInvariant()}"),
                    Message = $"Binding status is {status}.",
                    Source = "binding",
                    Subject = transition.Subject
                });
                break;

            case DescriptorBindingStatus.RuntimeReady:
                break;
        }
    }

    private static void EvaluateBindingForActivate(
        DescriptorBindingStatus status,
        DescriptorLifecycleTransition transition,
        DescriptorLifecycleGovernanceOptions options,
        List<DescriptorLifecycleFinding> findings)
    {
        switch (status)
        {
            case DescriptorBindingStatus.Invalid:
                findings.Add(new DescriptorLifecycleFinding
                {
                    Severity = SeverityLevel.Blocker,
                    Code = new DiagnosticCode("LIFECYCLE_BINDING_INVALID"),
                    Message = "Binding status is Invalid.",
                    Source = "binding",
                    Subject = transition.Subject
                });
                break;

            case DescriptorBindingStatus.Unbound:
                if (options.BlockActivateOnUnboundBinding)
                {
                    findings.Add(new DescriptorLifecycleFinding
                    {
                        Severity = SeverityLevel.Blocker,
                        Code = new DiagnosticCode("LIFECYCLE_BINDING_UNBOUND"),
                        Message = "Binding status is Unbound.",
                        Source = "binding",
                        Subject = transition.Subject
                    });
                }
                break;

            case DescriptorBindingStatus.Unsupported:
                if (options.BlockActivateOnUnsupportedBinding)
                {
                    findings.Add(new DescriptorLifecycleFinding
                    {
                        Severity = SeverityLevel.Blocker,
                        Code = new DiagnosticCode("LIFECYCLE_BINDING_UNSUPPORTED"),
                        Message = "Binding status is Unsupported.",
                        Source = "binding",
                        Subject = transition.Subject
                    });
                }
                break;

            case DescriptorBindingStatus.PartiallyBound:
                if (options.TreatBindingPartialAsReviewRequired)
                {
                    findings.Add(new DescriptorLifecycleFinding
                    {
                        Severity = SeverityLevel.Review,
                        Code = new DiagnosticCode("LIFECYCLE_BINDING_PARTIAL"),
                        Message = "Binding status is PartiallyBound.",
                        Source = "binding",
                        Subject = transition.Subject
                    });
                }
                break;

            case DescriptorBindingStatus.RuntimeReady:
                break;
        }
    }

    private static List<DescriptorLifecycleFinding> EvaluateTopology(
        DescriptorTopologyDiagnostics topologyDiagnostics,
        DescriptorLifecycleGovernanceOptions options)
    {
        var findings = new List<DescriptorLifecycleFinding>();

        foreach (var diag in topologyDiagnostics.All)
        {
            if (diag.Severity == SeverityLevel.Error)
            {
                findings.Add(new DescriptorLifecycleFinding
                {
                    Severity = options.BlockOnTopologyErrors
                        ? SeverityLevel.Blocker
                        : SeverityLevel.Review,
                    Code = new DiagnosticCode("LIFECYCLE_TOPOLOGY_ERROR"),
                    Message = diag.Message,
                    Source = "topology",
                    Subject = diag.Subject
                });
            }
            else if (diag.Severity == SeverityLevel.Warning)
            {
                findings.Add(new DescriptorLifecycleFinding
                {
                    Severity = SeverityLevel.Review,
                    Code = new DiagnosticCode("LIFECYCLE_TOPOLOGY_WARNING"),
                    Message = diag.Message,
                    Source = "topology",
                    Subject = diag.Subject
                });
            }
        }

        return findings;
    }

    private static List<DescriptorLifecycleFinding> EvaluateImpact(
        DescriptorImpactAnalysisReport impactReport,
        DescriptorLifecycleGovernanceOptions options)
    {
        var findings = new List<DescriptorLifecycleFinding>();

        // Impact diagnostics
        foreach (var diag in impactReport.Diagnostics)
        {
            if (diag.Severity == SeverityLevel.Error)
            {
                findings.Add(new DescriptorLifecycleFinding
                {
                    Severity = options.BlockOnImpactDiagnosticsErrors
                        ? SeverityLevel.Blocker
                        : SeverityLevel.Review,
                    Code = new DiagnosticCode("LIFECYCLE_IMPACT_DIAGNOSTIC_ERROR"),
                    Message = diag.Message,
                    Source = "impact",
                    Subject = diag.Subject
                });
            }
            else if (diag.Severity == SeverityLevel.Warning)
            {
                findings.Add(new DescriptorLifecycleFinding
                {
                    Severity = SeverityLevel.Review,
                    Code = new DiagnosticCode("LIFECYCLE_IMPACT_DIAGNOSTIC_WARNING"),
                    Message = diag.Message,
                    Source = "impact",
                    Subject = diag.Subject
                });
            }
        }

        // Impact severity threshold
        if (impactReport.MaxSeverity >= options.ReviewRequiredImpactThreshold)
        {
            // Important: impact severity triggers review but must NOT be labeled breaking
            findings.Add(new DescriptorLifecycleFinding
            {
                Severity = SeverityLevel.Review,
                Code = new DiagnosticCode("LIFECYCLE_IMPACT_SEVERITY_THRESHOLD"),
                Message = $"Impact severity {impactReport.MaxSeverity} meets or exceeds " +
                          $"threshold {options.ReviewRequiredImpactThreshold}.",
                Source = "impact"
            });
        }

        return findings;
    }

    private static List<DescriptorLifecycleFinding> EvaluateCompatibility(
        DescriptorLifecycleTransition transition,
        DescriptorCompatibilityReport compatibilityReport,
        DescriptorLifecycleGovernanceOptions options)
    {
        var findings = new List<DescriptorLifecycleFinding>();

        var findingsForSubject = compatibilityReport.Findings
            .Where(f => f.Subject == transition.Subject)
            .ToList();

        foreach (var compatFinding in findingsForSubject)
        {
            AddCompatibilityFinding(
                compatFinding.Level, transition, options, findings);
        }

        return findings;
    }

    private static void AddCompatibilityFinding(
        DescriptorCompatibilityLevel level,
        DescriptorLifecycleTransition transition,
        DescriptorLifecycleGovernanceOptions options,
        List<DescriptorLifecycleFinding> findings)
    {
        switch (level)
        {
            case DescriptorCompatibilityLevel.Breaking:
                if (transition.Operation == DescriptorLifecycleOperation.Activate
                    && options.BlockActivateOnBreakingCompatibility)
                {
                    findings.Add(new DescriptorLifecycleFinding
                    {
                        Severity = SeverityLevel.Blocker,
                        Code = new DiagnosticCode("LIFECYCLE_COMPAT_BREAKING"),
                        Message = "Breaking compatibility change detected.",
                        Source = "compatibility",
                        Subject = transition.Subject
                    });
                }
                else if (options.TreatBreakingCompatibilityAsReviewRequired)
                {
                    findings.Add(new DescriptorLifecycleFinding
                    {
                        Severity = SeverityLevel.Review,
                        Code = new DiagnosticCode("LIFECYCLE_COMPAT_BREAKING"),
                        Message = "Breaking compatibility change detected.",
                        Source = "compatibility",
                        Subject = transition.Subject
                    });
                }
                break;

            case DescriptorCompatibilityLevel.SecuritySensitive:
                if (options.TreatSecuritySensitiveAsReviewRequired)
                {
                    findings.Add(new DescriptorLifecycleFinding
                    {
                        Severity = SeverityLevel.Review,
                        Code = new DiagnosticCode("LIFECYCLE_COMPAT_SECURITY_SENSITIVE"),
                        Message = "Security-sensitive compatibility change detected.",
                        Source = "compatibility",
                        Subject = transition.Subject
                    });
                }
                break;

            case DescriptorCompatibilityLevel.Risky:
                if (options.TreatRiskyCompatibilityAsReviewRequired)
                {
                    findings.Add(new DescriptorLifecycleFinding
                    {
                        Severity = SeverityLevel.Review,
                        Code = new DiagnosticCode("LIFECYCLE_COMPAT_RISKY"),
                        Message = "Risky compatibility change detected.",
                        Source = "compatibility",
                        Subject = transition.Subject
                    });
                }
                break;

            case DescriptorCompatibilityLevel.Unsupported:
                if (options.TreatCompatibilityUnsupportedAsReviewRequired)
                {
                    findings.Add(new DescriptorLifecycleFinding
                    {
                        Severity = SeverityLevel.Review,
                        Code = new DiagnosticCode("LIFECYCLE_COMPAT_UNSUPPORTED"),
                        Message = "Unsupported compatibility change detected. " +
                                  "This indicates insufficient rule knowledge, not a breaking change.",
                        Source = "compatibility",
                        Subject = transition.Subject
                    });
                }
                break;

            case DescriptorCompatibilityLevel.Compatible:
                // Allowed — no finding needed
                break;
        }
    }

    private static List<DescriptorLifecycleFinding> EvaluateCompatibilityDiagnostics(
        DescriptorCompatibilityReport compatibilityReport,
        DescriptorLifecycleGovernanceOptions options)
    {
        var findings = new List<DescriptorLifecycleFinding>();

        foreach (var diag in compatibilityReport.Diagnostics)
        {
            if (diag.Severity == SeverityLevel.Error)
            {
                findings.Add(new DescriptorLifecycleFinding
                {
                    Severity = options.BlockOnCompatibilityDiagnosticsErrors
                        ? SeverityLevel.Blocker
                        : SeverityLevel.Review,
                    Code = new DiagnosticCode("LIFECYCLE_COMPAT_DIAGNOSTIC_ERROR"),
                    Message = diag.Message,
                    Source = "compatibility",
                    Subject = diag.Subject
                });
            }
            else if (diag.Severity == SeverityLevel.Warning)
            {
                findings.Add(new DescriptorLifecycleFinding
                {
                    Severity = SeverityLevel.Review,
                    Code = new DiagnosticCode("LIFECYCLE_COMPAT_DIAGNOSTIC_WARNING"),
                    Message = diag.Message,
                    Source = "compatibility",
                    Subject = diag.Subject
                });
            }
        }

        return findings;
    }

    private static DescriptorLifecycleDecisionKind ComputeDecision(
        IReadOnlyList<DescriptorLifecycleFinding> findings)
    {
        if (findings.Any(f => f.Severity == SeverityLevel.Blocker))
            return DescriptorLifecycleDecisionKind.Blocked;

        if (findings.Any(f => f.Severity == SeverityLevel.Review))
            return DescriptorLifecycleDecisionKind.ReviewRequired;

        return DescriptorLifecycleDecisionKind.Allowed;
    }
}
