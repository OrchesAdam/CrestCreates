# Descriptor Architecture Summary

> **Date:** 2026-06-12 | **Status:** Complete | **Phase 6a: Relationship Coverage + Phase 6b: Topology Read Model**

---

## 1. Design Goals

Phase 6a closes the descriptor relationship coverage gap: every descriptor that owns outgoing descriptor references must expose those references through **one uniform extraction path**.

The target question:

```
Given descriptor X, what other descriptors does X depend on / consume / produce / reference?
```

### Design Principles

1. **Single main path** — `IDescriptorRelationshipExtractor` per concrete descriptor type; no fallback, no dual-track
2. **Descriptors stay POCOs** — all relationship logic lives in extractors; descriptors are pure data containers
3. **AoT-friendly** — no runtime member scanning, no assembly scanning, no `dynamic`; provider uses `Type.IsInstanceOfType` dispatch
4. **Deterministic & testable** — each extractor produces the same output for the same input

---

## 2. Project Structure

All relationship types live in `CrestCreates.Metadata.Abstractions`. Extractors live in their respective domain modules.

```
framework/src/CrestCreates.Metadata.Abstractions/   # Core interfaces & types
  DescriptorRelationship.cs                          # Enhanced record (Role, SourcePath, Strength, IsRuntimeBinding)
  RelationshipKind.cs                                # Extended enum (+Uses, +Triggers)
  RelationshipStrength.cs                            # Strong / Weak
  IDescriptorRelationshipExtractor.cs                # Non-generic runtime interface
  DescriptorRelationshipExtractorBase.cs             # Optional typed base class (AoT-safe)
  IDescriptorRelationshipProvider.cs                 # Consumer-facing aggregation API

framework/src/CrestCreates.Metadata/
  DefaultDescriptorRelationshipProvider.cs           # IsInstanceOfType dispatch, IEnumerable<IDescriptorRelationshipExtractor>
  SchemaRelationshipExtractor.cs                     # Schema.References[] → SchemaDescriptor

framework/src/CrestCreates.Form/
  FormRelationshipExtractor.cs                       # Form.Schema → SchemaDescriptor

framework/src/CrestCreates.Capability/
  CapabilityRelationshipExtractor.cs                 # InputSchema/OutputSchema/Produces/Consumes/SupersededById

framework/src/CrestCreates.Event/
  EventRelationshipExtractor.cs                      # GeneratedEventDescriptor.PayloadSchemaRef → SchemaDescriptor

framework/src/CrestCreates.HumanTask/
  HumanTaskRelationshipExtractor.cs                  # Interaction/InputSchema/OutputSchema/Outcomes

framework/src/CrestCreates.Workflow/
  WorkflowRelationshipExtractor.cs                   # VariableSchema/CapabilityTarget/HumanTaskTarget/SubWorkflowTarget

framework/test/CrestCreates.Metadata.Tests/          # 5 test files (core types, provider, Schema extractor, dispatch)
framework/test/CrestCreates.Form.Tests/              # 1 test file
framework/test/CrestCreates.Capability.Tests/        # 1 test file
framework/test/CrestCreates.Event.Tests/             # 1 test file
framework/test/CrestCreates.HumanTask.Tests/         # 1 test file
framework/test/CrestCreates.Workflow.Tests/          # 1 test file
```

---

## 3. Core Architecture

### 3.1 The Extraction Chain

```
IDescriptor
     │
     ▼
IDescriptorRelationshipProvider.GetRelationships(descriptor)
     │
     ▼
foreach extractor in _extractors:
  if extractor.DescriptorType.IsInstanceOfType(descriptor):
      return extractor.Extract(descriptor)
     │
     ▼
DescriptorRelationshipExtractorBase<T>.Extract(IDescriptor)
     │  is TDescriptor typed?  (AoT-safe `is` pattern, no dynamic)
     ├─ yes → protected abstract Extract(TDescriptor) → concrete extractor logic
     └─ no  → Array.Empty<DescriptorRelationship>()
```

### 3.2 Why Non-Generic Interface?

The extractor interface is **non-generic**:

```csharp
public interface IDescriptorRelationshipExtractor
{
    DescriptorKind SupportedKind { get; }
    Type DescriptorType { get; }
    IReadOnlyList<DescriptorRelationship> Extract(IDescriptor descriptor);
}
```

