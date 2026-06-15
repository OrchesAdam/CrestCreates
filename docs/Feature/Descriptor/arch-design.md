# Descriptor Architecture Summary

> **Date:** 2026-06-15 | **Status:** Complete | **Phase 6a: Relationship Coverage + Phase 6b: Topology Read Model + Phase 6c: Impact Analysis Engine + Phase 6d: Compatibility Analyzer + Phase 6e: Lifecycle Governance + Phase 6f: Package / Manifest / Snapshot**

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
- Package/manifest persistence → Phase 6f ✅ (completed)
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

---

## 11. Phase 6e — Descriptor Lifecycle Governance

### 11.1 Overview

Phase 6e answers: **given current analysis reports and a requested descriptor lifecycle transition, can this transition proceed?**

It is a **stateless, deterministic governance gate** — pure function over `(request) → report`. It consumes (does not recompute) all prior analysis reports.

```
ValidationReport + RuntimeBindingReport + TopologyDiagnostics
       + ImpactAnalysisReport + CompatibilityReport
              ↓
IDescriptorLifecycleGovernanceService.Evaluate(request)
              ↓
GovernanceReport { MaxDecision: Allowed | ReviewRequired | Blocked }
```

### 11.2 Core Types

All types in `CrestCreates.Metadata.Abstractions.DescriptorLifecycle`:

| Type | Description |
|------|-------------|
| `DescriptorLifecycleOperation` | Enum: ValidateDraft, SubmitForReview, Approve, Activate, Deprecate, Retire, Reject |
| `DescriptorLifecycleDecisionKind` | Enum: Allowed, ReviewRequired, Blocked (Blocked > ReviewRequired > Allowed) |
| `DescriptorLifecycleFindingSeverity` | Enum: Info, Warning, Review, Blocker |
| `DescriptorLifecycleTransition` | Record: Subject (DescriptorRef) + Operation + optional FromState/ToState/Reason |
| `DescriptorLifecycleFinding` | Record: Severity, Code, Message, Subject, Source (validation/binding/topology/impact/compatibility/policy), RelatedRefs |
| `DescriptorLifecycleDecision` | Record: Transition + Decision + Findings (per-transition) |
| `DescriptorLifecycleGovernanceReport` | Record: Decisions, MaxDecision, PackageFindings; convenience: IsAllowed/RequiresReview/IsBlocked |
| `DescriptorLifecycleGovernanceRequest` | Record: Transitions + all 5 input reports + Options |
| `DescriptorLifecycleGovernanceOptions` | Record: BlockActivateOn* flags, Treat*AsReviewRequired flags |
| `IDescriptorLifecycleGovernanceService` | Interface: `Evaluate(request)` → report |

### 11.3 Operation → Policy Mapping

| Operation | Change-Driven? | Strictness | Key Default Behavior |
|---|---|---|---|
| ValidateDraft | No | Lenient | Binding unbound → ReviewRequired |
| SubmitForReview | Yes | Medium | Breaking compat → ReviewRequired |
| Approve | Yes | Medium | Breaking compat → ReviewRequired |
| Activate | Yes | Strict | Breaking compat → ReviewRequired (or Blocked if option enabled). Binding unbound → Blocked. |
| Deprecate | Yes | Medium | Affected consumers → ReviewRequired |
| Retire | Yes | Medium | Breaking compat → ReviewRequired |
| Reject | No | Lenient | Always Allowed (human gate reversal) |

### 11.4 Finding Severity → Decision Mapping

Per-transition findings drive the decision:

| Worst Finding Severity | Decision |
|---|---|
| No findings, or Info/Warning only | Allowed |
| Review | ReviewRequired |
| Blocker | Blocked |

Package-level findings (change-set mismatch, binding-report inconsistencies, subject-not-in-changeset) with `Review` or `Blocker` severity **upgrade `MaxDecision`**: Review → ReviewRequired (if currently Allowed), Blocker → Blocked.

