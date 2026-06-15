# Descriptor — Usage Guide

> This document is for CrestCreates module developers who need to consume or extend descriptor relationship extraction and topology queries.
> *Phase 6a (2026-06-12): Descriptor Relationship Coverage — 6 extractors, 1 provider, 434 tests*
> *Phase 6b (2026-06-12): Descriptor Topology Read Model — builder, snapshot, diagnostics, consumer index, 146 Metadata.Tests*
> *Phase 6c (2026-06-13): Impact Analysis Engine — analyzer, change set builder, severity model, 48 tests*
> *Phase 6d (2026-06-13): Compatibility Analyzer — 6 descriptor-specific rules, generic change-kind rules*
> *Phase 6e (2026-06-15): Lifecycle Governance — decision gate, 48 tests*

---

## 1. Quick Start

### 1.1 Register the Kernel

```csharp
using CrestCreates.Metadata;

var builder = WebApplication.CreateBuilder(args);

// Register the relationship provider + Schema extractor
builder.Services.AddRelationshipKernel();

// Each domain module registers its own extractor:
builder.Services.AddFormKernel();          // registers FormRelationshipExtractor
builder.Services.AddCapabilityRuntime();   // registers CapabilityRelationshipExtractor
builder.Services.AddEventKernel();         // registers EventRelationshipExtractor
builder.Services.AddHumanTaskRuntime();    // registers HumanTaskRelationshipExtractor
builder.Services.AddWorkflowEngine();      // registers WorkflowRelationshipExtractor
builder.Services.AddTopologyKernel();             // Phase 6b: topology builder
builder.Services.AddDescriptorImpactAnalysis();   // Phase 6c: impact analyzer + change set builder
builder.Services.AddDescriptorCompatibilityAnalysis(); // Phase 6d: compatibility analyzer
builder.Services.AddDescriptorLifecycleGovernance();   // Phase 6e: lifecycle governance gate
```

### 1.2 Query Relationships

```csharp
using CrestCreates.Metadata.Abstractions;

public class MyService
{
    private readonly IDescriptorRelationshipProvider _relationshipProvider;

    public MyService(IDescriptorRelationshipProvider relationshipProvider)
    {
        _relationshipProvider = relationshipProvider;
    }

    public void AnalyzeCapability(CapabilityDescriptor capability)
    {
        var relationships = _relationshipProvider.GetRelationships(capability);

        foreach (var rel in relationships)
        {
            Console.WriteLine($"{rel.From.FullId} --[{rel.Kind}]--> {rel.To.FullId}");
            // e.g., "capability.approve-order --[Consumes]--> schema.order-input"
        }
    }
}
```

---

## 2. Understanding Relationships

### 2.1 The DescriptorRelationship Record

```csharp
public sealed record DescriptorRelationship(
    DescriptorRef From,           // Source descriptor: Namespace, Id, Version
    DescriptorRef To,             // Target descriptor: Namespace, Id, Version
    RelationshipKind Kind,        // What kind of relationship this is
    string? Role,                 // Semantic role: "InputSchema", "OutputSchema", "CapabilityStep"
    string? SourcePath,           // Property path on source: "InputSchema", "Steps"
    RelationshipStrength Strength,// Strong (breaks if missing) | Weak (optional)
    bool IsRuntimeBinding);       // true if requires runtime handler execution
```

### 2.2 Relationship Kinds

| Kind | When to Use |
|------|-------------|
| `Produces` | Source emits/creates target |
| `Consumes` | Source reads/ingests target |
| `DependsOn` | Source is a successor/replacement of target |
| `References` | Loose reference (schema field types, unsupported features) |
| `Uses` | Broad consumption (Form→Schema, Event→Schema) |
| `Triggers` | Source causes target to execute at runtime |

### 2.3 Strength

| Strength | Meaning |
|----------|---------|
| `Strong` | This relationship is required — missing target breaks the source descriptor |
| `Weak` | This relationship is optional — the source can function without the target |

### 2.4 IsRuntimeBinding

- `true` — this relationship represents a runtime execution dependency (e.g., Workflow step triggers a Capability handler)
- `false` — this relationship is structural/reference-only (e.g., Form references a Schema)

---

## 3. Consuming Relationship Data

### 3.1 Filter by Kind

