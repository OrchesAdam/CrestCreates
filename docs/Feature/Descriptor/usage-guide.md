# Descriptor — Usage Guide

> This document is for CrestCreates module developers who need to consume or extend descriptor relationship extraction and topology queries.
> *Phase 6a (2026-06-12): Descriptor Relationship Coverage — 6 extractors, 1 provider, 434 tests*
> *Phase 6b (2026-06-12): Descriptor Topology Read Model — builder, snapshot, diagnostics, consumer index, 146 Metadata.Tests*
> *Phase 6c (2026-06-13): Impact Analysis Engine — analyzer, change set builder, severity model, 48 tests*

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

Severity is structural only — descriptor-kind-specific breaking compatibility rules are Phase 6d.`
