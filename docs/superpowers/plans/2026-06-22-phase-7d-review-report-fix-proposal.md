# Phase 7d — Review Report & Fix Proposal Contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add structured review report DTOs, builder, renderer, and message template catalog; upgrade FixProposal/FixProposalAction contract; add 2 new tools to the Agent Control Plane.

**Architecture:** Review Report is a structured DTO (13 fixed sections) produced by a deterministic Builder from pre-filtered review results. Renderer produces Markdown/PlainText projections from the DTO. FixProposal contract is upgraded in-place (breaking change) with new enums, fields, and JsonElement values. Apply runtime only supports single-action executable proposals.

**Tech Stack:** .NET 10, System.Text.Json source-generated context, xUnit + FluentAssertions

## Global Constraints

- Contract version: bump `AgentControlPlaneContractVersion.Current` from `"7c.v1"` to `"7d.v1"`
- All new DTOs are `sealed record` types in `CrestCreates.Agent.ControlPlane.Abstractions/ToolDtos/`
- All new enums in `CrestCreates.Agent.ControlPlane.Abstractions/`
- Builder/Renderer/Catalog implementations in `CrestCreates.Agent.ControlPlane/`
- Renderer must not access external services, registry, or LLM
- Builder must fail-fast on `VisibilityApplied=false`
- FixProposal contract upgrade is breaking — no dual-track
- Phase 7d only supports single-action executable proposals (multi-action rejected)
- `BlocksActivationUntilResolved` is explanation only, not gate
- `RequiresManualAction == (Applicability == ManualActionRequired)` — Builder enforces
- ReportId = stable hash of (TenantId + DraftId + DraftVersion + ReviewResultId + ContractVersion + TemplateVersion)
- SectionId uses lower_case stable ids (e.g. `summary`, `draft_identity`)

## File Structure

### New Files — Abstractions (DTOs + Enums)

- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/ToolDtos/DescriptorReviewReportDto.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/ToolDtos/DescriptorReviewReportSectionDto.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/ToolDtos/DescriptorReviewReportItemDto.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/ToolDtos/DescriptorReviewRecommendationDto.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/ToolDtos/DescriptorReviewReportBuildRequest.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/DescriptorReviewReportSectionKind.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/DescriptorReviewSeverity.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/DescriptorReviewRecommendationKind.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/DescriptorReviewReportFormat.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/FixProposalKind.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/FixProposalApplicability.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/FixProposalActionSafetyLevel.cs`

### New Files — ControlPlane (Builder + Renderer + Catalog)

- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/ReportBuilder/IDescriptorReviewReportBuilder.cs` (interface in Abstractions)
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/ReportBuilder/DefaultDescriptorReviewReportBuilder.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/ReportBuilder/IDescriptorReviewMessageTemplateCatalog.cs` (interface in Abstractions)
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/ReportBuilder/DefaultDescriptorReviewMessageTemplateCatalog.cs`
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/ReportRenderer/IDescriptorReviewReportRenderer.cs` (interface in Abstractions)
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/ReportRenderer/DefaultDescriptorReviewReportRenderer.cs`

### Modified Files — Abstractions

- `FixProposal.cs` — add fields, rename ProposalId→Id
- `FixProposalAction.cs` — add fields, rename Path→TargetPath, string→JsonElement?
- `FixProposalActionKind.cs` — expand from 3 to 10 values
- `FixProposalRiskLevel.cs` — no change (Safe, Low, Medium, High, Unsafe)
- `AgentToolName.cs` — add 2 new constants
- `AgentToolCategory.cs` — add ReviewReport category
- `AgentToolPermissionName.cs` — add 2 new permission names
- `IAgentControlPlaneToolService.cs` — add 2 new method signatures
- `Json/AgentControlPlaneToolJsonSerializerContext.cs` — add new type registrations
- `Json/AgentControlPlaneContractVersion.cs` — bump to "7d.v1"

### Modified Files — ControlPlane

- `DefaultAgentControlPlaneToolService.cs` — add 2 new tool methods, update Apply/Suggest methods
- `StaticAgentToolManifestProvider.cs` — add 2 new tool entries
- `AgentControlPlaneServiceCollectionExtensions.cs` — register Builder/Renderer/Catalog
- `AgentToolVisibilityCoverage.cs` — add 2 new tool entries
- `AgentControlPlaneArtifactEntries.cs` — add ReportResourceSnapshot

### Modified Files — Tests

- Existing test files constructing FixProposal/FixProposalAction — update to new shape
- New test files for Report Builder, Renderer, Template Catalog, Fix Proposal contract, Coverage, Boundary

---

### Task 1: Report DTO Types + Enums

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/DescriptorReviewReportSectionKind.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/DescriptorReviewSeverity.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/DescriptorReviewRecommendationKind.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/DescriptorReviewReportFormat.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/ToolDtos/DescriptorReviewReportItemDto.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/ToolDtos/DescriptorReviewReportSectionDto.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/ToolDtos/DescriptorReviewRecommendationDto.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/ToolDtos/DescriptorReviewReportDto.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/ToolDtos/DescriptorReviewReportBuildRequest.cs`

**Interfaces:**
- Consumes: `DescriptorDraftReviewResult` (from DescriptorDraft.Abstractions), `DescriptorDraft` (alias `Draft`)
- Produces: All report DTO types for Tasks 2-7

- [ ] **Step 1: Create enum types**

`DescriptorReviewReportSectionKind.cs`:
```csharp
namespace CrestCreates.Agent.ControlPlane.Abstractions;

public enum DescriptorReviewReportSectionKind
{
    Summary = 1,               // SectionId: "summary"
    DraftIdentity = 2,         // SectionId: "draft_identity"
    ProposedChanges = 3,       // SectionId: "proposed_changes"
    ImpactAnalysis = 4,        // SectionId: "impact_analysis"
    DependencySummary = 5,     // SectionId: "dependency_summary"
    Compatibility = 6,         // SectionId: "compatibility"
    Governance = 7,            // SectionId: "governance"
    RequiredHumanReview = 8,   // SectionId: "required_human_review"
    ActivationEligibility = 9, // SectionId: "activation_eligibility"
    Diagnostics = 10,          // SectionId: "diagnostics"
    Recommendations = 11,      // SectionId: "recommendations"
    PackagePreview = 12,       // SectionId: "package_preview"
    StableHashes = 13          // SectionId: "stable_hashes"
}
```

`DescriptorReviewSeverity.cs`:
```csharp
namespace CrestCreates.Agent.ControlPlane.Abstractions;