```csharp
var relationships = _provider.GetRelationships(descriptor);

// Find all schema dependencies
var schemaDeps = relationships
    .Where(r => r.To.Namespace == "schema")
    .ToList();

// Find all runtime-trigger dependencies
var triggers = relationships
    .Where(r => r.Kind == RelationshipKind.Triggers && r.IsRuntimeBinding)
    .ToList();

// Find critical (Strong) dependencies
var critical = relationships
    .Where(r => r.Strength == RelationshipStrength.Strong)
    .ToList();
```

### 3.2 Build a Dependency Map

```csharp
var allDescriptors = GetMyDescriptors(); // from registries
var adjacency = new Dictionary<string, List<(string Target, RelationshipKind Kind)>>();

foreach (var desc in allDescriptors)
{
    var rels = _provider.GetRelationships(desc);
    adjacency[desc.FullId] = rels
        .Select(r => (r.To.FullId, r.Kind))
        .ToList();
}
```

### 3.3 Validate Descriptor Completeness (Pre-Build)

```csharp
public IReadOnlyList<string> FindMissingDependencies(IDescriptor descriptor)
{
    var rels = _provider.GetRelationships(descriptor);

    return rels
        .Where(r => r.Strength == RelationshipStrength.Strong)
        .Where(r => !RegistryContains(r.To))  // your lookup logic
        .Select(r => $"{r.Role}: {r.To.FullId} not found")
        .ToList();
}
```

---

## 4. Adding a New Descriptor Kind (Extending the System)

### 4.1 Create an Extractor

```csharp
using CrestCreates.Metadata.Abstractions;

public sealed class MyNewDescriptorExtractor
    : DescriptorRelationshipExtractorBase<MyNewDescriptor>
{
    public override DescriptorKind SupportedKind => DescriptorKind.YourNewKind;

    protected override IReadOnlyList<DescriptorRelationship> Extract(MyNewDescriptor descriptor)
    {
        var relationships = new List<DescriptorRelationship>();

        // Map each outgoing reference:
        relationships.Add(new DescriptorRelationship(
            From: new DescriptorRef("your-namespace", descriptor.Id, descriptor.Version),
            To: new DescriptorRef("target-namespace", descriptor.TargetRef.Id, descriptor.TargetRef.Version),
            Kind: RelationshipKind.Uses,
            Role: "TargetRef",
            SourcePath: "TargetRef",
            Strength: RelationshipStrength.Strong));

        return relationships;
    }
}
```

### 4.2 Register the Extractor

```csharp
public static IServiceCollection AddMyModuleKernel(this IServiceCollection services)
{
    // ... other registrations ...

    services.AddSingleton<IDescriptorRelationshipExtractor, MyNewDescriptorExtractor>();

    return services;
}
```

### 4.3 Write Tests

```csharp
public class MyNewDescriptorExtractorTests
{
    private readonly MyNewDescriptorExtractor _extractor = new();

    [Fact]
    public void Extract_Returns_TargetRef_Relationship()
    {
        var descriptor = new MyNewDescriptor
        {
            Id = "my-desc",
            Version = 1,
            TargetRef = new VersionedDescriptorRef<TargetDescriptor> { Id = "target", Version = 1 }
        };

        var relationships = _extractor.Extract(descriptor);

        relationships.Should().HaveCount(1);
        var rel = relationships[0];
        rel.From.Namespace.Should().Be("your-namespace");
        rel.From.Id.Should().Be("my-desc");
        rel.From.Version.Should().Be(1);
        rel.To.Id.Should().Be("target");
        rel.Kind.Should().Be(RelationshipKind.Uses);
    }
}
```

---

## 5. Key Design Rules

### 5.1 Extractors Are Pure Projections