This avoids .NET generic variance limitations. A generic `IEnumerable<IDescriptorRelationshipExtractor<IDescriptor>>` cannot be populated from `IDescriptorRelationshipExtractor<SchemaDescriptor>` registrations because .NET generics are invariant. The non-generic interface with `Type DescriptorType` + `IsInstanceOfType` dispatch solves this cleanly and remains AoT-compatible.

### 3.3 DescriptorRelationship Record

```csharp
public sealed record DescriptorRelationship(
    DescriptorRef From,                              // Source descriptor identity (Namespace + Id + Version)
    DescriptorRef To,                                // Target descriptor identity
    RelationshipKind Kind,                           // Produces | Consumes | DependsOn | References | Uses | Triggers
    string? Role = null,                             // Semantic role: "InputSchema", "OutputSchema", "Interaction"
    string? SourcePath = null,                       // Property path on source: "InputSchema", "Steps"
    RelationshipStrength Strength = Strong,          // Strong (breaks if missing) | Weak (optional)
    bool IsRuntimeBinding = false);                  // true if this relationship requires runtime handler execution
```

### 3.4 Relationship Kind Mapping

| Kind | Semantic | Examples |
|------|----------|---------|
| `Produces` | "I create/emit this" | Capability → OutputSchema, Capability → Event |
| `Consumes` | "I read/ingest this" | Capability → InputSchema, HumanTask → InputSchema |
| `DependsOn` | "I am a successor of this" | Capability → SupersededBy capability |
| `References` | "I loosely point to this" | Schema → Schema field refs, Workflow → unsupported SubWorkflow |
| `Uses` | "I broadly consume this" | Form → Schema, Event → Schema, Workflow → Schema |
| `Triggers` | "I cause this to execute" | Workflow → Capability, HumanTask → Capability |

### 3.5 Strength Semantics

| Strength | When | Example |
|----------|------|---------|
| `Strong` | Missing relationship breaks core functionality | Form without Schema cannot render; Workflow step without Capability target cannot execute |
| `Weak` | Optional or informational | Capability producing an event is optional; SupersededBy is metadata |

### 3.6 Concrete Type Dispatch (Not Kind-Based)

Provider dispatches by **concrete descriptor type**, not `DescriptorKind`. This is critical because one `DescriptorKind` can have multiple concrete types:

| DescriptorKind | Concrete Types | Extractor |
|---|---|---|
| Event | `GeneratedEventDescriptor` | `EventRelationshipExtractor` handles it |
| Event | `EventDescriptor` | No extractor — gracefully returns empty |

If dispatch were Kind-based, both types would hit the same extractor, causing a cast failure or silent corruption. `IsInstanceOfType` ensures `EventDescriptor` returns empty (correct), while `GeneratedEventDescriptor` matches and extracts (correct).

---

## 4. Per-Descriptor Relationship Map

### Schema

| Field | → Target | Kind | Strength | IsRuntimeBinding |
|-------|----------|------|----------|-----------------|
| `References[]` | `SchemaDescriptor` | `References` | Weak | false |

### Form

| Field | → Target | Kind | Strength | IsRuntimeBinding |
|-------|----------|------|----------|-----------------|
| `Schema` | `SchemaDescriptor` | `Uses` | Strong | false |

### Capability

| Field | → Target | Kind | Strength | IsRuntimeBinding |
|-------|----------|------|----------|-----------------|
| `InputSchema` | `SchemaDescriptor` | `Consumes` | Strong | false |
| `OutputSchema` | `SchemaDescriptor` | `Produces` | Strong | false |
| `Produces[]` | Event descriptor | `Produces` | Weak | false |
| `Consumes[]` | Event descriptor | `Consumes` | Weak | false |
| `SupersededById` | `CapabilityDescriptor` | `DependsOn` | Weak | false |

### Event (GeneratedEventDescriptor)

| Field | → Target | Kind | Strength | IsRuntimeBinding |
|-------|----------|------|----------|-----------------|
| `PayloadSchemaRef` | `SchemaDescriptor` | `Uses` | Strong | false |

### HumanTask

| Field | → Target | Kind | Strength | IsRuntimeBinding |
|-------|----------|------|----------|-----------------|
| `Interaction` | `FormDescriptor` | `Uses` | Strong | false |
| `InputSchema` | `SchemaDescriptor` | `Consumes` | Strong | false |
| `OutputSchema` | `SchemaDescriptor` | `Produces` | Strong | false |
| `Outcomes[].Capability` | `CapabilityDescriptor` | `Triggers` | Strong | **true** |

