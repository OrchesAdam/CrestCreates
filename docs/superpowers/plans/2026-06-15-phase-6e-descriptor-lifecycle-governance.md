# Phase 6e — Descriptor Lifecycle Governance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement a stateless descriptor lifecycle governance service that evaluates whether requested descriptor transitions are Allowed, ReviewRequired, or Blocked based on existing analysis reports.

**Architecture:** Pure-function governance gate consuming 5 existing reports (validation, binding, topology, impact, compatibility). Operation-specific policy mapping with configurable options. No state mutation, no persistence, no recomputation of prior-phase analysis.

**Tech Stack:** .NET 10, xUnit, FluentAssertions, Moq

**Spec:** `docs/superpowers/specs/2026-06-15-phase-6e-descriptor-lifecycle-governance-design.md`

---

## File Structure

### New Abstractions (10 files)

```
framework/src/CrestCreates.Metadata.Abstractions/DescriptorLifecycle/
  DescriptorLifecycleOperation.cs          — 7-value enum
  DescriptorLifecycleDecisionKind.cs       — 3-value enum (Allowed/ReviewRequired/Blocked)
  DescriptorLifecycleFindingSeverity.cs    — 4-value enum (Info/Warning/Review/Blocker)
  DescriptorLifecycleTransition.cs         — record: Subject + Operation + FromState/ToState/Reason
  DescriptorLifecycleFinding.cs            — record: Severity + Code + Message + Subject/Source/RelatedRefs/SuggestedAction
  DescriptorLifecycleDecision.cs           — record: Transition + Decision + Findings
  DescriptorLifecycleGovernanceReport.cs   — record: Decisions + MaxDecision + PackageFindings + convenience bools
  DescriptorLifecycleGovernanceOptions.cs  — record: 17 configurable policy flags
  DescriptorLifecycleGovernanceRequest.cs  — record: Transitions + 5 reports + Options
  IDescriptorLifecycleGovernanceService.cs — interface: Evaluate(request) → report
```

### New Implementation (1 file)

```
framework/src/CrestCreates.Metadata/DescriptorLifecycle/
  DefaultDescriptorLifecycleGovernanceService.cs  — stateless singleton, ~300 lines
```

### Modified Files (1 file)

```
framework/src/CrestCreates.Metadata/MetadataServiceCollectionExtensions.cs  — add AddDescriptorLifecycleGovernance()
```

### Tests (1 file)

```
framework/test/CrestCreates.Metadata.Tests/DescriptorLifecycle/
  DescriptorLifecycleGovernanceServiceTests.cs  — 43 tests
```

---

## Task 1: Enum Types

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorLifecycle/DescriptorLifecycleOperation.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorLifecycle/DescriptorLifecycleDecisionKind.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorLifecycle/DescriptorLifecycleFindingSeverity.cs`

- [ ] **Step 1: Create DescriptorLifecycleOperation.cs**

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

public enum DescriptorLifecycleOperation
{
    ValidateDraft,
    SubmitForReview,
    Approve,
    Activate,
    Deprecate,
    Retire,
    Reject
}
```

- [ ] **Step 2: Create DescriptorLifecycleDecisionKind.cs**

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

public enum DescriptorLifecycleDecisionKind
{
    Allowed,
    ReviewRequired,
    Blocked
}
```

- [ ] **Step 3: Create DescriptorLifecycleFindingSeverity.cs**

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

public enum DescriptorLifecycleFindingSeverity
{
    Info,
    Warning,
    Review,
    Blocker
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Metadata.Abstractions`
Expected: 0 errors

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/DescriptorLifecycle/
git commit -m "feat(lifecycle): add DescriptorLifecycle enums — Operation, DecisionKind, FindingSeverity"
```

---

## Task 2: Record Types — Transition, Finding, Decision

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorLifecycle/DescriptorLifecycleTransition.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorLifecycle/DescriptorLifecycleFinding.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorLifecycle/DescriptorLifecycleDecision.cs`

- [ ] **Step 1: Create DescriptorLifecycleTransition.cs**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

