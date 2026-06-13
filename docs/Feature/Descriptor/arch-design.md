# Descriptor Architecture Summary

> **Date:** 2026-06-13 | **Status:** Complete | **Phase 6a: Relationship Coverage + Phase 6b: Topology Read Model + Phase 6c: Impact Analysis Engine**

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

## 9. Phase 6c: Impact Analysis Engine

### 9.1 Overview

Phase 6c builds a structural impact analysis engine on top of Phase 6b's topology snapshot. It answers: "Given descriptor changes, which descriptors are affected, through which relationship paths, and what runtime areas may need attention?"

```
DescriptorTopologySnapshot (Phase 6b)
        +
DescriptorChangeSet (changed descriptors)
        ↓
IDescriptorImpactAnalyzer.Analyze(topology, changeSet, options)
        ↓
DescriptorImpactAnalysisReport
  ├─ AffectedDescriptors (deduped, with impact paths)
  ├─ Paths (all version branches preserved)
  ├─ MaxSeverity
  └─ Diagnostics (IMPACT_TOPOLOGY_* + IMPACT_*)
```

### 9.2 Core Types

All types in `CrestCreates.Metadata.Abstractions.DescriptorImpact`:

| Type | Description |
|------|-------------|
| `DescriptorChangeKind` | Enum: Added, Updated, Deprecated, Removed, Activated, StateChanged, ContractHashChanged |
| `DescriptorImpactSeverity` | Enum: None, Info, Low, Medium, High, Critical |
| `DescriptorImpactRuntimeArea` | Enum: Metadata, Schema, Form, Capability, Event, Workflow, HumanTask, RuntimeBinding |
| `DescriptorChange` | Record: Ref + Kind + BeforeState/AfterState + BeforeContractHash/AfterContractHash |
| `DescriptorChangeSet` | Record: list of `DescriptorChange` |
| `DescriptorImpactPathSegment` | Record: From, To, Kind, Strength, IsRuntimeBinding, Role, SourcePath |
| `DescriptorImpactPath` | Record: SourceChange, Affected, Segments |
| `AffectedDescriptor` | Record: Ref, Kind, Name, Severity, RuntimeAreas, Paths, Reason, SuggestedAction |
| `DescriptorImpactDiagnostic` | Record: Severity, Code, Message, Subject, RelatedRefs |
| `DescriptorImpactAnalysisReport` | Record: ChangeSet, AffectedDescriptors, Paths, MaxSeverity, Diagnostics |
| `DescriptorImpactAnalysisOptions` | Record: IncludeWeakRelationships, IncludeAdvisoryRelationships, MaxDepth |
| `IDescriptorImpactAnalyzer` | Interface: `Analyze(topology, changeSet, options?)` → report |
| `IDescriptorChangeSetBuilder` | Interface: `Build(before, after)` → change set |

### 9.3 Analyzer Internals

The analyzer builds three internal indices at `Analyze()` start from `topology.Nodes` + `topology.Edges`:

1. **`_exactIndex`**: `DescriptorRef` → `DescriptorNode` (exact version match)
2. **`_identityIndex`**: `DescriptorIdentity` → `List<DescriptorNode>` (all versions of same identity, deterministically sorted)
3. **`_impactIncomingIndex`**: `DescriptorRef` → `List<DescriptorEdge>` — fan-out-aware incoming edge index

The `_impactIncomingIndex` is critical: it does NOT use `DescriptorNode.IncomingEdgeIndices` (which uses `FirstOrDefault` for unpinned edges). Instead, it iterates all `topology.Edges` and fans out unpinned edges (`Version == null`) to ALL matching versioned target nodes. This ensures deterministic multi-version impact analysis.

### 9.4 Severity Model

Severity is **structural only** — descriptor-kind-specific compatibility rules belong to Phase 6d.

| Change Kind | Strong Runtime | Strong Descriptor | Weak |
|---|---|---|---|
| Removed | Critical | High | Medium |
| Deprecated | High | Medium | Low |
| Updated / ContractHashChanged | High | Medium | Low |
| StateChanged | Medium | Low | Info |
| Activated / Added | Info | Info | Info |

**Modifiers (applied in order):**
1. **Transitive attenuation**: depth ≥ 2 → reduce one level
2. **RuntimeBoost**: terminal segment `IsRuntimeBinding == true` AND base NOT already from Strong Runtime column → +1 level, cap High
3. **RuntimeBinding area**: any segment in path with `IsRuntimeBinding == true` → add `RuntimeBinding` to `RuntimeAreas`