### Workflow

| Field | → Target | Kind | Strength | IsRuntimeBinding |
|-------|----------|------|----------|-----------------|
| `VariableSchema` | `SchemaDescriptor` | `Uses` | Strong | false |
| `CapabilityTarget` | `CapabilityDescriptor` | `Triggers` | Strong | **true** |
| `HumanTaskTarget` | `HumanTaskDescriptor` | `Triggers` | Strong | **true** |
| `SubWorkflowTarget` | `WorkflowDescriptor` | `References` | Weak | **false** |

---

## 5. DI Registration

```csharp
// Metadata module — core kernel
services.AddRelationshipKernel()
  → registers IDescriptorRelationshipProvider (TryAddSingleton)
  → registers SchemaRelationshipExtractor (AddSingleton<IDescriptorRelationshipExtractor>)

// Per-module — one-liner in each *ServiceCollectionExtensions:
// Form:            services.AddSingleton<IDescriptorRelationshipExtractor, FormRelationshipExtractor>();
// Capability:      services.AddSingleton<IDescriptorRelationshipExtractor, CapabilityRelationshipExtractor>();
// Event:           services.AddSingleton<IDescriptorRelationshipExtractor, EventRelationshipExtractor>();
// HumanTask:       services.AddSingleton<IDescriptorRelationshipExtractor, HumanTaskRelationshipExtractor>();
// Workflow:        services.AddSingleton<IDescriptorRelationshipExtractor, WorkflowRelationshipExtractor>();
```

---

## 6. Removed Types

| Type | Reason |
|------|--------|
| `IRelationshipAwareDescriptor` | Replaced by extractors. Descriptors stay POCOs. |
| `CapabilityDescriptor.GetRelationships()` | Logic moved to `CapabilityRelationshipExtractor`. Bug (schema namespace) fixed there. |
| `FormDescriptorDependencyExtractor` | Replaced by `FormRelationshipExtractor`. Moved to `./99_RecycleBin/`. |

`DependencyEdge`, `DescriptorDependencyKind`, `IDescriptorDependencyGraph`, `DescriptorDependencyGraph` are preserved as Phase 6b projection targets.

---

## 8. Phase 6b: Descriptor Topology Read Model

### 8.1 Overview

Phase 6b builds a **unique topology read model** on top of Phase 6a's relationship data layer. It projects flat `DescriptorRelationship` lists into a structured graph: nodes, edges, consumer index, and diagnostics — all computed at snapshot build time.

```
IDescriptorRelationshipProvider (Phase 6a)
         │
         ▼
IDescriptorTopologyBuilder.Build(descriptors)
         │
         ▼
DescriptorTopologySnapshot
  ├─ Nodes:  Dictionary<DescriptorRef, DescriptorNode>
  ├─ Edges:  List<DescriptorEdge>
  ├─ Diagnostics: DescriptorTopologyDiagnostics (5 rules)
  ├─ Consumer Index: 3-way version-aware segmentation
  └─ Query API: direct/transitive deps, consumers, lookups
```

### 8.2 Core Types

All types in `CrestCreates.Metadata.Abstractions.DescriptorTopology`:

| Type | Description |
|------|-------------|
| `DescriptorIdentity` | Version-independent key: `(Namespace, Id)` |
| `DescriptorNode` | Graph node: `Ref` + summary (`Kind`, `Name`, `State`, `ContractHash`) + edge index sets |
| `DescriptorEdge` | Directed edge: `From → To` = "From depends on To". Carries `Kind`, `Role`, `SourcePath`, `Strength`, `IsRuntimeBinding` |
| `DescriptorTopologySnapshot` | Frozen immutable graph with embedded diagnostics, consumer index, and query API |
| `DescriptorTopologyDiagnostics` | Collection of `DescriptorTopologyDiagnostic` with `Error`/`Warning` filtering |
| `DiagnosticSeverity` | `Error`, `Warning`, `Info` |
| `RelationshipRoles` | Canonical role constants (InputSchema, OutputSchema, SubWorkflowStep, etc.) |

### 8.3 Query API (on DescriptorTopologySnapshot)