public sealed record DescriptorLifecycleTransition
{
    public required DescriptorRef Subject { get; init; }
    public required DescriptorLifecycleOperation Operation { get; init; }
    public DescriptorState? FromState { get; init; }
    public DescriptorState? ToState { get; init; }
    public string? Reason { get; init; }
}
```

- [ ] **Step 2: Create DescriptorLifecycleFinding.cs**

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

public sealed record DescriptorLifecycleFinding
{
    public required DescriptorLifecycleFindingSeverity Severity { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public DescriptorRef? Subject { get; init; }
    public string? Source { get; init; }
    public IReadOnlyList<DescriptorRef> RelatedRefs { get; init; }
        = Array.Empty<DescriptorRef>();
    public string? SuggestedAction { get; init; }
}
```

- [ ] **Step 3: Create DescriptorLifecycleDecision.cs**

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

public sealed record DescriptorLifecycleDecision
{
    public required DescriptorLifecycleTransition Transition { get; init; }
    public required DescriptorLifecycleDecisionKind Decision { get; init; }
    public required IReadOnlyList<DescriptorLifecycleFinding> Findings { get; init; }
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Metadata.Abstractions`
Expected: 0 errors

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/DescriptorLifecycle/
git commit -m "feat(lifecycle): add DescriptorLifecycle record types — Transition, Finding, Decision"
```

---

## Task 3: Record Types — Report, Options, Request, Interface

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorLifecycle/DescriptorLifecycleGovernanceReport.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorLifecycle/DescriptorLifecycleGovernanceOptions.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorLifecycle/DescriptorLifecycleGovernanceRequest.cs`
- Create: `framework/src/CrestCreates.Metadata.Abstractions/DescriptorLifecycle/IDescriptorLifecycleGovernanceService.cs`

- [ ] **Step 1: Create DescriptorLifecycleGovernanceReport.cs**

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

public sealed record DescriptorLifecycleGovernanceReport
{
    public required IReadOnlyList<DescriptorLifecycleDecision> Decisions { get; init; }
    public required DescriptorLifecycleDecisionKind MaxDecision { get; init; }
    public required IReadOnlyList<DescriptorLifecycleFinding> PackageFindings { get; init; }

    public bool IsAllowed => MaxDecision == DescriptorLifecycleDecisionKind.Allowed;
    public bool RequiresReview => MaxDecision == DescriptorLifecycleDecisionKind.ReviewRequired;
    public bool IsBlocked => MaxDecision == DescriptorLifecycleDecisionKind.Blocked;
}
```

- [ ] **Step 2: Create DescriptorLifecycleGovernanceOptions.cs**

```csharp
using CrestCreates.Metadata.Abstractions.DescriptorImpact;

namespace CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

public sealed record DescriptorLifecycleGovernanceOptions
{
    // Validation
    public bool TreatValidationWarningsAsReviewRequired { get; init; } = false;

    // Binding — SubmitForReview
    public bool BlockSubmitForReviewOnUnboundBinding { get; init; } = false;
    public bool BlockSubmitForReviewOnUnsupportedBinding { get; init; } = false;
    public bool TreatSubmitForReviewUnsupportedBindingAsReviewRequired { get; init; } = true;
    public bool TreatSubmitForReviewPartialBindingAsReviewRequired { get; init; } = true;

    // Binding — Activate
    public bool BlockActivateOnUnboundBinding { get; init; } = true;
    public bool BlockActivateOnUnsupportedBinding { get; init; } = true;
    public bool TreatBindingPartialAsReviewRequired { get; init; } = false;

    // Compatibility
    public bool TreatBreakingCompatibilityAsReviewRequired { get; init; } = true;
    public bool TreatSecuritySensitiveAsReviewRequired { get; init; } = true;
    public bool TreatRiskyCompatibilityAsReviewRequired { get; init; } = true;
    public bool TreatCompatibilityUnsupportedAsReviewRequired { get; init; } = true;
    public bool BlockActivateOnBreakingCompatibility { get; init; } = false;

    // Impact
    public DescriptorImpactSeverity ReviewRequiredImpactThreshold { get; init; }
        = DescriptorImpactSeverity.Critical;

    // Diagnostics
    public bool BlockOnTopologyErrors { get; init; } = true;
    public bool BlockOnImpactDiagnosticsErrors { get; init; } = true;
    public bool BlockOnCompatibilityDiagnosticsErrors { get; init; } = true;
}
```

- [ ] **Step 3: Create DescriptorLifecycleGovernanceRequest.cs**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

public sealed record DescriptorLifecycleGovernanceRequest
{
    public required IReadOnlyList<DescriptorLifecycleTransition> Transitions { get; init; }
    public required ValidationReport ValidationReport { get; init; }
    public required RuntimeBindingReport BindingReport { get; init; }
    public required DescriptorTopologyDiagnostics TopologyDiagnostics { get; init; }
    public required DescriptorImpactAnalysisReport ImpactReport { get; init; }
    public required DescriptorCompatibilityReport CompatibilityReport { get; init; }
    public DescriptorLifecycleGovernanceOptions Options { get; init; } = new();
}
```

- [ ] **Step 4: Create IDescriptorLifecycleGovernanceService.cs**

```csharp
namespace CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

public interface IDescriptorLifecycleGovernanceService
{
    DescriptorLifecycleGovernanceReport Evaluate(
        DescriptorLifecycleGovernanceRequest request);
}
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Metadata.Abstractions`
Expected: 0 errors

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/DescriptorLifecycle/
git commit -m "feat(lifecycle): add Report, Options, Request, and IDescriptorLifecycleGovernanceService"
```

---

## Task 4: DefaultDescriptorLifecycleGovernanceService

**Files:**
- Create: `framework/src/CrestCreates.Metadata/DescriptorLifecycle/DefaultDescriptorLifecycleGovernanceService.cs`

This is the core implementation. It is a single stateless class that:

1. Evaluates validation signals → findings
2. Evaluates binding signals per operation → findings
3. Evaluates topology signals → findings
4. Evaluates impact signals → findings
5. Evaluates compatibility signals per operation → findings
6. Runs consistency checks → package findings
7. Assembles per-transition decisions and report

- [ ] **Step 1: Create the service file**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata.DescriptorLifecycle;

public sealed class DefaultDescriptorLifecycleGovernanceService
    : IDescriptorLifecycleGovernanceService
{
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
                Severity = DescriptorLifecycleFindingSeverity.Info,
                Code = "LIFECYCLE_NO_TRANSITIONS",
                Message = "No transitions requested.",
                Source = "policy"
            });
        }

        var maxDecision = decisions.Count == 0
            ? DescriptorLifecycleDecisionKind.Allowed
            : decisions.Max(d => d.Decision);

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
                Severity = DescriptorLifecycleFindingSeverity.Review,
                Code = "LIFECYCLE_CHANGESET_MISMATCH",
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
                    Severity = DescriptorLifecycleFindingSeverity.Review,
                    Code = "LIFECYCLE_SUBJECT_NOT_IN_CHANGESET",
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
        var transitionSubjects = request.Transitions
            .Select(t => t.Subject)
            .ToHashSet();

        // Check for unresolvable DescriptorIds
        foreach (var br in request.BindingReport.Descriptors)
        {
            if (string.IsNullOrEmpty(br.DescriptorId))
            {
                packageFindings.Add(new DescriptorLifecycleFinding
                {
                    Severity = DescriptorLifecycleFindingSeverity.Review,
                    Code = "LIFECYCLE_BINDING_ID_UNRESOLVABLE",
                    Message = $"Binding report has empty or null DescriptorId.",
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
                    Severity = DescriptorLifecycleFindingSeverity.Review,
                    Code = "LIFECYCLE_BINDING_ID_UNRESOLVABLE",
                    Message = $"Binding report DescriptorId '{br.DescriptorId}' " +
                              "cannot be parsed as Namespace.Id.",
                    Source = "binding"
                });
            }
        }

        // Check for kind mismatch against transition subjects
        foreach (var br in request.BindingReport.Descriptors)
        {
            foreach (var transition in request.Transitions)
            {
                if (br.DescriptorId == transition.Subject.FullId
                    && br.DescriptorKind != transition.Subject.Kind
                    && transition.Subject.Version != null)
                {
                    packageFindings.Add(new DescriptorLifecycleFinding
                    {
                        Severity = DescriptorLifecycleFindingSeverity.Review,
                        Code = "LIFECYCLE_BINDING_KIND_MISMATCH",
                        Message = $"Binding report kind {br.DescriptorKind} does not match " +
                                  $"transition subject kind for {br.DescriptorId}.",
                        Source = "binding",
                        Subject = transition.Subject
                    });
                }
            }
        }

        // Check for version ambiguity
        var groupsByVersion = request.BindingReport.Descriptors
            .GroupBy(d => (d.DescriptorKind, d.DescriptorId))
            .Where(g => g.Select(d => d.Status).Distinct().Count() > 1);

        foreach (var group in groupsByVersion)
        {
            packageFindings.Add(new DescriptorLifecycleFinding
            {
                Severity = DescriptorLifecycleFindingSeverity.Review,
                Code = "LIFECYCLE_BINDING_VERSION_AMBIGUITY",
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
            if (issue.Severity == ValidationSeverity.Error)
            {
                findings.Add(new DescriptorLifecycleFinding
                {
                    Severity = DescriptorLifecycleFindingSeverity.Blocker,
                    Code = "LIFECYCLE_VALIDATION_ERROR",
                    Message = issue.Message,
                    Source = "validation"
                });
            }
            else if (issue.Severity == ValidationSeverity.Warning)
            {
                if (options.TreatValidationWarningsAsReviewRequired)
                {
                    findings.Add(new DescriptorLifecycleFinding
                    {
                        Severity = DescriptorLifecycleFindingSeverity.Review,
                        Code = "LIFECYCLE_VALIDATION_WARNING",
                        Message = issue.Message,
                        Source = "validation"
                    });
                }
                else
                {
                    findings.Add(new DescriptorLifecycleFinding
                    {
                        Severity = DescriptorLifecycleFindingSeverity.Warning,
                        Code = "LIFECYCLE_VALIDATION_WARNING",
                        Message = issue.Message,
                        Source = "validation"
                    });
                }
            }
            else
            {
                findings.Add(new DescriptorLifecycleFinding
                {
                    Severity = DescriptorLifecycleFindingSeverity.Info,
                    Code = "LIFECYCLE_VALIDATION_INFO",
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
                        Severity = DescriptorLifecycleFindingSeverity.Info,
                        Code = "LIFECYCLE_BINDING_NOT_READY",
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
                        Severity = DescriptorLifecycleFindingSeverity.Info,
                        Code = "LIFECYCLE_BINDING_NOT_READY",
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
                    Severity = DescriptorLifecycleFindingSeverity.Blocker,
                    Code = "LIFECYCLE_BINDING_INVALID",
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
                        Severity = DescriptorLifecycleFindingSeverity.Blocker,
                        Code = "LIFECYCLE_BINDING_UNBOUND",
                        Message = "Binding status is Unbound.",
                        Source = "binding",
                        Subject = transition.Subject
                    });
                }
                else
                {
                    findings.Add(new DescriptorLifecycleFinding
                    {
                        Severity = DescriptorLifecycleFindingSeverity.Review,
                        Code = "LIFECYCLE_BINDING_UNBOUND",
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
                        Severity = DescriptorLifecycleFindingSeverity.Blocker,
                        Code = "LIFECYCLE_BINDING_UNSUPPORTED",
                        Message = "Binding status is Unsupported.",
                        Source = "binding",
                        Subject = transition.Subject
                    });
                }
                else if (options.TreatSubmitForReviewUnsupportedBindingAsReviewRequired)
                {
                    findings.Add(new DescriptorLifecycleFinding
                    {
                        Severity = DescriptorLifecycleFindingSeverity.Review,
                        Code = "LIFECYCLE_BINDING_UNSUPPORTED",
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
                        Severity = DescriptorLifecycleFindingSeverity.Review,
                        Code = "LIFECYCLE_BINDING_PARTIAL",
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
                    Severity = DescriptorLifecycleFindingSeverity.Blocker,
                    Code = "LIFECYCLE_BINDING_INVALID",
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
                    Severity = DescriptorLifecycleFindingSeverity.Review,
                    Code = $"LIFECYCLE_BINDING_{status.ToString().ToUpperInvariant()}",
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
                    Severity = DescriptorLifecycleFindingSeverity.Blocker,
                    Code = "LIFECYCLE_BINDING_INVALID",
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
                        Severity = DescriptorLifecycleFindingSeverity.Blocker,
                        Code = "LIFECYCLE_BINDING_UNBOUND",
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
                        Severity = DescriptorLifecycleFindingSeverity.Blocker,
                        Code = "LIFECYCLE_BINDING_UNSUPPORTED",
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
                        Severity = DescriptorLifecycleFindingSeverity.Review,
                        Code = "LIFECYCLE_BINDING_PARTIAL",
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
            if (diag.Severity == DiagnosticSeverity.Error)
            {
                findings.Add(new DescriptorLifecycleFinding
                {
                    Severity = options.BlockOnTopologyErrors
                        ? DescriptorLifecycleFindingSeverity.Blocker
                        : DescriptorLifecycleFindingSeverity.Review,
                    Code = "LIFECYCLE_TOPOLOGY_ERROR",
                    Message = diag.Message,
                    Source = "topology",
                    Subject = diag.Subject
                });
            }
            else if (diag.Severity == DiagnosticSeverity.Warning)
            {
                findings.Add(new DescriptorLifecycleFinding
                {
                    Severity = DescriptorLifecycleFindingSeverity.Review,
                    Code = "LIFECYCLE_TOPOLOGY_WARNING",
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
            if (diag.Severity == DiagnosticSeverity.Error)
            {
                findings.Add(new DescriptorLifecycleFinding
                {
                    Severity = options.BlockOnImpactDiagnosticsErrors
                        ? DescriptorLifecycleFindingSeverity.Blocker
                        : DescriptorLifecycleFindingSeverity.Review,
                    Code = "LIFECYCLE_IMPACT_DIAGNOSTIC_ERROR",
                    Message = diag.Message,
                    Source = "impact",
                    Subject = diag.Subject
                });
            }
            else if (diag.Severity == DiagnosticSeverity.Warning)
            {
                findings.Add(new DescriptorLifecycleFinding
                {
                    Severity = DescriptorLifecycleFindingSeverity.Review,
                    Code = "LIFECYCLE_IMPACT_DIAGNOSTIC_WARNING",
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
                Severity = DescriptorLifecycleFindingSeverity.Review,
                Code = "LIFECYCLE_IMPACT_SEVERITY_THRESHOLD",
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

        // Find compatibility findings for this transition's subject
        var findingsForSubject = compatibilityReport.Findings
            .Where(f => f.Subject == transition.Subject)
            .ToList();

        // If no subject-specific findings, check report-level MaxLevel
        if (findingsForSubject.Count == 0)
        {
            // Apply report-level compatibility governance
            AddCompatibilityFinding(
                compatibilityReport.MaxLevel, transition, options, findings);
            return findings;
        }

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
                        Severity = DescriptorLifecycleFindingSeverity.Blocker,
                        Code = "LIFECYCLE_COMPAT_BREAKING",
                        Message = "Breaking compatibility change detected.",
                        Source = "compatibility",
                        Subject = transition.Subject
                    });
                }
                else if (options.TreatBreakingCompatibilityAsReviewRequired)
                {
                    findings.Add(new DescriptorLifecycleFinding
                    {
                        Severity = DescriptorLifecycleFindingSeverity.Review,
                        Code = "LIFECYCLE_COMPAT_BREAKING",
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
                        Severity = DescriptorLifecycleFindingSeverity.Review,
                        Code = "LIFECYCLE_COMPAT_SECURITY_SENSITIVE",
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
                        Severity = DescriptorLifecycleFindingSeverity.Review,
                        Code = "LIFECYCLE_COMPAT_RISKY",
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
                        Severity = DescriptorLifecycleFindingSeverity.Review,
                        Code = "LIFECYCLE_COMPAT_UNSUPPORTED",
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
            if (diag.Severity == DiagnosticSeverity.Error)
            {
                findings.Add(new DescriptorLifecycleFinding
                {
                    Severity = options.BlockOnCompatibilityDiagnosticsErrors
                        ? DescriptorLifecycleFindingSeverity.Blocker
                        : DescriptorLifecycleFindingSeverity.Review,
                    Code = "LIFECYCLE_COMPAT_DIAGNOSTIC_ERROR",
                    Message = diag.Message,
                    Source = "compatibility",
                    Subject = diag.Subject
                });
            }
            else if (diag.Severity == DiagnosticSeverity.Warning)
            {
                findings.Add(new DescriptorLifecycleFinding
                {
                    Severity = DescriptorLifecycleFindingSeverity.Review,
                    Code = "LIFECYCLE_COMPAT_DIAGNOSTIC_WARNING",
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
        if (findings.Any(f => f.Severity == DescriptorLifecycleFindingSeverity.Blocker))
            return DescriptorLifecycleDecisionKind.Blocked;

        if (findings.Any(f => f.Severity == DescriptorLifecycleFindingSeverity.Review))
            return DescriptorLifecycleDecisionKind.ReviewRequired;

        return DescriptorLifecycleDecisionKind.Allowed;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Metadata`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Metadata/DescriptorLifecycle/
git commit -m "feat(lifecycle): implement DefaultDescriptorLifecycleGovernanceService"
```

---

## Task 5: DI Registration

**Files:**
- Modify: `framework/src/CrestCreates.Metadata/MetadataServiceCollectionExtensions.cs`

- [ ] **Step 1: Add the extension method**

Add to `MetadataServiceCollectionExtensions.cs`:

```csharp
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.DescriptorLifecycle;

// Add inside the class:
public static IServiceCollection AddDescriptorLifecycleGovernance(
    this IServiceCollection services)
{
    services.TryAddSingleton<IDescriptorLifecycleGovernanceService,
        DefaultDescriptorLifecycleGovernanceService>();
    return services;
}
```

The full file becomes:

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.DescriptorCompatibility;
using CrestCreates.Metadata.DescriptorLifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Metadata;

public static class MetadataServiceCollectionExtensions
{
    public static IServiceCollection AddBindingStatusKernel(this IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorRuntimeBindingStatusProvider,
            DefaultDescriptorRuntimeBindingStatusProvider>();
        return services;
    }

    public static IServiceCollection AddRelationshipKernel(this IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorRelationshipProvider,
            DefaultDescriptorRelationshipProvider>();

        services.AddSingleton<IDescriptorRelationshipExtractor, SchemaRelationshipExtractor>();

        return services;
    }

    public static IServiceCollection AddTopologyKernel(this IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorTopologyBuilder, DescriptorTopologyBuilder>();
        return services;
    }

    public static IServiceCollection AddDescriptorImpactAnalysis(
        this IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorImpactAnalyzer, DescriptorImpactAnalyzer>();
        services.TryAddSingleton<IDescriptorChangeSetBuilder, DescriptorChangeSetBuilder>();
        return services;
    }

    public static IServiceCollection AddDescriptorCompatibilityAnalysis(
        this IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorCompatibilityAnalyzer, DescriptorCompatibilityAnalyzer>();
        return services;
    }

    public static IServiceCollection AddDescriptorLifecycleGovernance(
        this IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorLifecycleGovernanceService,
            DefaultDescriptorLifecycleGovernanceService>();
        return services;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Metadata`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Metadata/MetadataServiceCollectionExtensions.cs
git commit -m "feat(lifecycle): add AddDescriptorLifecycleGovernance DI extension"
```

---

## Task 6: Test File — Helper Methods and First 3 Tests (Validation Policy)

**Files:**
- Create: `framework/test/CrestCreates.Metadata.Tests/DescriptorLifecycle/DescriptorLifecycleGovernanceServiceTests.cs`

- [ ] **Step 1: Create test file with helper factory methods and first 3 tests**

```csharp
using Xunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.DescriptorLifecycle;

namespace CrestCreates.Metadata.Tests.DescriptorLifecycle;

public class DescriptorLifecycleGovernanceServiceTests
{
    private static readonly IDescriptorLifecycleGovernanceService Service =
        new DefaultDescriptorLifecycleGovernanceService();

    private static DescriptorRef TestRef => new("test", "T1", 1);

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
        => new()
        {
            ChangeSet = new DescriptorChangeSet { Changes = Array.Empty<DescriptorChange>() },
            AffectedDescriptors = Array.Empty<AffectedDescriptor>(),
            Paths = Array.Empty<DescriptorImpactPath>(),
            MaxSeverity = DescriptorImpactSeverity.None,
            Diagnostics = Array.Empty<DescriptorImpactDiagnostic>()
        };

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
        => new()
        {
            ChangeSet = new DescriptorChangeSet { Changes = Array.Empty<DescriptorChange>() },
            ImpactReport = MakeCleanImpactReport(),
            Findings = Array.Empty<DescriptorCompatibilityFinding>(),
            MaxLevel = DescriptorCompatibilityLevel.Compatible,
            Diagnostics = Array.Empty<DescriptorCompatibilityDiagnostic>()
        };

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
                new ValidationIssue(ValidationSeverity.Error, "Something is wrong")));

        var report = Service.Evaluate(request);

        report.IsBlocked.Should().BeTrue();
        report.Decisions.Should().ContainSingle()
            .Which.Findings.Should().Contain(f =>
                f.Code == "LIFECYCLE_VALIDATION_ERROR" &&
                f.Severity == DescriptorLifecycleFindingSeverity.Blocker);
    }

    [Fact]
    public void ValidationWarning_AllowsByDefault()
    {
        var request = MakeRequest(
            validationReport: ValidationReport.FromIssues(
                new ValidationIssue(ValidationSeverity.Warning, "Minor issue")));

        var report = Service.Evaluate(request);

        report.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void ValidationWarning_ReviewRequired_WhenOptionEnabled()
    {
        var request = MakeRequest(
            validationReport: ValidationReport.FromIssues(
                new ValidationIssue(ValidationSeverity.Warning, "Minor issue")),
            options: new DescriptorLifecycleGovernanceOptions
            {
                TreatValidationWarningsAsReviewRequired = true
            });

        var report = Service.Evaluate(request);

        report.RequiresReview.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Build and run these 3 tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~DescriptorLifecycleGovernanceServiceTests" --no-restore`
Expected: 3 passed

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Metadata.Tests/DescriptorLifecycle/
git commit -m "test(lifecycle): add validation policy tests (3/43)"
```

---

## Task 7: Binding Policy Tests (13 tests)

**Files:**
- Modify: `framework/test/CrestCreates.Metadata.Tests/DescriptorLifecycle/DescriptorLifecycleGovernanceServiceTests.cs`

Add the following 13 test methods to the `DescriptorLifecycleGovernanceServiceTests` class, after the validation tests:

- [ ] **Step 1: Add binding policy test methods**

```csharp
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
```

- [ ] **Step 2: Build and run these 13 binding tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~DescriptorLifecycleGovernanceServiceTests" --no-restore`
Expected: 16 passed (3 validation + 13 binding)

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Metadata.Tests/DescriptorLifecycle/
git commit -m "test(lifecycle): add binding policy tests (16/43)"
```

---

## Task 8: Topology, Impact, and Compatibility Policy Tests (12 tests)

**Files:**
- Modify: `framework/test/CrestCreates.Metadata.Tests/DescriptorLifecycle/DescriptorLifecycleGovernanceServiceTests.cs`

Add these 12 test methods to the class:

- [ ] **Step 1: Add topology, impact, and compatibility test methods**

```csharp
    // --- 10.6 Topology Policy ---

    [Fact]
    public void TopologyError_BlocksByDefault()
    {
        var topologyDiag = new DescriptorTopologyDiagnostics
        {
            All = new[]
            {
                new DescriptorTopologyDiagnostic(
                    DiagnosticSeverity.Error,
                    "MISSING_TARGET",
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
                    DiagnosticSeverity.Warning,
                    "SOME_WARNING",
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
                    DiagnosticSeverity.Error,
                    "IMPACT_TEST",
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
                    DiagnosticSeverity.Warning,
                    "IMPACT_TEST",
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
                f.Severity == DescriptorLifecycleFindingSeverity.Blocker);
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
                f.Severity == DescriptorLifecycleFindingSeverity.Blocker);
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
                    DiagnosticSeverity.Error,
                    "COMPAT_TEST",
                    "Compat error",
                    TestRef,
                    null)
            });

        var request = MakeRequest(compatibilityReport: compatReport);

        var report = Service.Evaluate(request);

        report.IsBlocked.Should().BeTrue();
    }
```

- [ ] **Step 2: Build and run all 28 tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~DescriptorLifecycleGovernanceServiceTests" --no-restore`
Expected: 28 passed

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Metadata.Tests/DescriptorLifecycle/
git commit -m "test(lifecycle): add topology, impact, and compatibility policy tests (28/43)"
```

---

## Task 9: Consistency, Edge Cases, and DI Tests (15 tests)

**Files:**
- Modify: `framework/test/CrestCreates.Metadata.Tests/DescriptorLifecycle/DescriptorLifecycleGovernanceServiceTests.cs`

Add these 15 test methods to the class:

- [ ] **Step 1: Add consistency, edge case, and DI test methods**

```csharp
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
                    DiagnosticSeverity.Warning,
                    "WARN",
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
                new ValidationIssue(ValidationSeverity.Error, "Error")));
        var blockedReport = Service.Evaluate(blockedRequest);
        blockedReport.IsAllowed.Should().BeFalse();
        blockedReport.RequiresReview.Should().BeFalse();
        blockedReport.IsBlocked.Should().BeTrue();
    }

    [Fact]
    public void DoesNotMutateDescriptorsOrReports()
    {
        var originalValidation = ValidationReport.FromIssues(
            new ValidationIssue(ValidationSeverity.Warning, "Original"));
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
        // TestRef has Namespace="test", Id="T1", so FullId="test.T1"
        // We create a transition with a ref that has a version,
        // and a binding report with mismatched kind
        var refWithVersion = new DescriptorRef("test", "T1", 1);
        var bindingReport = new RuntimeBindingReport
        {
            Descriptors = new[]
            {
                new DescriptorBindingReport
                {
                    DescriptorId = "test.T1",
                    DescriptorKind = DescriptorKind.Workflow,  // Mismatch
                    Status = DescriptorBindingStatus.RuntimeReady
                }
            }
        };

        var request = MakeRequest(
            transitions: new[] { MakeTransition(DescriptorLifecycleOperation.Activate, refWithVersion) },
            bindingReport: bindingReport);

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
                    DescriptorId = "test.T1",
                    DescriptorKind = DescriptorKind.Schema,
                    Status = DescriptorBindingStatus.RuntimeReady
                },
                new DescriptorBindingReport
                {
                    DescriptorId = "test.T1",
                    DescriptorKind = DescriptorKind.Schema,
                    Status = DescriptorBindingStatus.Unbound  // Same id, different status
                }
            }
        };

        var request = MakeRequest(bindingReport: bindingReport);

        var report = Service.Evaluate(request);

        report.PackageFindings.Should().Contain(f =>
            f.Code == "LIFECYCLE_BINDING_VERSION_AMBIGUITY");
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
```

- [ ] **Step 2: Build and run all 43 tests**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~DescriptorLifecycleGovernanceServiceTests" --no-restore`
Expected: 43 passed

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Metadata.Tests/DescriptorLifecycle/
git commit -m "test(lifecycle): add consistency, edge case, and DI tests (43/43)"
```

---

## Task 7: Full Build and Regression Verification

- [ ] **Step 1: Full solution build**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 2: Full test run**

Run: `dotnet test`
Expected: All tests pass (existing + 43 new lifecycle tests)

- [ ] **Step 3: Final commit if any fixes needed**

Only if test failures required code changes.

---

## Self-Review

### Spec Coverage

| Spec Section | Task |
|---|---|
| §3 Governance Operation vs State | Task 1 (DescriptorLifecycleOperation enum) |
| §4.1 DescriptorLifecycleTransition | Task 2 |
| §4.2 DescriptorLifecycleDecisionKind | Task 1 |
| §4.3 DescriptorLifecycleFindingSeverity | Task 1 |
| §4.4 DescriptorLifecycleFinding | Task 2 |
| §4.5 DescriptorLifecycleDecision | Task 2 |
| §4.6 DescriptorLifecycleGovernanceReport | Task 3 |
| §4.7 DescriptorLifecycleGovernanceOptions | Task 3 |
| §4.8 DescriptorLifecycleGovernanceRequest | Task 3 |
| §4.9 IDescriptorLifecycleGovernanceService | Task 3 |
| §5 Default Policy Mapping | Task 4 (implementation) + Tasks 6-8 (tests) |
| §6 Operation Semantics | Task 4 (implementation) |
| §7 Consistency Checks | Task 4 (implementation) + Task 9 (tests) |
| §8 DI Registration | Task 5 |
| §9 Project Structure | Tasks 1-5 |
| §10 Test Plan (43 tests) | Tasks 6-9 |
| §11 Completion Criteria | Task 7 (build + regression) |

### Placeholder Scan

No TBD, TODO, "implement later", "add validation", "similar to Task N", or steps without code.

### Type Consistency

- `DescriptorRef` used consistently across all types (from `CrestCreates.Metadata.Abstractions`)
- `DescriptorLifecycleOperation` enum values match between type definition (Task 1) and switch statements (Task 4)
- `DescriptorBindingStatus` values match existing enum: `Invalid`, `Unbound`, `Unsupported`, `PartiallyBound`, `RuntimeReady`
- `DiagnosticSeverity` from `CrestCreates.Metadata.Abstractions.DescriptorTopology` used consistently
- `DescriptorCompatibilityLevel` values match existing enum: `Compatible`, `Risky`, `SecuritySensitive`, `Breaking`, `Unsupported`