### 11.5 Finding Sources

Findings carry a stable `Source` string for UI/CI grouping:

| Source | Origin Report | Example Findings |
|---|---|---|
| `validation` | ValidationReport | Validation errors/warnings |
| `binding` | RuntimeBindingReport | Unbound, version ambiguity, ID unresolvable, namespace/kind mismatch |
| `topology` | TopologyDiagnostics | MISSING_TARGET, STRONG_CYCLE |
| `impact` | ImpactAnalysisReport | Impact severity, affected consumers |
| `compatibility` | CompatibilityReport | Breaking, Risky, SecuritySensitive, Unsupported |
| `policy` | Governance logic | ChangeSet mismatch, subject not in change set |

### 11.6 Compatibility Cross-Contamination Prevention

Compatibility findings are filtered per-transition by `f.Subject == transition.Subject`. A Breaking finding for descriptor A does NOT affect descriptor B's transition. `DescriptorCompatibilityReport.MaxLevel` is never used as a fallback — only subject-matched findings contribute to a transition's decision.

### 11.7 Binding Report Validation

The governance service validates binding-report entries for consistency:
- **Unresolvable ID**: empty/null or non-parseable `Namespace.Id` format → `Review`
- **Namespace/kind mismatch**: DescriptorId namespace must match the canonical namespace for its DescriptorKind (`schema`, `capability`, `event`, `workflow`, `form`, `humantask`) → `Review`
- **Multi-kind per ID**: same DescriptorId with different DescriptorKind values → `Review`
- **Version ambiguity**: same (Kind, ID) with different Status values → `Review`

### 11.8 DI Registration

```csharp
services.AddDescriptorLifecycleGovernance()   // TryAddSingleton
```

### 11.9 Boundary Rules

- **Consume, do not recompute** — no re-validation, re-binding, re-topology, re-impact, or re-compatibility
- **Classification only** — does not persist approvals, mutate descriptor state, or publish runtime changes
- **Stateless & deterministic** — pure function, no DI state, no runtime reflection
- **AoT-friendly** — records, enums, static dispatch only
- **Compatibility Unsupported ≠ more severe than Breaking** — it means "knowledge gap", not "harder block"

### 11.10 Project Structure

```
framework/src/CrestCreates.Metadata.Abstractions/
  DescriptorLifecycle/
    DescriptorLifecycleOperation.cs
    DescriptorLifecycleDecisionKind.cs
    DescriptorLifecycleFindingSeverity.cs
    DescriptorLifecycleTransition.cs
    DescriptorLifecycleFinding.cs
    DescriptorLifecycleDecision.cs
    DescriptorLifecycleGovernanceReport.cs
    DescriptorLifecycleGovernanceOptions.cs
    DescriptorLifecycleGovernanceRequest.cs
    IDescriptorLifecycleGovernanceService.cs

framework/src/CrestCreates.Metadata/
  DescriptorLifecycle/
    DefaultDescriptorLifecycleGovernanceService.cs

framework/test/CrestCreates.Metadata.Tests/
  DescriptorLifecycle/
    DescriptorLifecycleGovernanceServiceTests.cs    (48 tests)
```

### 11.11 Non-Goals

- Approval workflow engine
- Persistence of approval records
- Package/manifest persistence → Phase 6f ✅ (completed)
- UI / API / AppService
- CI gate integration

---

## 12. Phase 6f — Descriptor Package / Manifest / Snapshot

### 12.1 Overview

Phase 6f freezes what the descriptor control plane knows into a deterministic, inspectable, portable package/snapshot unit. It does **not** decide runtime activation, mutate registries, or re-execute prior phase analyzers.

```
Descriptor inventory
+ Descriptor hashes (ContractHash / DefinitionHash, informational)
+ Relationship facts (from 6b topology)
+ Evidence summaries (from 6b/6c/6d/6e reports)
+ Package self-consistency diagnostics
→ deterministic DescriptorPackage
```