**Advisory edges** (Weak References/DependsOn/SupersededBy/SubWorkflowStep, non-runtime) are skippable via `IncludeAdvisoryRelationships=false`.

### 9.5 Traversal Algorithm

BFS upstream (incoming edges = consumers/dependents). Visited key: `(originChangedRef, currentNodeRef, edgeIndex)` — prevents false merge across version branches and infinite loops in cycles.

Unpinned `edge.From` (consumer) resolution:
- Exact match → normal traversal
- `Version == null`, one matching node → normal traversal
- `Version == null`, multiple matching nodes → **fan-out**: separate branch per versioned consumer, emit `IMPACT_AMBIGUOUS_UNPINNED_TARGET`
- Zero matching → emit `IMPACT_UNRESOLVED_CONSUMER`, path stops

### 9.6 Diagnostics

| Code | Severity | Source |
|---|---|---|
| `IMPACT_TOPOLOGY_MISSING_TARGET` | Error/Warning | Re-mapped from `MISSING_TARGET` if on path |
| `IMPACT_TOPOLOGY_STRONG_CYCLE` | Error | Re-mapped from `STRONG_CYCLE` if on path |
| `IMPACT_TOPOLOGY_UNSUPPORTED_REFERENCE` | Warning | Re-mapped from `UNSUPPORTED_REFERENCE` if on path |
| `IMPACT_AMBIGUOUS_UNPINNED_TARGET` | Warning | Consumer resolves to 2+ versions |
| `IMPACT_UNRESOLVED_CONSUMER` | Warning | Consumer resolves to 0 nodes |
| `IMPACT_PATH_TRUNCATED` | Warning | BFS stopped at MaxDepth |
| `IMPACT_SKIPPED_WEAK_PATH` | Info | Advisory edge excluded by option |

### 9.7 ChangeSetBuilder

`IDescriptorChangeSetBuilder.Build(before, after)` diffs two `IReadOnlyList<IDescriptor>` inventories:

- Not in before → Added
- Not in after → Removed
- State transition → Removed (to Removed), Deprecated (to Deprecated), Activated (Draft→Active), StateChanged (other)
- Same State, different ContractHash → ContractHashChanged
- Same State, same ContractHash, different Name → Updated

Changes are deduplicated by priority: Removed > Deprecated > StateChanged > ContractHashChanged > Updated > Added > Activated.

### 9.8 DI Registration

```csharp
services.AddDescriptorImpactAnalysis()
  → TryAddSingleton<IDescriptorImpactAnalyzer, DescriptorImpactAnalyzer>()
  → TryAddSingleton<IDescriptorChangeSetBuilder, DescriptorChangeSetBuilder>()
```

Both singletons (stateless pure functions). No scoped dependencies.

### 9.9 Boundary Rules

- Does NOT use legacy `DescriptorCatalog.AnalyzeImpact()` or `IDescriptorDependencyGraph`
- Does NOT use `DescriptorNode.IncomingEdgeIndices` (not fan-out-safe)
- Does NOT re-parse descriptor internals or introduce new relationship extraction
- Severity is structural only — Phase 6d handles descriptor-kind-specific compatibility
- Preserves: `DependencyEdge`, `DescriptorDependencyKind`, `ImpactReport` (still used by adapter)

### 9.10 Project Structure (Phase 6c additions)

```
framework/src/CrestCreates.Metadata.Abstractions/
  DescriptorImpact/
    DescriptorChangeKind.cs
    DescriptorImpactSeverity.cs
    DescriptorImpactRuntimeArea.cs
    DescriptorChange.cs
    DescriptorChangeSet.cs
    DescriptorImpactPathSegment.cs
    DescriptorImpactPath.cs
    AffectedDescriptor.cs
    DescriptorImpactDiagnostic.cs
    DescriptorImpactAnalysisReport.cs
    DescriptorImpactAnalysisOptions.cs
    IDescriptorImpactAnalyzer.cs
    IDescriptorChangeSetBuilder.cs

framework/src/CrestCreates.Metadata/
  DescriptorImpactAnalyzer.cs
  DescriptorChangeSetBuilder.cs

framework/test/CrestCreates.Metadata.Tests/
  DescriptorImpact/
    DescriptorChangeSetBuilderTests.cs
    DescriptorImpactAnalyzerTests.cs
    DescriptorImpactSeverityTests.cs
```

### 9.11 Non-Goals (Phase 6c)