public enum DescriptorReviewSeverity { Info, Warning, Error, Blocker }
```

`DescriptorReviewRecommendationKind.cs`:
```csharp
namespace CrestCreates.Agent.ControlPlane.Abstractions;

public enum DescriptorReviewRecommendationKind
{
    RequestActivationHandoff,
    RequestHumanReview,
    ApplyFixProposal,
    ReviseDraft,
    CancelDraft,
    NoAction
}
```

`DescriptorReviewReportFormat.cs`:
```csharp
namespace CrestCreates.Agent.ControlPlane.Abstractions;

public enum DescriptorReviewReportFormat { Markdown, PlainText }
```

- [ ] **Step 2: Create DTO types**

`DescriptorReviewReportItemDto.cs`:
```csharp
namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record DescriptorReviewReportItemDto
{
    public required string ItemId { get; init; }
    public required string ReasonCode { get; init; }
    public required string MessageTemplateId { get; init; }
    public required string Message { get; init; }
    public required DescriptorReviewSeverity Severity { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new(StringComparer.Ordinal);
    public IReadOnlyList<string> RelatedDiagnosticIds { get; init; } = [];
    public IReadOnlyList<string> RelatedDescriptorIds { get; init; } = [];
}
```

`DescriptorReviewReportSectionDto.cs`:
```csharp
namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record DescriptorReviewReportSectionDto
{
    public required DescriptorReviewReportSectionKind Kind { get; init; }
    public required string SectionId { get; init; }
    public required string Title { get; init; }
    public required int Order { get; init; }
    public required bool IsEmpty { get; init; }
    public required DescriptorReviewSeverity OverallSeverity { get; init; }
    public required IReadOnlyList<DescriptorReviewReportItemDto> Items { get; init; }
}
```

`DescriptorReviewRecommendationDto.cs`:
```csharp
namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record DescriptorReviewRecommendationDto
{
    public required string RecommendationId { get; init; }
    public required string ReasonCode { get; init; }
    public required string Message { get; init; }
    public required DescriptorReviewRecommendationKind Kind { get; init; }
    public required bool IsActionable { get; init; }
    public IReadOnlyList<string> RelatedItemIds { get; init; } = [];
}
```

`DescriptorReviewReportDto.cs`:
```csharp
namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record DescriptorReviewReportDto
{
    public required string ReportId { get; init; }
    public required string DraftId { get; init; }
    public required string TenantId { get; init; }
    public required string ReviewResultId { get; init; }
    public required string DraftVersion { get; init; }
    public required string SourceReviewHash { get; init; }
    public required string TemplateVersion { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
    public required string ContractVersion { get; init; } = AgentControlPlaneContractVersion.Current;

    public required IReadOnlyList<DescriptorReviewRecommendationDto> Recommendations { get; init; }

    public required DescriptorReviewReportSectionDto SummarySection { get; init; }
    public required DescriptorReviewReportSectionDto DraftIdentitySection { get; init; }
    public required DescriptorReviewReportSectionDto ProposedChangesSection { get; init; }
    public required DescriptorReviewReportSectionDto ImpactAnalysisSection { get; init; }
    public required DescriptorReviewReportSectionDto DependencySummarySection { get; init; }
    public required DescriptorReviewReportSectionDto CompatibilitySection { get; init; }
    public required DescriptorReviewReportSectionDto GovernanceSection { get; init; }
    public required DescriptorReviewReportSectionDto RequiredHumanReviewSection { get; init; }
    public required DescriptorReviewReportSectionDto ActivationEligibilitySection { get; init; }
    public required DescriptorReviewReportSectionDto DiagnosticsSection { get; init; }
    public required DescriptorReviewReportSectionDto RecommendationsSection { get; init; }
    public required DescriptorReviewReportSectionDto PackagePreviewSection { get; init; }
    public required DescriptorReviewReportSectionDto StableHashesSection { get; init; }
}
```

`DescriptorReviewReportBuildRequest.cs`:
```csharp
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record DescriptorReviewReportBuildRequest
{
    public required DraftAbstractions.DescriptorDraftReviewResult ReviewResult { get; init; }
    public required DraftAbstractions.DescriptorDraft Draft { get; init; }
    public required bool VisibilityApplied { get; init; }
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat(phase-7d): add Review Report DTO types and enums"
```

---

### Task 2: Fix Proposal Contract Enums

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/FixProposalKind.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/FixProposalApplicability.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/FixProposalActionSafetyLevel.cs`

**Interfaces:**
- Produces: Enums for Task 3 (FixProposal/FixProposalAction upgrade)

- [ ] **Step 1: Create enum types**

`FixProposalKind.cs`:
```csharp
namespace CrestCreates.Agent.ControlPlane.Abstractions;

public enum FixProposalKind
{
    CreateMissingDescriptor = 1,
    ReplaceMissingReference = 2,
    RemoveInvalidRelationship = 3,
    AddRequiredBindingMetadata = 4,
    SplitBreakingChangeIntoCompatibleChange = 5,
    MarkRequiresReview = 6,
    FlagUnsafeExpansion = 7,
    SuggestVersionBump = 8
}
```

`FixProposalApplicability.cs`:
```csharp
namespace CrestCreates.Agent.ControlPlane.Abstractions;

public enum FixProposalApplicability
{
    CurrentMutableDraft = 1,
    RequiresNewDraftRevision = 2,
    ManualActionRequired = 3,
    NotApplicable = 4
}
```

`FixProposalActionSafetyLevel.cs`:
```csharp
namespace CrestCreates.Agent.ControlPlane.Abstractions;

public enum FixProposalActionSafetyLevel
{
    Safe = 1,
    LowRisk = 2,
    RequiresReview = 3,
    Unsafe = 4
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "feat(phase-7d): add FixProposalKind, FixProposalApplicability, FixProposalActionSafetyLevel enums"
```

---

### Task 3: FixProposal & FixProposalAction Upgrade

**Files:**
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/FixProposal.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/FixProposalAction.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/FixProposalActionKind.cs`

**Interfaces:**
- Consumes: Enums from Task 2
- Produces: Upgraded FixProposal/FixProposalAction for Tasks 6-8

- [ ] **Step 1: Upgrade FixProposalActionKind**

Replace entire content of `FixProposalActionKind.cs`:
```csharp
namespace CrestCreates.Agent.ControlPlane.Abstractions;

public enum FixProposalActionKind
{
    SetValue = 1,
    RemoveValue = 2,
    AddValue = 3,
    MergeObject = 4,
    ReplaceReference = 5,
    RemoveRelationship = 6,
    AddRequiredBindingMetadata = 7,
    SuggestVersionBump = 8,
    MarkRequiresReview = 9,
    ManualActionRequired = 10
}
```

- [ ] **Step 2: Upgrade FixProposalAction**

Replace entire content of `FixProposalAction.cs`:
```csharp
using System.Text.Json;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record FixProposalAction
{
    public required FixProposalActionKind Kind { get; init; }
    public required string TargetPath { get; init; }
    public string? TargetDescriptorId { get; init; }
    public JsonElement? CurrentValue { get; init; }
    public JsonElement? ProposedValue { get; init; }
    public required bool IsExecutable { get; init; }
    public required FixProposalActionSafetyLevel SafetyLevel { get; init; }
    public string? Description { get; init; }
}
```

- [ ] **Step 3: Upgrade FixProposal**

Replace entire content of `FixProposal.cs`:
```csharp
namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record FixProposal
{
    public required string Id { get; init; }
    public required string DraftId { get; init; }
    public required string TenantId { get; init; }
    public required FixProposalKind Kind { get; init; }
    public required string Title { get; init; }
    public required string Explanation { get; init; }
    public required string ReasonCode { get; init; }

    public required FixProposalApplicability Applicability { get; init; }
    public required bool IsExecutable { get; init; }
    public required bool RequiresManualAction { get; init; }
    public required bool RequiresHumanReview { get; init; }
    public required bool BlocksActivationUntilResolved { get; init; }
    public required FixProposalRiskLevel RiskLevel { get; init; }

    public IReadOnlyList<string> RelatedDiagnosticIds { get; init; } = [];
    public IReadOnlyList<string> RelatedDescriptorIds { get; init; } = [];
    public required IReadOnlyList<FixProposalAction> Actions { get; init; }
    public required IReadOnlyList<AgentToolDiagnostic> Diagnostics { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? Rationale { get; init; }
    public required string ContractVersion { get; init; } = AgentControlPlaneContractVersion.Current;
}
```

- [ ] **Step 4: Build to check compilation errors** (expect errors in service + tests — Task 6 will fix)

Run: `dotnet build src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions`
Expected: PASS (Abstractions itself should compile)

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(phase-7d): upgrade FixProposal and FixProposalAction contracts (breaking)"
```

---

### Task 4: Message Template Catalog

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/IDescriptorReviewMessageTemplateCatalog.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/ReportBuilder/DefaultDescriptorReviewMessageTemplateCatalog.cs`

**Interfaces:**
- Produces: `IDescriptorReviewMessageTemplateCatalog` for Task 5 (Builder)

- [ ] **Step 1: Create interface**

`IDescriptorReviewMessageTemplateCatalog.cs`:
```csharp
namespace CrestCreates.Agent.ControlPlane.Abstractions;

public interface IDescriptorReviewMessageTemplateCatalog
{
    string Format(string messageTemplateId, IReadOnlyDictionary<string, string> parameters);
    string TemplateVersion { get; }
}
```

- [ ] **Step 2: Create default implementation**

`DefaultDescriptorReviewMessageTemplateCatalog.cs`:
```csharp
using System.Text.RegularExpressions;
using CrestCreates.Agent.ControlPlane.Abstractions;

namespace CrestCreates.Agent.ControlPlane;

public sealed class DefaultDescriptorReviewMessageTemplateCatalog
    : IDescriptorReviewMessageTemplateCatalog
{
    public string TemplateVersion => "7d.v1";

    private static readonly Dictionary<string, string> Templates = new(StringComparer.Ordinal)
    {
        ["report.activation.eligible"] = "Draft is eligible for activation handoff.",
        ["report.activation.blocked"] = "Draft is not eligible: {BlockingReasons}.",
        ["report.governance.approved"] = "Governance decision: approved. {Rationale}",
        ["report.governance.rejected"] = "Governance decision: rejected. {Rationale}",
        ["report.governance.review_required"] = "Governance decision: review required. {Rationale}",
        ["report.diagnostics.missing_ref"] = "Descriptor '{DescriptorId}' references missing '{ReferenceId}'.",
        ["report.compatibility.schema"] = "Schema change is incompatible: {Details}.",
        ["report.summary.valid"] = "Draft validation passed with {DiagnosticCount} diagnostics.",
        ["report.summary.invalid"] = "Draft validation failed with {ErrorCount} errors and {BlockerCount} blockers.",
        ["report.human_review.required"] = "Human review required: {Reason}.",
        ["report.recommendation.no_action"] = "No action required at this time.",
        ["report.recommendation.activation_handoff"] = "Draft is ready for activation handoff.",
        ["report.recommendation.human_review"] = "Human review is required before proceeding.",
        ["report.recommendation.apply_fix"] = "Fix proposal available: {FixProposalId}.",
        ["report.recommendation.revise_draft"] = "Draft needs revision before proceeding.",
        ["report.recommendation.cancel_draft"] = "Draft should be cancelled.",
        ["report.package.available"] = "Package preview available with {DescriptorCount} descriptors.",
        ["report.hashes.computed"] = "Stable hashes computed for {HashCount} items.",
        ["report.draft_identity.info"] = "Draft '{DraftId}' of kind '{DescriptorKind}', operation {Operation}, status {Status}.",
        ["report.proposed_changes.materialized"] = "Materialization produced {ProposedCount} proposed descriptors.",
        ["report.proposed_changes.failed"] = "Materialization failed: {Reason}.",
        ["report.impact.affected"] = "Impact analysis found {AffectedCount} affected descriptors.",
        ["report.impact.none"] = "No descriptors affected by this draft.",
        ["report.dependency.summary"] = "Topology: {NodeCount} nodes, {EdgeCount} edges.",
        ["report.compatibility.compatible"] = "All {DescriptorCount} descriptors are compatible.",
        ["report.compatibility.incompatible"] = "{IncompatibleCount} of {TotalCount} descriptors are incompatible.",
        ["report.diagnostics.count"] = "{TotalCount} diagnostics: {InfoCount} info, {WarningCount} warnings, {ErrorCount} errors, {BlockerCount} blockers.",
        ["report.stable_hashes.present"] = "Stable hashes available for {HashCount} items.",
        ["report.stable_hashes.none"] = "No stable hashes computed.",
        ["report.package_preview.present"] = "Package preview with {DescriptorCount} descriptors, {HashCount} hashes.",
        ["report.package_preview.none"] = "No package preview available.",
    };

    private static readonly Regex ParameterPattern = new(@"\{(\w+)\}", RegexOptions.Compiled);

    public string Format(string messageTemplateId, IReadOnlyDictionary<string, string> parameters)
    {
        if (!Templates.TryGetValue(messageTemplateId, out var template))
            return $"[Unknown template: {messageTemplateId}]";

        return ParameterPattern.Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            return parameters.TryGetValue(key, out var value) ? value : match.Value;
        });
    }
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/Runtime/Agent/CrestCreates.Agent.ControlPlane`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat(phase-7d): add IDescriptorReviewMessageTemplateCatalog + default impl"
```

---

### Task 5: Report Builder

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/IDescriptorReviewReportBuilder.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/ReportBuilder/DefaultDescriptorReviewReportBuilder.cs`

**Interfaces:**
- Consumes: DTO types from Task 1, `IDescriptorReviewMessageTemplateCatalog` from Task 4, `DescriptorDraftReviewResult`, `DescriptorDraft`
- Produces: `IDescriptorReviewReportBuilder` for Task 8 (service integration)

- [ ] **Step 1: Create interface**

`IDescriptorReviewReportBuilder.cs`:
```csharp
namespace CrestCreates.Agent.ControlPlane.Abstractions;

public interface IDescriptorReviewReportBuilder
{
    DescriptorReviewReportDto Build(DescriptorReviewReportBuildRequest request);
}
```

- [ ] **Step 2: Create default implementation**

`DefaultDescriptorReviewReportBuilder.cs` — This is the largest new file (~400 lines). It builds 13 sections from the review result. Key structure:

```csharp
using System.Security.Cryptography;
using System.Text;
using CrestCreates.Agent.ControlPlane.Abstractions;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.ControlPlane;

internal sealed class DefaultDescriptorReviewReportBuilder
    : IDescriptorReviewReportBuilder
{
    private readonly TimeProvider _clock;
    private readonly IDescriptorReviewMessageTemplateCatalog _templateCatalog;

    public DefaultDescriptorReviewReportBuilder(
        TimeProvider clock,
        IDescriptorReviewMessageTemplateCatalog templateCatalog)
    {
        _clock = clock;
        _templateCatalog = templateCatalog;
    }

    public DescriptorReviewReportDto Build(DescriptorReviewReportBuildRequest request)
    {
        if (!request.VisibilityApplied)
        {
            throw new InvalidOperationException(
                "DescriptorReviewReportBuilder requires a visibility-projected review result. " +
                "Call with VisibilityApplied=true after applying denied descriptor kind filtering.");
        }

        var reviewResult = request.ReviewResult;
        var draft = request.Draft;

        // Build all 13 sections
        var summarySection = BuildSummarySection(reviewResult);
        var draftIdentitySection = BuildDraftIdentitySection(draft);
        var proposedChangesSection = BuildProposedChangesSection(reviewResult);
        var impactAnalysisSection = BuildImpactAnalysisSection(reviewResult);
        var dependencySummarySection = BuildDependencySummarySection(reviewResult);
        var compatibilitySection = BuildCompatibilitySection(reviewResult);
        var governanceSection = BuildGovernanceSection(reviewResult);
        var requiredHumanReviewSection = BuildRequiredHumanReviewSection(reviewResult);
        var activationEligibilitySection = BuildActivationEligibilitySection(reviewResult);
        var diagnosticsSection = BuildDiagnosticsSection(reviewResult);
        var packagePreviewSection = BuildPackagePreviewSection(reviewResult);
        var stableHashesSection = BuildStableHashesSection(reviewResult);

        // Recommendations derived from typed state (not from rendered text)
        var recommendations = DeriveRecommendations(
            reviewResult, governanceSection, activationEligibilitySection, diagnosticsSection);
        var recommendationsSection = BuildRecommendationsSection(recommendations);

        // Source binding
        var reviewResultId = reviewResult.DraftId; // review result is keyed by draftId in current impl
        var draftVersion = draft.Version?.ToString() ?? "0";
        var sourceReviewHash = ComputeSourceReviewHash(reviewResult);
        var reportId = ComputeReportId(reviewResult.TenantId, reviewResult.DraftId, draftVersion, reviewResultId, _templateCatalog.TemplateVersion);

        return new DescriptorReviewReportDto
        {
            ReportId = reportId,
            DraftId = reviewResult.DraftId,
            TenantId = reviewResult.TenantId,
            ReviewResultId = reviewResultId,
            DraftVersion = draftVersion,
            SourceReviewHash = sourceReviewHash,
            TemplateVersion = _templateCatalog.TemplateVersion,
            GeneratedAt = _clock.GetUtcNow(),
            Recommendations = recommendations,
            SummarySection = summarySection,
            DraftIdentitySection = draftIdentitySection,
            ProposedChangesSection = proposedChangesSection,
            ImpactAnalysisSection = impactAnalysisSection,
            DependencySummarySection = dependencySummarySection,
            CompatibilitySection = compatibilitySection,
            GovernanceSection = governanceSection,
            RequiredHumanReviewSection = requiredHumanReviewSection,
            ActivationEligibilitySection = activationEligibilitySection,
            DiagnosticsSection = diagnosticsSection,
            RecommendationsSection = recommendationsSection,
            PackagePreviewSection = packagePreviewSection,
            StableHashesSection = stableHashesSection,
        };
    }

    // Each Build*Section method creates a DescriptorReviewReportSectionDto
    // using _templateCatalog.Format() for Message generation.
    // Empty sections have IsEmpty=true and Items=[].
    // ... (13 Build*Section methods + DeriveRecommendations + hash helpers)
}
```

Each `Build*Section` method follows this pattern:
1. Extract data from the review result field
2. Create `DescriptorReviewReportItemDto` entries with ReasonCode, MessageTemplateId, Parameters
3. Call `_templateCatalog.Format(templateId, parameters)` for each item's Message
4. Compute `OverallSeverity` as max of item severities (Info if empty)
5. Return `DescriptorReviewReportSectionDto` with correct Kind, SectionId, Title, Order, IsEmpty

The `DeriveRecommendations` method:
1. If `IsActivationEligible` && governance approved → `RequestActivationHandoff`
2. If governance requires review → `RequestHumanReview`
3. If diagnostics contain Blocker/Error → `ReviseDraft`
4. If diagnostics contain Warning && fix proposals available → `ApplyFixProposal`
5. If no issues → `NoAction`

Hash helpers use `SHA256` with UTF-8 encoding for deterministic stable IDs.

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/Runtime/Agent/CrestCreates.Agent.ControlPlane`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat(phase-7d): add IDescriptorReviewReportBuilder + default impl (13 sections)"
```

---

### Task 6: Report Renderer

**Files:**
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/IDescriptorReviewReportRenderer.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/ReportRenderer/DefaultDescriptorReviewReportRenderer.cs`

**Interfaces:**
- Consumes: `DescriptorReviewReportDto` from Task 1
- Produces: `IDescriptorReviewReportRenderer` for Task 8 (service integration)

- [ ] **Step 1: Create interface**

`IDescriptorReviewReportRenderer.cs`:
```csharp
namespace CrestCreates.Agent.ControlPlane.Abstractions;

public interface IDescriptorReviewReportRenderer
{
    string RenderMarkdown(DescriptorReviewReportDto report);
    string RenderPlainText(DescriptorReviewReportDto report);
}
```

- [ ] **Step 2: Create default implementation**

`DefaultDescriptorReviewReportRenderer.cs` (~200 lines):

```csharp
using System.Text;
using CrestCreates.Agent.ControlPlane.Abstractions;

namespace CrestCreates.Agent.ControlPlane;

internal sealed class DefaultDescriptorReviewReportRenderer
    : IDescriptorReviewReportRenderer
{
    public string RenderMarkdown(DescriptorReviewReportDto report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Review Report: {report.ReportId}");
        sb.AppendLine();
        sb.AppendLine($"- **Draft**: {report.DraftId}");
        sb.AppendLine($"- **Tenant**: {report.TenantId}");
        sb.AppendLine($"- **Generated**: {report.GeneratedAt:O}");
        sb.AppendLine($"- **Contract Version**: {report.ContractVersion}");
        sb.AppendLine();

        // Render each non-empty section
        foreach (var section in GetSectionsInOrder(report))
        {
            if (section.IsEmpty) continue;  // Hide empty sections by default
            RenderMarkdownSection(sb, section);
        }

        // Render top-level recommendations
        if (report.Recommendations.Count > 0)
        {
            sb.AppendLine("## Recommendations");
            sb.AppendLine();
            foreach (var rec in report.Recommendations)
            {
                sb.AppendLine($"- **{rec.Kind}**: {rec.Message}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public string RenderPlainText(DescriptorReviewReportDto report)
    {
        // Similar structure but plain text formatting
        // No markdown syntax, just indentation and separators
    }

    private static IEnumerable<DescriptorReviewReportSectionDto> GetSectionsInOrder(DescriptorReviewReportDto report)
    {
        yield return report.SummarySection;
        yield return report.DraftIdentitySection;
        yield return report.ProposedChangesSection;
        yield return report.ImpactAnalysisSection;
        yield return report.DependencySummarySection;
        yield return report.CompatibilitySection;
        yield return report.GovernanceSection;
        yield return report.RequiredHumanReviewSection;
        yield return report.ActivationEligibilitySection;
        yield return report.DiagnosticsSection;
        yield return report.RecommendationsSection;
        yield return report.PackagePreviewSection;
        yield return report.StableHashesSection;
    }

    private static void RenderMarkdownSection(StringBuilder sb, DescriptorReviewReportSectionDto section)
    {
        sb.AppendLine($"## {section.Title}");
        sb.AppendLine();
        foreach (var item in section.Items)
        {
            var severityBadge = item.Severity switch
            {
                DescriptorReviewSeverity.Blocker => "🔴",
                DescriptorReviewSeverity.Error => "🟠",
                DescriptorReviewSeverity.Warning => "🟡",
                _ => "🟢",
            };
            sb.AppendLine($"- {severityBadge} **[{item.ReasonCode}]** {item.Message}");
        }
        sb.AppendLine();
    }
}
```

Key: Renderer reads `item.Message` from the DTO — it does NOT call TemplateCatalog. It only formats structure (headers, lists, severity markers).

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/Runtime/Agent/CrestCreates.Agent.ControlPlane`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat(phase-7d): add IDescriptorReviewReportRenderer + Markdown/PlainText impl"
```

---

### Task 7: Contract Version Bump + JSON Context + Manifest + Service Constants

**Files:**
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/Json/AgentControlPlaneContractVersion.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/Json/AgentControlPlaneToolJsonSerializerContext.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/AgentToolName.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/AgentToolCategory.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/AgentToolPermissionName.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/StaticAgentToolManifestProvider.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentToolVisibilityCoverage.cs`

**Interfaces:**
- Consumes: All new types from Tasks 1-3
- Produces: Updated constants, manifest entries, JSON registrations for Task 8

- [ ] **Step 1: Bump contract version**

In `AgentControlPlaneContractVersion.cs`, change:
```csharp
public const string Current = "7d.v1";  // was "7c.v1"
```

- [ ] **Step 2: Add tool name constants**

In `AgentToolName.cs`, add after the Review section:
```csharp
// ── Review Report (Phase 7d) ──
public const string BuildDescriptorReviewReport = nameof(BuildDescriptorReviewReport);
public const string RenderDescriptorReviewReport = nameof(RenderDescriptorReviewReport);
```

- [ ] **Step 3: Add tool category**

In `AgentToolCategory.cs`, add:
```csharp
ReviewReport  // Phase 7d
```

- [ ] **Step 4: Add permission names**

In `AgentToolPermissionName.cs`, add:
```csharp
public const string ReviewReportBuild = "agent.review.report.build";
public const string ReviewReportRender = "agent.review.report.render";
```

- [ ] **Step 5: Add manifest entries**

In `StaticAgentToolManifestProvider.cs`, add 2 new tool entries in a new section after Review:
```csharp
// ── Review Report (Phase 7d) ──
new()
{
    Name = AgentToolName.BuildDescriptorReviewReport,
    Description = "Build a structured review report from a draft review result.",
    Category = AgentToolCategory.ReviewReport,
    Permissions = [Perm(AgentToolPermissionName.ReviewReportBuild, AgentToolCategory.ReviewReport, true)],
    AllowedActors = allActors,
    IsReadOnly = true,
    MutatesRuntimeRegistry = false
},
new()
{
    Name = AgentToolName.RenderDescriptorReviewReport,
    Description = "Render a review report as Markdown or PlainText.",
    Category = AgentToolCategory.ReviewReport,
    Permissions = [Perm(AgentToolPermissionName.ReviewReportRender, AgentToolCategory.ReviewReport, true)],
    AllowedActors = allActors,
    IsReadOnly = true,
    MutatesRuntimeRegistry = false
},
```

- [ ] **Step 6: Add visibility coverage entries**

In `AgentToolVisibilityCoverage.cs`, add:
```csharp
new(AgentToolName.BuildDescriptorReviewReport, AgentToolResourceShape.Indirect),
new(AgentToolName.RenderDescriptorReviewReport, AgentToolResourceShape.Indirect),
```

- [ ] **Step 7: Add JSON context registrations**

In `AgentControlPlaneToolJsonSerializerContext.cs`, add a new wave section:
```csharp
// ── Wave 8 — Review Report (Phase 7d) ──
[JsonSerializable(typeof(DescriptorReviewReportDto))]
[JsonSerializable(typeof(DescriptorReviewReportSectionDto))]
[JsonSerializable(typeof(DescriptorReviewReportItemDto))]
[JsonSerializable(typeof(DescriptorReviewRecommendationDto))]
[JsonSerializable(typeof(DescriptorReviewReportBuildRequest))]
[JsonSerializable(typeof(DescriptorReviewReportSectionKind))]
[JsonSerializable(typeof(DescriptorReviewSeverity))]
[JsonSerializable(typeof(DescriptorReviewRecommendationKind))]
[JsonSerializable(typeof(DescriptorReviewReportFormat))]
[JsonSerializable(typeof(FixProposalKind))]
[JsonSerializable(typeof(FixProposalApplicability))]
[JsonSerializable(typeof(FixProposalActionSafetyLevel))]
[JsonSerializable(typeof(System.Text.Json.JsonElement))]
```

Also update the existing `FixProposal` and `FixProposalAction` registrations (they changed shape).

- [ ] **Step 8: Build to verify**

Run: `dotnet build src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions && dotnet build src/Runtime/Agent/CrestCreates.Agent.ControlPlane`
Expected: PASS

- [ ] **Step 9: Commit**

```bash
git add -A && git commit -m "feat(phase-7d): bump contract version, add manifest/JSON/constants for 2 new tools"
```

---

### Task 8: Service Integration — New Tool Methods + Fix Proposal Migration

**Files:**
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/IAgentControlPlaneToolService.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/DefaultAgentControlPlaneToolService.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentControlPlaneServiceCollectionExtensions.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentControlPlaneArtifactEntries.cs`

**Interfaces:**
- Consumes: All types from Tasks 1-7
- Produces: Working service with 2 new tools + migrated FixProposal methods

- [ ] **Step 1: Add interface methods**

In `IAgentControlPlaneToolService.cs`, add after the Review section:
```csharp
// ── Wave 3d — Review Report (Phase 7d) ──

Task<AgentToolResult<DescriptorReviewReportDto>> BuildDescriptorReviewReportAsync(
    AgentToolInvocationContext context,
    string draftId,
    CancellationToken ct = default);

Task<AgentToolResult<string>> RenderDescriptorReviewReportAsync(
    AgentToolInvocationContext context,
    DescriptorReviewReportDto report,
    DescriptorReviewReportFormat format,
    CancellationToken ct = default);
```

- [ ] **Step 2: Add artifact snapshot type**

In `AgentControlPlaneArtifactEntries.cs`, add:
```csharp
internal sealed record ReportResourceSnapshot(DescriptorReviewReportDto Report, Draft Owner);
```

- [ ] **Step 3: Add DI registrations**

In `AgentControlPlaneServiceCollectionExtensions.cs`, add before the `DefaultAgentControlPlaneToolService` registration:
```csharp
services.TryAddSingleton<IDescriptorReviewReportBuilder, DefaultDescriptorReviewReportBuilder>();
services.TryAddSingleton<IDescriptorReviewReportRenderer, DefaultDescriptorReviewReportRenderer>();
services.TryAddSingleton<IDescriptorReviewMessageTemplateCatalog, DefaultDescriptorReviewMessageTemplateCatalog>();
```

Add required usings for the new types.

- [ ] **Step 4: Add service fields + report store**

In `DefaultAgentControlPlaneToolService.cs`, add fields:
```csharp
private readonly IDescriptorReviewReportBuilder _reportBuilder;
private readonly IDescriptorReviewReportRenderer _reportRenderer;
private readonly ConcurrentDictionary<(string TenantId, string Id), ReportResourceSnapshot> _reports = new();
```

Update constructor to accept and assign the new dependencies.

- [ ] **Step 5: Implement BuildDescriptorReviewReportAsync**

```csharp
public async Task<AgentToolResult<DescriptorReviewReportDto>> BuildDescriptorReviewReportAsync(
    AgentToolInvocationContext context, string draftId, CancellationToken ct = default)
{
    return await ExecuteAsync(context, AgentToolName.BuildDescriptorReviewReport,
        AgentToolPermissionName.ReviewReportBuild, async (scope, ct) =>
    {
        var draftResolution = await _resourceResolver.ResolveDraftAsync(context.TenantId, draftId, ct);
        if (draftResolution.Status == ResourceResolutionStatus.NotFound)
            return await RecordAndReturn(context,
                AgentToolResult<DescriptorReviewReportDto>.NotFound($"Draft '{draftId}' not found."));

        var draft = draftResolution.Snapshot!.Draft;
        var denyResult = DenyIfInvisible<DescriptorReviewReportDto>(context, scope, draft.DescriptorKind);
        if (denyResult is not null) return denyResult;

        // Find the latest review result for this draft
        var reviewSnapshot = _reviewResults.Values
            .Where(r => r.Owner.Id == draftId && r.Owner.TenantId == context.TenantId)
            .OrderByDescending(r => r.Review.DraftId) // latest
            .FirstOrDefault();
        if (reviewSnapshot is null)
            return await RecordAndReturn(context,
                AgentToolResult<DescriptorReviewReportDto>.Failed(
                    [new() { Code = "NO_REVIEW_RESULT", Severity = AgentToolDiagnosticSeverity.Error,
                        Message = $"No review result found for draft '{draftId}'. Run ReviewDescriptorDraft first." }],
                    BuildAudit(context, AgentToolResultStatus.Failed)));

        var request = new DescriptorReviewReportBuildRequest
        {
            ReviewResult = reviewSnapshot.Review,
            Draft = draft,
            VisibilityApplied = true
        };

        var report = _reportBuilder.Build(request);
        var reportId = report.ReportId;
        _reports[(context.TenantId, reportId)] = new ReportResourceSnapshot(report, draft);

        var audit = BuildAudit(context, AgentToolResultStatus.Success);
        await _auditor.RecordAsync(audit, ct);
        return AgentToolResult<DescriptorReviewReportDto>.Success(report, [], audit);
    }, ct);
}
```

- [ ] **Step 6: Implement RenderDescriptorReviewReportAsync**

```csharp
public async Task<AgentToolResult<string>> RenderDescriptorReviewReportAsync(
    AgentToolInvocationContext context, DescriptorReviewReportDto report,
    DescriptorReviewReportFormat format, CancellationToken ct = default)
{
    return await ExecuteAsync(context, AgentToolName.RenderDescriptorReviewReport,
        AgentToolPermissionName.ReviewReportRender, async (scope, ct) =>
    {
        // Validate contract version
        if (report.ContractVersion != AgentControlPlaneContractVersion.Current)
        {
            var diag = new AgentToolDiagnostic
            {
                Code = "UNSUPPORTED_REPORT_CONTRACT_VERSION",
                Severity = AgentToolDiagnosticSeverity.Error,
                Message = $"Report contract version '{report.ContractVersion}' is not supported. Current: '{AgentControlPlaneContractVersion.Current}'."
            };
            var audit = BuildAudit(context, AgentToolResultStatus.InvalidRequest, [diag]);
            await _auditor.RecordAsync(audit, ct);
            return AgentToolResult<string>.InvalidRequest([diag], audit);
        }

        var rendered = format switch
        {
            DescriptorReviewReportFormat.Markdown => _reportRenderer.RenderMarkdown(report),
            DescriptorReviewReportFormat.PlainText => _reportRenderer.RenderPlainText(report),
            _ => null
        };

        if (rendered is null)
        {
            var diag = new AgentToolDiagnostic
            {
                Code = "UNSUPPORTED_REPORT_FORMAT",
                Severity = AgentToolDiagnosticSeverity.Error,
                Message = $"Report format '{format}' is not supported."
            };
            var audit = BuildAudit(context, AgentToolResultStatus.InvalidRequest, [diag]);
            await _auditor.RecordAsync(audit, ct);
            return AgentToolResult<string>.InvalidRequest([diag], audit);
        }

        var successAudit = BuildAudit(context, AgentToolResultStatus.Success);
        await _auditor.RecordAsync(successAudit, ct);
        return AgentToolResult<string>.Success(rendered, [], successAudit);
    }, ct);
}
```

- [ ] **Step 7: Migrate SuggestDescriptorDraftFixesAsync to new FixProposal shape**

Update `GenerateFixActions` to:
- Use `FixProposalActionKind.SetValue` instead of `FixProposalActionKind.Set`
- Use `TargetPath` instead of `Path`
- Create `JsonElement?` values via `JsonSerializer.SerializeToElement(value)`
- Populate all new FixProposal fields: `Kind`, `Title`, `Explanation`, `ReasonCode`, `Applicability`, `IsExecutable`, `RequiresManualAction`, `BlocksActivationUntilResolved`, `RelatedDiagnosticIds`, `RelatedDescriptorIds`, `ContractVersion`
- Set `FixProposalAction.IsExecutable = true`, `SafetyLevel = Safe`
- Use `Id` instead of `ProposalId`

- [ ] **Step 8: Migrate ApplyFixProposalToDraftAsync to new contract**

Update to:
- Check `proposal.Actions.Count > 1` → return `UnsupportedMultiActionFixProposal` diagnostic
- Check `action.IsExecutable == false` → return `NonExecutableFixAction`
- Check `action.Kind` not in `{SetValue, RemoveValue, AddValue}` → return `UnsupportedFixActionKind`
- Check `action.SafetyLevel == Unsafe` → return `UnsafeFixActionRejected`
- Check target is active descriptor → return `FixActionTargetBoundaryViolation`
- Check target path not in `{Intent, Rationale, ProposedVersion, CorrelationId}` → return `FixActionTargetNotAllowed`
- Use `action.TargetPath` instead of `action.Path`
- Use `action.Kind` instead of `action.ActionKind`
- Read `action.ProposedValue` as `JsonElement?` → deserialize to string for draft field assignment

- [ ] **Step 9: Build to verify**

Run: `dotnet build src/Runtime/Agent/CrestCreates.Agent.ControlPlane`
Expected: PASS

- [ ] **Step 10: Commit**

```bash
git add -A && git commit -m "feat(phase-7d): integrate Report Builder/Renderer into service, migrate FixProposal methods"
```

---

### Task 9: Fix Test Compilation Errors

**Files:**
- Modify: All test files that construct `FixProposal` / `FixProposalAction` / reference old enum values

**Interfaces:**
- Consumes: Upgraded types from Task 3, service changes from Task 8
- Produces: Compiling test project

- [ ] **Step 1: Build test project to identify all compilation errors**

Run: `dotnet build tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests`
Expected: Multiple CS0117/CS0103 errors from renamed fields/enums

- [ ] **Step 2: Fix all compilation errors**

Key changes needed across test files:
- `FixProposalActionKind.Set` → `FixProposalActionKind.SetValue`
- `FixProposalActionKind.Remove` → `FixProposalActionKind.RemoveValue`
- `FixProposalActionKind.Add` → `FixProposalActionKind.AddValue`
- `action.Path` → `action.TargetPath`
- `action.ActionKind` → `action.Kind`
- `action.CurrentValue` / `action.ProposedValue` (string) → `JsonElement?` via `JsonSerializer.SerializeToElement(value)`
- `proposal.ProposalId` → `proposal.Id`
- `FixProposal` construction: add all new required fields (`Kind`, `Title`, `Explanation`, `ReasonCode`, `Applicability`, `IsExecutable`, `RequiresManualAction`, `BlocksActivationUntilResolved`, `ContractVersion`)
- `FixProposalAction` construction: add `IsExecutable`, `SafetyLevel`

- [ ] **Step 3: Build test project to verify all errors fixed**

Run: `dotnet build tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests`
Expected: PASS

- [ ] **Step 4: Run existing tests**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --no-build`
Expected: Some failures from changed behavior (e.g., multi-action rejection, new diagnostic codes)

- [ ] **Step 5: Fix test failures**

Update test assertions:
- Tests expecting multi-action apply to succeed → should now get `UnsupportedMultiActionFixProposal`
- Tests using old enum names → already fixed in Step 2
- Tests expecting `Set`/`Remove`/`Add` → now `SetValue`/`RemoveValue`/`AddValue`

- [ ] **Step 6: Run tests until all pass**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests`
Expected: ALL PASS

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "fix(phase-7d): migrate tests to upgraded FixProposal/FixProposalAction contract"
```

---

### Task 10: Report Builder Tests

**Files:**
- Create: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/ReportBuilderTests.cs`

**Interfaces:**
- Consumes: `DefaultDescriptorReviewReportBuilder`, `DefaultDescriptorReviewMessageTemplateCatalog`, DTO types

- [ ] **Step 1: Write builder tests**

Test class covering all spec Section 5.1 tests:
- `Build_AllowedDraft_ProducesReportWith_RequestActivationHandoff`
- `Build_ReviewRequiredDraft_ProducesReportWith_RequestHumanReview`
- `Build_BlockedDraft_ProducesReportWith_NoActivation`
- `Build_AllSections_AlwaysPresent`
- `Build_SectionOrder_IsDeterministic`
- `Build_SectionOrder_MatchesCanonicalSectionOrder`
- `Build_DiagnosticsGroupedBySeverity`
- `Build_EmptyDiagnostics_ProducesUsefulSummary`
- `Build_VisibilityAppliedFalse_ThrowsInvalidOperationException`
- `Build_ReportId_IsStableHash`
- `Build_IsActivationEligible_IsExplanationNotGate`
- `Build_BlocksActivationUntilResolved_IsExplanationNotGate`
- `Build_DeniedDescriptorKind_NotPresent_InReportItems`
- `Build_RequiresManualAction_ConsistentWithApplicability`

Each test constructs a `DescriptorDraftReviewResult` + `DescriptorDraft`, calls `builder.Build(request)`, and asserts on the resulting `DescriptorReviewReportDto`.

- [ ] **Step 2: Run tests**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter "ReportBuilderTests"`
Expected: ALL PASS

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "test(phase-7d): add Report Builder tests"
```

---

### Task 11: Renderer + Template Catalog Tests

**Files:**
- Create: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/ReportRendererTests.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/MessageTemplateCatalogTests.cs`

- [ ] **Step 1: Write template catalog tests**

- `Format_KnownTemplateId_ReturnsFormattedMessage`
- `Format_UnknownTemplateId_ReturnsFallbackMessage`
- `Format_SameInput_DeterministicOutput`

- [ ] **Step 2: Write renderer tests**

- `RenderMarkdown_AllSections_Rendered`
- `RenderMarkdown_EmptySections_OptionallyHidden`
- `RenderMarkdown_Deterministic`
- `RenderPlainText_Deterministic`
- `Renderer_UsesDtoMessage_NotTemplateCatalog`
- `Renderer_DoesNotRequireExternalServices`
- `Renderer_Deterministic_WithSameDto`
- `RenderMarkdown_DeniedDescriptorKind_NotRendered`
- `RenderPlainText_DeniedDescriptorKind_NotRendered`

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter "ReportRendererTests|MessageTemplateCatalogTests"`
Expected: ALL PASS

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "test(phase-7d): add Renderer and Template Catalog tests"
```

---

### Task 12: Fix Proposal Contract + Apply Tests

**Files:**
- Create: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/FixProposalContractTests.cs`
- Modify: existing apply fix proposal tests

- [ ] **Step 1: Write contract tests**

- `FixProposal_IsExecutable_AggregationRule`
- `FixProposal_ContractVersion_Present`
- `FixProposalAction_TargetPath_NotNull`
- `FixProposalAction_JsonElement_RoundTrip_String`
- `FixProposalAction_JsonElement_RoundTrip_Number`
- `FixProposalAction_JsonElement_RoundTrip_Object`
- `FixProposalAction_JsonElement_RoundTrip_Null`
- `FixProposalActionKind_Values_1Through10`

- [ ] **Step 2: Write apply tests**

- `Apply_NonExecutableAction_Returns_NonExecutableFixAction`
- `Apply_UnsupportedKind_Returns_UnsupportedFixActionKind`
- `Apply_UnsafeAction_Returns_UnsafeFixActionRejected`
- `Apply_BoundaryViolation_Returns_FixActionTargetBoundaryViolation`
- `Apply_NotAllowedTarget_Returns_FixActionTargetNotAllowed`
- `Apply_SupportedAction_Succeeds`
- `Apply_MixedSupportedAndUnsupportedActions_FailsWithoutMutation`
- `Apply_MultiActionProposal_Returns_UnsupportedMultiActionFixProposal`

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --filter "FixProposalContractTests"`
Expected: ALL PASS

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "test(phase-7d): add FixProposal contract and Apply tests"
```

---

### Task 13: Service Integration + Coverage + Boundary Tests

**Files:**
- Create: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/ReviewReportServiceTests.cs`
- Modify: `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/ToolContractCoverageTests.cs`

- [ ] **Step 1: Write service integration tests**

- `BuildDescriptorReviewReport_ByDto_DoesNotRequireStoredReport`
- `BuildDescriptorReviewReport_ByDto_Deterministic`
- `RenderDescriptorReviewReport_RejectsUnsupportedContractVersion`
- `RenderDescriptorReviewReport_RejectsUnsupportedFormat`
- `Manifest_DoesNotInclude_RenderStoredDescriptorReviewReport`

- [ ] **Step 2: Write coverage tests**

- `AllPublicToolContractDtos_Have_JsonTypeInfo` (update to include new types)
- `ManifestToolNames_Match_ContractRegistrations` (update for 34 tools)
- `Manifest_Includes_BuildDescriptorReviewReport`
- `Manifest_Includes_RenderDescriptorReviewReport`
- `Manifest_DoesNotInclude_ApproveOrActivateTools`
- `ToolCount_Is_34`

- [ ] **Step 3: Write boundary tests**

- `ReportBuilder_DoesNot_PerformVisibilityFiltering`
- `Renderer_DoesNot_MutateGovernanceOrActivation`
- `FixProposal_DoesNot_MutateActiveRegistry`
- `FixProposal_BlocksActivationUntilResolved_IsExplanationOnly`

- [ ] **Step 4: Run all tests**

Run: `dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests`
Expected: ALL PASS

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "test(phase-7d): add service integration, coverage, and boundary tests"
```

---

### Task 14: Final Build Verification + memory.md Update

**Files:**
- Modify: `memory.md`

- [ ] **Step 1: Full solution build**

Run: `dotnet build`
Expected: PASS

- [ ] **Step 2: Full test run**

Run: `dotnet test`
Expected: ALL PASS

- [ ] **Step 3: Update memory.md**

Add Phase 7d completion entry under Completed Features:
- Review Report DTO (13 sections, source binding, stable ReportId)
- Report Builder (IDescriptorReviewReportBuilder + default impl, VisibilityApplied fail-fast)
- Message Template Catalog (IDescriptorReviewMessageTemplateCatalog + default impl)
- Report Renderer (Markdown + PlainText, deterministic, no external services)
- FixProposal contract upgrade (8 kinds, 10 action kinds, 4 applicability levels, 4 safety levels, JsonElement? values)
- Apply runtime (single-action only, 6 diagnostic codes for rejection)
- 2 new tools (BuildDescriptorReviewReport, RenderDescriptorReviewReport)
- Contract version bumped to 7d.v1
- Test count

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "docs(phase-7d): update memory.md with completion status"
```