| Method | Direction | Semantics |
|--------|-----------|-----------|
| `GetDirectDependencies(of)` | Outgoing | Descriptors `of` depends on |
| `GetDirectDependents(of)` | Incoming | Descriptors that depend on `of` |
| `GetTransitiveDependencies(of, includeWeak)` | BFS outgoing | All downstream (Strong-only default) |
| `GetTransitiveDependents(of, includeWeak)` | BFS incoming | All upstream (Strong-only default) |
| `GetConsumers(ns, id, version?)` | Consumer index | Version-aware: null→all, exact→exact+unpinned |
| `Contains(r)` / `FindNode(r)` | Lookup | Version-aware resolution |

### 8.4 Version-Aware Resolution

`TryResolveRef(DescriptorRef)` — exact match first, then `(Namespace, Id)` fallback for `Version=null` refs. Applied consistently in builder, snapshot queries, BFS, and adapter edge matching. This ensures unpinned relationships (e.g., SupersededBy, unversioned EventRef) correctly resolve to versioned target nodes.

### 8.5 Diagnostics (5 Rules)

| Code | Severity | Rule |
|------|----------|------|
| `MISSING_TARGET` | Error (Strong) / Warning (Weak) | Edge.To not found in nodes |
| `STRONG_CYCLE` | Error | DFS back-edge on Strong edges (both From/To must exist) |
| `ORPHAN` | Warning | Node with zero incoming edges, State ∉ {Draft, Removed} |
| `EXACT_DUPLICATE` | Warning | Full semantic key (7 fields) appears ≥2 times |
| `UNSUPPORTED_REFERENCE` | Warning | Edge matches explicit `(Role, Kind)` whitelist; NOT Weak inference |

### 8.6 Consumer Index (3-Way Segmentation)

```
_consumersByIdentity         — all consumers, regardless of version
_consumersByExactVersion     — only edges where To.Version != null
_consumersByUnpinnedVersion  — only edges where To.Version == null
```

Query: `version == null` → all; `version != null` → exact + unpinned (null-as-any).

### 8.7 DI Registration

```csharp
services.AddTopologyKernel()  // registers IDescriptorTopologyBuilder (TryAddSingleton)
```

### 8.8 Backward Compat

`DescriptorDependencyGraphAdapter` wraps `IDescriptorTopologyBuilder` → `IDescriptorDependencyGraph` for `DescriptorCatalog` backward compat. Uses bare-Id semantics. `AddEdge()` throws. `AnalyzeImpact` is direct-only (matches old one-hop behavior). All 6 `RelationshipKind→DescriptorDependencyKind` mappings covered.

### 8.9 Removed

- `DescriptorDependencyGraph` → `99_RecycleBin/`
- `DependencyGraphProvider` → `99_RecycleBin/`

Preserved (no `[Obsolete]`): `DependencyEdge`, `DescriptorDependencyKind`, `ImpactReport`.

### 8.10 Project Structure (Phase 6b additions)

```
framework/src/CrestCreates.Metadata.Abstractions/
  DescriptorTopology/
    DescriptorIdentity.cs
    DescriptorNode.cs
    DescriptorEdge.cs
    DescriptorTopologySnapshot.cs
    DescriptorTopologyDiagnostics.cs
    DescriptorTopologyDiagnostic.cs
    DiagnosticSeverity.cs
    RelationshipRoles.cs
  IDescriptorTopologyBuilder.cs

framework/src/CrestCreates.Metadata/
  DescriptorTopologyBuilder.cs              (internal)
  DescriptorDependencyGraphAdapter.cs

framework/test/CrestCreates.Metadata.Tests/
  DescriptorTopology/
    DescriptorNodeTests.cs
    DescriptorTopologySnapshotTests.cs
    DescriptorTopologyBuilderTests.cs
    DescriptorTopologyDiagnosticsTests.cs
    DescriptorDependencyGraphAdapterTests.cs
```

---

## 7. Explicit Non-Goals (Phase 6a)

- Topology graph / transitive analysis → Phase 6b
- Impact analysis beyond existing `AnalyzeImpact()` → Phase 6b
- `GetAllRelationships()` (registry enumeration) → Phase 6b
- `RelationshipKind → DescriptorDependencyKind` projection → Phase 6b
- Exposure descriptor coverage → Phase 8
- Runtime execution changes of any kind
- Dynamic / reflection-based extraction