- Descriptor-kind-specific breaking-change rules → Phase 6d
- Lifecycle governance → Phase 6e
- Package/manifest persistence → Phase 6f
- Runtime instance lookup, LLM, UI/API

---

## 7. Explicit Non-Goals (Phase 6a)

- Topology graph / transitive analysis → Phase 6b
- Impact analysis beyond existing `AnalyzeImpact()` → Phase 6b
- `GetAllRelationships()` (registry enumeration) → Phase 6b
- `RelationshipKind → DescriptorDependencyKind` projection → Phase 6b
- Exposure descriptor coverage → Phase 8
- Runtime execution changes of any kind
- Dynamic / reflection-based extraction

---

## 10. Phase 6d — Compatibility / Breaking Change Analyzer

### 10.1 Position

Phase 6d sits on top of Phase 6c (Impact Analysis). It consumes before/after descriptor inventories, `DescriptorChangeSet`, and `DescriptorImpactAnalysisReport` to produce a rule-based `DescriptorCompatibilityReport`. It does not rebuild topology or redo impact traversal.

### 10.2 Core Types

| Type | Purpose |
|---:|---|
| `DescriptorCompatibilityLevel` | Compatible(1)/Risky(2)/SecuritySensitive(3)/Breaking(4)/Unsupported(0). Unsupported=0 ensures MaxLevel excludes it. |
| `DescriptorCompatibilityFinding` | Per-change finding with Level, RuleId, Message, AffectedRefs, Path, BeforeValue, AfterValue |
| `DescriptorCompatibilityReport` | Aggregate with Findings, MaxLevel, Diagnostics, plus HasBreakingChanges/HasSecuritySensitiveChanges/RequiresReview |
| `IDescriptorCompatibilityAnalyzer` | `.Analyze(before, after, changeSet, impactReport, options?)` — stateless singleton |
| `IDescriptorCompatibilityRule` | Public interface for future module-owned rules. Methods: CanAnalyze, Analyze |

### 10.3 Rule Architecture

- **Generic rules** cover all 7 `DescriptorChangeKind` values without inspecting descriptor internals. Uses only Phase 6c affected consumers for severity decisions (e.g., Removed → Breaking if affected consumers, Risky otherwise).
- **Descriptor-specific rules** fire on `ContractHashChanged`/`Updated` and compare before/after descriptor internals (fields, schemas, permissions, steps, outcomes).
- Rules dispatch: specific rules first, generic rule as catch-all. Dedup by (Subject, RuleId, Path, Level).

### 10.4 Descriptor-Specific Coverage

| Descriptor Kind | Rule ID | Coverage |
|---|---:|---|
| Schema | SchemaCompatibilityRule | Field add/remove/type/reference, IsRequired, IsNullable, MaxLength/MinLength, MaxValue/MinValue, Pattern, Collection, References, DeclaredBreaking |
| Form | FormCompatibilityRule | Schema ref, field add/remove, IsRequiredOverride, IsReadOnly, ControlType, OptionsSource, presentation-only |
| Capability | CapabilityCompatibilityRule | Input/Output schema, permissions (SecuritySensitive), risk level (SecuritySensitive), capability kind, semantic tags |
| Event | EventCompatibilityRule | Payload schema (both EventDescriptor and GeneratedEventDescriptor), importance, scope, reliability, operational flags, DeclaredBreaking |
| HumanTask | HumanTaskCompatibilityRule | Interaction ref, schema refs, assignee strategy, permissions (SecuritySensitive), outcomes (add/remove/capability change), timeout |
| Workflow | WorkflowCompatibilityRule | Variable schema, steps (add/remove/target/transitions), OnError, mappings, variable scope |

### 10.5 Key Boundaries

- **6c severity is never projected into 6d compatibility.** High impact ≠ Breaking; Low impact ≠ Compatible.
- **Unsupported means insufficient rule knowledge**, not "more severe than Breaking." Phase 6e may map it to mandatory review.
- **No data-permission comparisons** — no descriptor owns data-permission scope rules today.
- **No topology access** — compatibility rules consume Phase 6c's impact report, not the topology snapshot.
- **DI**: `AddDescriptorCompatibilityAnalysis()` (TryAddSingleton).
- **Impact diagnostics** mapped to compatibility diagnostics: topology errors → `COMPAT_BLOCKED_BY_TOPOLOGY_ERROR`, path truncation → `COMPAT_ANALYSIS_INCOMPLETE`, unpinned ambiguity → `COMPAT_VERSION_AMBIGUITY`.