Extractors **project** descriptor data into relationship records. They do NOT:
- Validate that referenced descriptors exist (that's the validator/binding status job)
- Resolve or enrich references (that's the resolver/Phase 6b job)
- Mutate anything

### 5.2 Non-Nullable Struct Refs Are Always Emitted

`VersionedDescriptorRef<T>` is a record struct — it is never null. Extractors always emit a relationship for it, even if its `Id` is empty. Structural validation (empty Id, unresolvable target) is handled by validators and binding status contributors.

### 5.3 Nullable Refs Are Conditionally Emitted

`VersionedDescriptorRef<T>?` fields (e.g., `CapabilityDescriptor.InputSchema`) are checked with `.HasValue`. If null, the relationship is omitted.

### 5.4 DescriptorKind ≠ Concrete Type

The provider dispatches by `DescriptorType.IsInstanceOfType()`, not `DescriptorKind`. This ensures correct behavior when one Kind has multiple concrete types (e.g., `EventDescriptor` vs `GeneratedEventDescriptor`).

### 5.5 Don't Reintroduce IRelationshipAwareDescriptor

Descriptive relationship logic belongs in extractors, not in descriptors. Descriptors stay POCOs.

---

## 6. Troubleshooting

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| `GetRelationships()` returns empty for a known descriptor type | No extractor registered for that concrete type | Check DI registration — `services.AddSingleton<IDescriptorRelationshipExtractor, YourExtractor>()` |
| `EventDescriptor` relationships are empty | By design — `EventRelationshipExtractor` handles `GeneratedEventDescriptor` only | Use `GeneratedEventDescriptor` (the registry main-path type) or register a second extractor |
| Missing `From.Version` | Older code that doesn't pass `descriptor.Version` | Always include Version: `new DescriptorRef(ns, id, version)` |
| Duplicate relationships for same ref | Two extractors registered for same `DescriptorType` | Use `TryAddSingleton` or check for duplicate registrations |

---

## 7. Phase 6b: Topology Queries

### 7.1 Register the Topology Kernel

```csharp
using CrestCreates.Metadata;

builder.Services.AddTopologyKernel();  // registers IDescriptorTopologyBuilder
```

### 7.2 Build a Topology Snapshot

```csharp
var topologyBuilder = services.GetRequiredService<IDescriptorTopologyBuilder>();
var descriptors = GetMyDescriptors();  // from registries

var snapshot = topologyBuilder.Build(descriptors);
```

### 7.3 Query Direct Dependencies

```csharp
var myCapability = new DescriptorRef("capability", "approve-order", version: 2);

// What does this capability depend on?
var deps = snapshot.GetDirectDependencies(myCapability);
foreach (var dep in deps)
    Console.WriteLine($"{dep.Ref.FullId} ({dep.Kind}, {dep.Name})");

// What depends on this capability?
var consumers = snapshot.GetDirectDependents(myCapability);
```

### 7.4 Transitive Traversal

```csharp
// All downstream dependencies (Strong edges only by default)
var allDownstream = snapshot.GetTransitiveDependencies(myCapability);

// Include Weak edges for full impact analysis
var fullDownstream = snapshot.GetTransitiveDependencies(myCapability, includeWeak: true);

// All upstream consumers (reversed graph)
var allConsumers = snapshot.GetTransitiveDependents(myCapability);
```

### 7.5 Consumer Index (Version-Aware)

```csharp
// All consumers of "schema.User" regardless of version
var all = snapshot.GetConsumers("schema", "User");

// Consumers of "schema.User" version 2 exactly + unpinned consumers
var v2 = snapshot.GetConsumers("schema", "User", version: 2);
```

### 7.6 Check Diagnostics

```csharp
var snapshot = topologyBuilder.Build(descriptors);

if (!snapshot.Diagnostics.IsHealthy)
{
    foreach (var diag in snapshot.Diagnostics.Errors)
        Console.WriteLine($"ERROR [{diag.Code}]: {diag.Message}");

    foreach (var diag in snapshot.Diagnostics.Warnings)
        Console.WriteLine($"WARN [{diag.Code}]: {diag.Message}");
}

// Quick checks:
snapshot.Diagnostics.HasErrors      // true if any MISSING_TARGET(Strong) or STRONG_CYCLE
snapshot.Diagnostics.IsHealthy      // true if no errors
```

### 7.7 Lookup with Version-Aware Resolution

```csharp
// Exact match
var node = snapshot.FindNode(new DescriptorRef("schema", "User", 2));

// Unpinned ref resolves to any version
var anyVersion = snapshot.FindNode(new DescriptorRef("schema", "User", null));
// → returns version 2 node (or whatever version exists)
```

---

## 8. Phase 6c: Impact Analysis

### 8.1 Register the Impact Analysis Services

```csharp
using CrestCreates.Metadata;

builder.Services.AddTopologyKernel();                // Phase 6b — required
builder.Services.AddDescriptorImpactAnalysis();      // Phase 6c
```

### 8.2 Build a Change Set

Compare two descriptor inventories to detect what changed:

```csharp
var changeSetBuilder = services.GetRequiredService<IDescriptorChangeSetBuilder>();

var beforeDescriptors = GetDescriptorsFromRegistry();   // e.g., from package snapshot
var afterDescriptors = GetCurrentDescriptors();          // e.g., after update

var changeSet = changeSetBuilder.Build(beforeDescriptors, afterDescriptors);

foreach (var change in changeSet.Changes)
    Console.WriteLine($"{change.Ref.FullId}: {change.Kind}");
// e.g., "schema.Order@1: Removed", "capability.ProcessOrder@2: ContractHashChanged"
```

### 8.3 Run Impact Analysis

```csharp
var topologyBuilder = services.GetRequiredService<IDescriptorTopologyBuilder>();
var analyzer = services.GetRequiredService<IDescriptorImpactAnalyzer>();

var snapshot = topologyBuilder.Build(currentDescriptors);
var report = analyzer.Analyze(snapshot, changeSet);

Console.WriteLine($"Max severity: {report.MaxSeverity}");

foreach (var affected in report.AffectedDescriptors)
{
    Console.WriteLine($"[{affected.Severity}] {affected.Ref.FullId} ({affected.Name})");
    Console.WriteLine($"  Reason: {affected.Reason}");
    Console.WriteLine($"  Runtime areas: {string.Join(", ", affected.RuntimeAreas)}");

    foreach (var path in affected.Paths)
    {
        Console.WriteLine($"  Path from {path.SourceChange.FullId}:");
        foreach (var seg in path.Segments)
            Console.WriteLine($"    {seg.From.FullId} --[{seg.Kind}/{seg.Role}]--> {seg.To.FullId} ({seg.Strength}, runtime={seg.IsRuntimeBinding})");
    }
}

// Check diagnostics
foreach (var diag in report.Diagnostics)
    Console.WriteLine($"[{diag.Severity}] {diag.Code}: {diag.Message}");
```

### 8.4 Filter Impact with Options

```csharp
// Conservative: include everything (default)
var fullReport = analyzer.Analyze(snapshot, changeSet);

// Exclude Weak edges
var strongOnly = analyzer.Analyze(snapshot, changeSet,
    new DescriptorImpactAnalysisOptions { IncludeWeakRelationships = false });

// Skip advisory edges (SupersededBy, unsupported SubWorkflow, etc.)
var noAdvisory = analyzer.Analyze(snapshot, changeSet,
    new DescriptorImpactAnalysisOptions { IncludeAdvisoryRelationships = false });

// Depth-limited (first-hop consumers only)
var shallowReport = analyzer.Analyze(snapshot, changeSet,
    new DescriptorImpactAnalysisOptions { MaxDepth = 1 });
```

### 8.5 Manual Change Set Construction

```csharp
// Build a change set without using the builder (for targeted analysis)
var changeSet = new DescriptorChangeSet
{
    Changes = new DescriptorChange[]
    {
        new() { Ref = new DescriptorRef("schema", "Order", 1), Kind = DescriptorChangeKind.Removed },
        new() { Ref = new DescriptorRef("capability", "ProcessOrder", 2), Kind = DescriptorChangeKind.Updated }
    }
};

var report = analyzer.Analyze(snapshot, changeSet);
```

### 8.6 Understanding Impact Severity

| Severity | When |
|---|---|
| None | Changed descriptor has zero consumers |
| Info | Added/Activated, or too deep to matter |
| Low | Weak advisory path, StateChanged via metadata |
| Medium | Updated/ContractHashChanged via weak path, Deprecated via advisory |
| High | Removed via Strong path, Updated via Strong runtime path |
| Critical | Removed via Strong runtime path (hard break) |

Severity is structural only — descriptor-kind-specific breaking compatibility rules are Phase 6d.

---

## 9. Phase 6d — Compatibility Analysis

Phase 6d adds `IDescriptorCompatibilityAnalyzer` to classify descriptor changes as Compatible, Risky, SecuritySensitive, Breaking, or Unsupported.

### 9.1 Quick Start

```csharp
var analyzer = services.GetRequiredService<IDescriptorCompatibilityAnalyzer>();

var beforeInventory = GetCurrentDescriptors();  // IReadOnlyList<IDescriptor>
var afterInventory = GetUpdatedDescriptors();   // IReadOnlyList<IDescriptor>

var changeSet = changeSetBuilder.Build(beforeInventory, afterInventory);
var topology = topologyBuilder.Build(beforeInventory);
var impactReport = impactAnalyzer.Analyze(topology, changeSet);

var report = analyzer.Analyze(beforeInventory, afterInventory, changeSet, impactReport);

Console.WriteLine($"Max Level: {report.MaxLevel}");
Console.WriteLine($"Requires Review: {report.RequiresReview}");
Console.WriteLine($"Has Breaking: {report.HasBreakingChanges}");
Console.WriteLine($"Has Security-Sensitive: {report.HasSecuritySensitiveChanges}");

foreach (var f in report.Findings)
{
    Console.WriteLine($"[{f.Level}] {f.RuleId}: {f.Message}");
    if (f.Path != null)
        Console.WriteLine($"  Path: {f.Path}");
    if (f.BeforeValue != null || f.AfterValue != null)
        Console.WriteLine($"  {f.BeforeValue} → {f.AfterValue}");
}
```

### 9.2 Interpreting the Report

| Report Property | Semantics |
|---|---|
| `MaxLevel` | Highest classified level (Compatible..Breaking). Only `Unsupported` if ALL findings are Unsupported. |
| `RequiresReview` | True if MaxLevel is Risky, SecuritySensitive, Breaking, or Unsupported. |
| `HasBreakingChanges` | True if any finding is Breaking. |
| `HasSecuritySensitiveChanges` | True if any finding is SecuritySensitive. |
| `HasUnsupportedFindings` | True if any finding is Unsupported. |

### 9.3 Unsupported ≠ Breaking

`Unsupported` means the analyzer lacks rule knowledge to classify the change — not that it's a known breaking change. Phase 6e can map `Unsupported` to mandatory human review. `MaxLevel` reports `Unsupported` only when every finding is Unsupported.

### 9.4 Impact Severity Is Not Compatibility

Phase 6c severity and Phase 6d compatibility are independent:
- High impact ≠ Breaking — an optional field addition may have broad impact but is perfectly compatible.
- Low impact ≠ Compatible — removing a required field may affect few consumers but is still breaking.

### 9.5 Options

```csharp
var options = new DescriptorCompatibilityAnalysisOptions
{
    TreatRemovedWithoutConsumersAsRisky = true,    // default: true
    TreatUnknownDescriptorKindAsUnsupported = true,  // default: true
    TreatImpactWarningsAsUnsupported = false,        // default: false
    IncludeCompatibleFindings = true                 // default: true
};

var report = analyzer.Analyze(before, after, changeSet, impactReport, options);
```

### 9.6 DI Registration

```csharp
services.AddDescriptorCompatibilityAnalysis();
```

---

## 10. Phase 6e — Lifecycle Governance

Phase 6e adds `IDescriptorLifecycleGovernanceService` — the governance gate that answers: can this descriptor lifecycle transition proceed?

### 10.1 Quick Start

```csharp
var governance = services.GetRequiredService<IDescriptorLifecycleGovernanceService>();

var request = new DescriptorLifecycleGovernanceRequest
{
    Transitions = new[]
    {
        new DescriptorLifecycleTransition
        {
            Subject = new DescriptorRef("schema", "Order", 1),
            Operation = DescriptorLifecycleOperation.Activate
        }
    },
    ValidationReport = validationReport,       // from pre-6
    BindingReport = bindingReport,              // from Phase 5h
    TopologyDiagnostics = snapshot.Diagnostics,  // from Phase 6b
    ImpactReport = impactReport,                // from Phase 6c
    CompatibilityReport = compatReport          // from Phase 6d
};

var report = governance.Evaluate(request);

Console.WriteLine($"Max decision: {report.MaxDecision}");
// Allowed, ReviewRequired, or Blocked

if (report.IsBlocked)
{
    foreach (var d in report.Decisions)
        foreach (var f in d.Findings.Where(f => f.Severity == DescriptorLifecycleFindingSeverity.Blocker))
            Console.WriteLine($"  BLOCKER [{f.Code}]: {f.Message}");
}
else if (report.RequiresReview)
{
    Console.WriteLine("Requires human review before proceeding.");
}
```

### 10.2 Understanding the Report

| Property | Semantics |
|---|---|
| `MaxDecision` | Worst decision across all transitions and package findings |
| `IsAllowed` | True if MaxDecision == Allowed |
| `RequiresReview` | True if MaxDecision == ReviewRequired |
| `IsBlocked` | True if MaxDecision == Blocked |
| `Decisions` | One per transition, each with its own findings list |
| `PackageFindings` | Issues not attributable to a single transition (change-set mismatch, binding inconsistencies) |

### 10.3 Decision Hierarchy

```
Blocked > ReviewRequired > Allowed
```

A single `Blocked` decision anywhere in the request produces `MaxDecision = Blocked`. Package-level `Review` findings upgrade `Allowed` to `ReviewRequired`. Package-level `Blocker` findings force `Blocked`.

### 10.4 Operations and Their Strictness

| Operation | Change-Driven? | Description | Default Strictness |
|---|---|---|---|
| `ValidateDraft` | No | Early authoring gate | Lenient |
| `SubmitForReview` | Yes | Can a human review this? | Medium |
| `Approve` | Yes | Approve for activation | Medium |
| `Activate` | Yes | Make runtime-active now | Strict (Blocked if Binding Unbound) |
| `Deprecate` | Yes | Mark deprecated | Medium |
| `Retire` | Yes | Remove/retire | Medium |
| `Reject` | No | Reject review request | Always Allowed |

### 10.5 Reading Findings

Each decision carries a list of findings with stable `Source` and `Code` values:

| Source | What It Checks |
|---|---|
| `validation` | ValidationReport issues |
| `binding` | Unbound descriptors, ID unresolvable, namespace/kind mismatch, version ambiguity |
| `topology` | Missing targets, strong cycles, orphans |
| `impact` | Affected consumers, impact severity, diagnostics |
| `compatibility` | Breaking/Risky/SecuritySensitive/Unsupported changes |
| `policy` | Change-set consistency, subjects in change set |

```csharp
foreach (var decision in report.Decisions)
{
    var bySource = decision.Findings.GroupBy(f => f.Source);
    foreach (var group in bySource)
        Console.WriteLine($"  {group.Key}: {group.Count()} findings");
}
```

### 10.6 Customizing with Options

```csharp
var options = new DescriptorLifecycleGovernanceOptions
{
    BlockActivateOnBreakingCompatibility = true,        // default: false
    BlockActivateOnUnboundBinding = true,               // default: true
    BlockActivateOnTopologyErrors = true,               // default: true
    TreatBreakingCompatibilityAsReviewRequired = true,   // default: true
    TreatSecuritySensitiveAsReviewRequired = true,       // default: true
    TreatRiskyCompatibilityAsReviewRequired = true,      // default: true
    TreatUnsupportedCompatibilityAsReviewRequired = true, // default: true
    TreatUnboundBindingAsBlocked = true,                 // default: true (for Activate)
    TreatPartialBindingAsAllowed = true                  // default: true
};

var request = new DescriptorLifecycleGovernanceRequest
{
    Transitions = transitions,
    Options = options,
    // ... reports
};
```

### 10.7 DI Registration

```csharp
services.AddDescriptorLifecycleGovernance();   // TryAddSingleton
```

---

## 11. Phase 6f — Descriptor Package / Manifest / Snapshot

### 11.1 Overview

Phase 6f builds deterministic, inspectable descriptor packages from explicit inventory and optional precomputed reports from Phases 6b-6e. The package is a metadata/evidence envelope — it does **not** contain descriptor payload, rerun analysis, or activate descriptors.

### 11.2 DI Registration

```csharp
services.AddDescriptorPackaging();
// Registers (TryAddSingleton):
//   IDescriptorPackageBuilder → DefaultDescriptorPackageBuilder
//   IDescriptorPackageDiffer   → DescriptorPackageDiffer
//   IDescriptorPackageSerializer → DescriptorPackageSerializer
```

### 11.3 Build a Package

```csharp
var builder = services.GetRequiredService<IDescriptorPackageBuilder>();

var package = builder.Build(new DescriptorPackageBuildRequest
{
    PackageId = "CrestCreates.CRM",
    PackageVersion = "1.0.0",
    Descriptors = myDescriptors,                    // IReadOnlyList<IDescriptor>
    TopologySnapshot = topologySnapshot,            // optional, from 6b
    ImpactReport = impactReport,                    // optional, from 6c
    CompatibilityReport = compatibilityReport,      // optional, from 6d
    GovernanceReport = governanceReport             // optional, from 6e
});

// Access package identity
Console.WriteLine(package.PackageId);       // "CrestCreates.CRM"
Console.WriteLine(package.ContentHash);     // deterministic SHA-256 hex (64 chars)
Console.WriteLine(package.Snapshot.SnapshotId); // "snapshot_" + ContentHash[..16]

// Access manifest entries
foreach (var entry in package.Manifest.DescriptorEntries)
{
    Console.WriteLine($"{entry.Ref.FullId} v{entry.Ref.Version} [{entry.State}]");
    Console.WriteLine($"  ContractHash: {entry.ContractHash}");
    Console.WriteLine($"  DefinitionHash: {entry.DefinitionHash}");
}

// Access evidence summary
Console.WriteLine($"Max impact severity: {package.Evidence.MaxImpactSeverity}");
Console.WriteLine($"Compatibility level: {package.Evidence.MaxCompatibilityLevel}");
Console.WriteLine($"Requires review: {package.Evidence.RequiresReview}");

// Access self-consistency diagnostics
foreach (var diag in package.Diagnostics)
{
    Console.WriteLine($"[{diag.Severity}] {diag.Code}: {diag.Message}");
}

// Access relationship facts
foreach (var rel in package.Snapshot.Relationships)
{
    Console.WriteLine($"{rel.From.FullId} → {rel.To.FullId} ({rel.Kind})");
    Console.WriteLine($"  SourcePath: {rel.SourcePath}");
}
```

### 11.4 Diff Two Packages

```csharp
var differ = services.GetRequiredService<IDescriptorPackageDiffer>();

var diff = differ.Diff(packageV1, packageV2);

Console.WriteLine($"Added: {diff.AddedRefs.Count}");
Console.WriteLine($"Removed: {diff.RemovedRefs.Count}");
Console.WriteLine($"Changed hashes: {diff.ChangedEntries.Count}");

foreach (var stateChange in diff.StateChanges)
{
    Console.WriteLine($"{stateChange.Ref.Id}: {stateChange.FromState} → {stateChange.ToState}");
}

foreach (var metaChange in diff.MetadataChanges)
{
    Console.WriteLine($"{metaChange.Field}: {metaChange.BeforeValue} → {metaChange.AfterValue}");
}
```

### 11.5 Serialize / Deserialize

```csharp
var serializer = services.GetRequiredService<IDescriptorPackageSerializer>();

var json = serializer.Serialize(package);
var restored = serializer.Deserialize(json);

Console.WriteLine(restored.PackageId == package.PackageId); // true
Console.WriteLine(restored.ContentHash == package.ContentHash); // true
```

Note: serialization round-trips metadata/envelope only (manifest, refs, evidence, diagnostics). Descriptor payload (`IDescriptor` objects) is not serialized.

### 11.6 Deterministic Hashing

```csharp
// Same inventory + same evidence → same ContentHash, regardless of input order
var pkg1 = builder.Build(request with { Descriptors = descriptors1 });
var pkg2 = builder.Build(request with { Descriptors = descriptors2 });
// descriptors1 and descriptors2 contain same descriptors in different order

pkg1.ContentHash == pkg2.ContentHash;           // true
pkg1.Snapshot.SnapshotId == pkg2.Snapshot.SnapshotId; // true

// Different CreatedAt → different EnvelopeHash, same ContentHash
pkg1.ContentHash == pkg2.ContentHash;           // true
pkg1.Manifest.EnvelopeHash == pkg2.Manifest.EnvelopeHash; // false
```

### 11.7 Key Types Reference

| Type | Namespace | Purpose |
|------|-----------|---------|
| `DescriptorPackage` | `CrestCreates.Metadata.Abstractions` | Package envelope |
| `DescriptorManifest` | `CrestCreates.Metadata.Abstractions` | Deterministic manifest |
| `DescriptorManifestEntry` | `CrestCreates.Metadata.Abstractions` | Entry with Ref/Kind/State/hashes |
| `DescriptorSnapshot` | `CrestCreates.Metadata.Abstractions` | Deterministic snapshot |
| `DescriptorPackageEvidence` | `CrestCreates.Metadata.Abstractions` | Aggregated evidence |
| `DescriptorPackageRelationshipEntry` | `CrestCreates.Metadata.Abstractions` | Relationship fact |
| `DescriptorPackageDiagnostic` | `CrestCreates.Metadata.Abstractions` | Self-consistency diagnostic |
| `IDescriptorPackageBuilder` | `CrestCreates.Metadata.Abstractions` | Builder interface |
| `IDescriptorPackageDiffer` | `CrestCreates.Metadata.Abstractions` | Differ interface |
| `IDescriptorPackageSerializer` | `CrestCreates.Metadata.Abstractions` | Serializer interface |