### 12.2 Core Types

All types in `CrestCreates.Metadata.Abstractions` (evolved in-place) + new files:

| Type | Status | Description |
|------|--------|-------------|
| `DescriptorPackage` | UPGRADED | Envelope: Manifest + Snapshot + Evidence + Diagnostics; convenience passthroughs for PackageId/Version/ContentHash |
| `DescriptorManifest` | UPGRADED | Deterministic manifest: FormatVersion, identity metadata, flat `DescriptorEntries`, ContentHash/EvidenceHash/EnvelopeHash |
| `DescriptorManifestEntry` | UPGRADED | Identity: `DescriptorRef` (Namespace, Id, Version) + Kind/Name/State/ContractHash/DefinitionHash/SupersededById |
| `DescriptorSnapshot` | UPGRADED | Deterministic SnapshotId (first 16 chars of ContentHash, no Guid) + Descriptors + Relationships |
| `SnapshotEntry` | UPGRADED | Identity: `DescriptorRef` + contract/definition hashes + state |
| `DescriptorPackageRelationshipEntry` | NEW | Flattened relationship: From/To refs + Kind/Role/SourcePath/Strength/IsRuntimeBinding |
| `DescriptorPackageEvidence` | NEW | Aggregated evidence: topology counts + impact severity/counts + compatibility level/finding counts + lifecycle decision |
| `EvidenceFinding` | NEW | Normalized finding: Source/Code/Severity/Subject/Message/RelatedRefs |
| `EvidenceFindingCount` | NEW | Aggregated count by Severity+Code |
| `DescriptorPackageDiagnostic` | NEW | Self-consistency diagnostic: Code/Severity/Message/Subject |
| `DescriptorPackageDiagnosticCode` | NEW | 12 diagnostic code constants + 3 severity constants |

### 12.3 Builder API

```csharp
public interface IDescriptorPackageBuilder
{
    DescriptorPackage Build(DescriptorPackageBuildRequest request);
}

public sealed record DescriptorPackageBuildRequest
{
    public required string PackageId { get; init; }
    public required string PackageVersion { get; init; }
    public required IReadOnlyList<IDescriptor> Descriptors { get; init; }
    // Optional reports from 6b/6c/6d/6e:
    public DescriptorTopologySnapshot? TopologySnapshot { get; init; }
    public DescriptorImpactAnalysisReport? ImpactReport { get; init; }
    public DescriptorCompatibilityReport? CompatibilityReport { get; init; }
    public DescriptorLifecycleGovernanceReport? GovernanceReport { get; init; }
}
```

Builder is stateless singleton. Explicit inventory input — does NOT read from `IGlobalDescriptorRegistry`.

### 12.4 Hash Rules (AoT-Safe)

`DescriptorPackageHashComputer` uses deterministic string concatenation + SHA-256 — no `JsonSerializer.Serialize`, no anonymous objects, no runtime reflection.

| Hash | Contents |
|------|----------|
| `ContractHash` | From legacy `DescriptorHashComputer` (informational only) |
| `DefinitionHash` | From legacy `DescriptorHashComputer` (informational only) |
| `EvidenceHash` | Deterministic string concat of all evidence fields including diagnostic counts and normalized findings with RelatedRefs |
| `ContentHash` | `SHA256(FormatVersion + sorted refs + sorted relationships)` — snapshot identity only, no evidence dependence |
| `EnvelopeHash` | `SHA256(ContentHash + EvidenceHash + PackageId + PackageVersion + CreatedAt/CreatedBy/Source)` |

Key invariants:
- `CreatedAt` does NOT affect `ContentHash`.
- Different evidence → same `ContentHash`, different `EvidenceHash`, different `EnvelopeHash`.
- `SnapshotId` derives from `ContentHash[..16]` only.

### 12.5 Evidence Summary

`BuildEvidence()` populates from supplied reports without recomputation:

- **Topology**: NodeCount, EdgeCount, HasTopologyErrors, diagnostic counts
- **Impact**: MaxSeverity, AffectedDescriptorCount, ImpactPathCount, diagnostic counts
- **Compatibility**: MaxLevel, finding counts by level (Breaking / SecuritySensitive / Unsupported)
- **Lifecycle**: MaxDecision, RequiresReview, IsBlocked, PackageFindingCount
- **NormalizedFindings**: Unified `EvidenceFinding[]` from all 4 report types

### 12.6 Package Diagnostics (12 Codes)

| Code | Severity | Trigger |
|------|----------|---------|
| `PACKAGE_DUPLICATE_DESCRIPTOR_REF` | Error | Same (Namespace, Id, Version) appears twice |
| `PACKAGE_EVIDENCE_SUBJECT_OUTSIDE_INVENTORY` | Warning | Normalized finding subject not in package refs |
| `PACKAGE_TOPOLOGY_EDGE_OUTSIDE_PACKAGE` | Warning | Topology edge endpoint not in package |
| `PACKAGE_IMPACT_CHANGE_OUTSIDE_PACKAGE` | Warning | Impact change ref not in package |
| `PACKAGE_COMPATIBILITY_SUBJECT_OUTSIDE_PACKAGE` | Warning | Compatibility finding subject not in package |
| `PACKAGE_LIFECYCLE_TRANSITION_OUTSIDE_INVENTORY` | Warning | Lifecycle transition subject not in package |
| `PACKAGE_TOPOLOGY_NOT_PROVIDED` | Info | No topology snapshot supplied |

Plus: `PACKAGE_DESCRIPTOR_HASH_MISMATCH`, `PACKAGE_MANIFEST_REF_MISMATCH`, `PACKAGE_HASH_MISMATCH`, `PACKAGE_FORMAT_UNSUPPORTED` (defined but not all emitted by default builder).

### 12.7 Package Diff (Shallow)

```csharp
public interface IDescriptorPackageDiffer
{
    DescriptorPackageDiff Diff(DescriptorPackage before, DescriptorPackage after);
}
```

Output: `AddedRefs`, `RemovedRefs`, `ChangedEntries` (hash changes), `StateChanges`, `MetadataChanges` (strong-typed: `DescriptorPackageMetadataChange` with Field/BeforeValue/AfterValue).

Diff is shallow — no impact traversal, compatibility classification, or lifecycle governance.

### 12.8 Serializer

`IDescriptorPackageSerializer` — source-generated JSON via `CrestCreatesMetadataJsonContext` (AoT-safe). Round-trips metadata/envelope: manifest, snapshot refs, evidence, diagnostics. Does NOT serialize descriptor payload (`IDescriptor` objects).

### 12.9 DI Registration

```csharp
services.AddDescriptorPackaging();
// TryAddSingleton for: IDescriptorPackageBuilder, IDescriptorPackageDiffer, IDescriptorPackageSerializer
```

### 12.10 Project Structure (Phase 6f additions)

```
framework/src/CrestCreates.Metadata.Abstractions/    ← 14 new files (evidence/diagnostic/builder/diff/serializer types)
framework/src/CrestCreates.Metadata/                  ← 4 new files (builder, hash computer, differ, serializer)
framework/test/CrestCreates.Metadata.Tests/           ← 5 new test files (41 tests)
```

### 12.11 Boundary Rules

- 6f does NOT: activate descriptors, mutate registries, rerun 6b/6c/6d/6e analyzers, persist approvals, deploy runtime changes.
- 6f IS: evidence freezing, manifest/snapshot construction, deterministic identity, shallow diff.
- `ContentHash` = AoT-safe. `ContractHash`/`DefinitionHash` = informational, not used in package identity.
- `DescriptorSnapshotBuilder.TakeSnapshot()` is `[Obsolete]`. New main path: `IDescriptorPackageBuilder.Build(explicit inventory)`.
