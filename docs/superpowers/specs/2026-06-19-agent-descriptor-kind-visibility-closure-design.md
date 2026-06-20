# Phase 7c Stabilization - Agent Descriptor Kind Visibility Closure Design

**Date**: 2026-06-19
**Status**: Approved design
**Issue**: [#40 - Phase 7c Stabilization - Agent Tool Authorization Policy Hardening](https://github.com/OrchesAdam/CrestCreates/issues/40)
**Depends on**: Phase 7c Agent Control Plane Tool Surface, Agent Tool Authorization Policy Hardening

## 1. Decision Summary

`DeniedDescriptorKinds` defines what descriptor kinds an Agent may observe, not merely what descriptor kinds an Agent may directly operate on.

`DeniedDescriptorKinds` 控制 Agent 可观察的元数据宇宙，不是只控制 Agent 可直接操作的单个资源。

It is an Agent Control Plane information-flow boundary, not only an authorization hint for operations that happen to carry one descriptor kind.

An Agent operating under a restrictive kind policy must observe a closed, visible descriptor universe:

```text
development visible kinds = valid descriptor kinds - denied kinds
production/hardened visible kinds = explicitly allowed kinds - denied kinds
visible universe = tenant-visible descriptors whose kind is in the effective visible-kind set
```

All direct reads, mutations, searches, topology projections, context packs, drafts, reviews, diagnostics, fix proposals, package previews, activation handoffs, counts, and nested artifacts must be authorized or projected against that universe.

The policy has the following externally observable semantics:

1. An explicit request targeting a denied kind is denied.
2. A single-resource request whose authoritative kind is denied is denied.
3. A broad query returns only visible results.
4. Counts, pagination, truncation flags, topology, and diagnostics are derived from visible results only.
5. Indirect resources inherit the kind of their owning draft or descriptor.
6. Nested descriptor-bearing artifacts are projected recursively with typed code.
7. Failure to establish the authoritative kind of a protected single resource fails closed.
8. Production and hardened policies expose only descriptor kinds in their effective allow set; development policies may use open-world visibility.

This closes the gap in which an Agent denied access to `Event` descriptors could still infer their existence, attributes, counts, or relationships through aggregate tools.

## 2. Context and Problem

Issue #40 hardens Phase 7c tool authorization so development can opt into permissive behavior while production requires explicit grants for mutating and activation handoff tools. `DeniedDescriptorKinds` was introduced as a deny-wins rule.

The first implementation applied the rule only when a tool supplied one `DescriptorKindConstraint`. That is sufficient for a direct operation such as creating an `Event` draft, but not for tools that operate over mixed-kind data:

- descriptor search can return denied descriptors;
- topology summaries can expose denied node and edge counts;
- context packs can traverse denied descriptors;
- draft, review, fix, preview, and activation lists can expose resources owned by denied kinds;
- nested package inventories and diagnostics can reintroduce data removed at the top level.

Documenting aggregate queries as exempt would give `DeniedDescriptorKinds` two incompatible meanings: "always denied" for direct tools and "still observable" for aggregate tools. That is not a stable security contract.

## 3. Considered Approaches

### 3.1 Document the Single-Resource Limitation

Keep aggregate tools unchanged and document that kind denies apply only when a request has one target kind.

**Rejected** because it preserves an information disclosure path, makes deny semantics depend on tool shape, and invites future tools to bypass the boundary by returning aggregates.

### 3.2 Deny All Aggregate Tools When Any Kind Is Denied

Reject every mixed-kind query whenever `DeniedDescriptorKinds` is non-empty.

**Rejected** because it is secure but unnecessarily destroys useful read access. An Agent denied `Event` should still be able to inspect allowed `Workflow` metadata.

### 3.3 Visibility Closure

Define one visible descriptor universe and require every tool to either target a visible resource or derive its output from that universe.

**Selected** because it provides consistent deny semantics, preserves useful access, strengthens the Control Plane as the unique governance boundary, and supports deterministic AoT-safe implementation.

## 4. Goals

- Give `DeniedDescriptorKinds` one consistent meaning across every manifest tool; the manifest currently contains 30 tools.
- Prevent disclosure of denied descriptor existence, attributes, relationships, derived artifacts, and counts.
- Preserve broad queries by security-trimming them before pagination and aggregation.
- Resolve each single target once and reuse the same authoritative snapshot for authorization and execution.
- Keep tool identity, actor, permission, mutation, and activation authorization ahead of resource access.
- Keep tenant boundaries authoritative during direct and indirect resolution.
- Use explicit strongly typed projection, without runtime reflection or object-graph scrubbing.
- Provide table-driven coverage that prevents new tools from silently omitting a visibility strategy.
- Keep production and hardened visibility closed-world while preserving an explicit development open-world mode.

## 5. Non-Goals

- This change does not add field-level authorization within a visible descriptor kind.
- It does not infer descriptor visibility from diagnostic message text, paths, or caller-supplied identifiers.
- It does not change permission names, tool categories, production mutation defaults, or activation approval governance.
- It does not make Agents an activation or runtime execution authority.
- It does not redesign descriptor storage, topology storage, draft persistence, or context-pack traversal APIs beyond the filtering hooks required for closure.
- It does not add a second public authorization framework or expose visibility policy from `CrestCreates.Agent.ControlPlane.Abstractions`.
- It does not use redaction strings as a substitute for removing denied typed data.

## 6. Security Semantics

### 6.1 Deny Wins

Tool, permission, actor, and descriptor-kind denies override explicit allows and mode defaults. Visibility filtering cannot turn a denied explicit target into an allowed empty result.

### 6.2 Explicit Target Versus Broad Query

An explicit target is a kind, descriptor reference, draft ID, review result ID, proposal ID, preview ID, activation request ID, or focus descriptor supplied by the caller.

- If the target resolves to a denied kind, return `Denied`.
- If a caller explicitly supplies a denied `DescriptorKind` filter, return `Denied`.
- If a query has no explicit kind or resource target, filter denied kinds and return the visible result.

This distinction prevents a caller from probing a known denied identifier while keeping general discovery useful.

### 6.3 Visible Result Semantics

Filtering happens before ordering, paging, `MaxResults`, `TotalCount`, `WasTruncated`, topology aggregation, or diagnostic generation. Returned metadata describes only the visible universe.

The caller must not receive the denied kind names, denied identifiers, hidden count, or a before-filter total. A `RESULTS_SECURITY_TRIMMED` diagnostic may be emitted only in a non-probing form: emit it consistently whenever the invocation has a restrictive kind scope, whether or not the current data contained a denied item. Otherwise the diagnostic itself becomes an existence oracle.

### 6.4 Unknown Kinds

- For a protected single target, an unavailable authoritative kind returns `Denied` with `AUTHORIZATION_CONTEXT_UNAVAILABLE`.
- For an aggregate source, inability to load or classify the complete source returns `Failed`; no partial unfiltered result is returned.
- Development policy is open-world: a valid future `DescriptorKind` may be visible by default unless explicitly denied.
- Production and hardened policies are closed-world: visibility requires membership in `AllowedDescriptorKinds`, producing an effective allow set after deny-wins evaluation. A valid newly introduced enum kind that is absent from that set is denied.
- Invalid wire values, unrecognized serialized values, and missing kinds on records that require a kind are invalid authorization context and fail closed in every mode.
- An `UnknownDescriptorKindVisibilityMode` flag alone is insufficient because it cannot distinguish a valid newly introduced enum member from a wire value the current process cannot parse. The effective allow set is the production-safe authority; a mode may only describe development defaults.

## 7. Component Design

All new implementation components remain internal to `CrestCreates.Agent.ControlPlane` unless a later adapter requirement proves a public contract is necessary.

### 7.1 Descriptor Kind Policy Evaluator

One internal evaluator owns kind normalization and deny matching. Both direct authorization and visibility scopes use it. The authorization service and projectors must not independently interpret `DeniedDescriptorKinds`.

```csharp
internal interface IAgentDescriptorKindPolicyEvaluator
{
    bool HasRestrictions { get; }
    AgentDescriptorKindDecision Evaluate(DescriptorKind kind);
}
```

Matching remains ordinal and uses the canonical `DescriptorKind` representation already used by the metadata contracts. The evaluator computes the effective allow set from policy mode, `AllowedDescriptorKinds`, and `DeniedDescriptorKinds`; deny always wins. It rejects invalid wire values before visibility evaluation and treats valid kinds absent from a production or hardened allow set as denied.

### 7.2 Agent Descriptor Visibility Scope

An immutable `AgentDescriptorVisibilityScope` is created once per invocation after coarse tool authorization. It captures the tenant and the effective kind policy snapshot.

```csharp
internal sealed class AgentDescriptorVisibilityScope
{
    public required string TenantId { get; init; }
    public bool IsRestricted { get; }

    public bool IsVisible(DescriptorKind kind);
    public AgentVisibilityDecision EvaluateExplicit(DescriptorKind kind);
    public IReadOnlyList<T> Filter<T>(
        IEnumerable<T> source,
        Func<T, DescriptorKind> kindSelector);
}
```

The scope performs policy decisions only. It does not query stores, use reflection, or own business execution. It captures whether policy semantics are development open-world or production/hardened closed-world and the evaluator's immutable effective allow set. This keeps storage resolution, policy evaluation, and artifact projection independently testable.

### 7.3 Resource Resolver and Snapshots

An internal resolver loads direct and indirect resources inside the invocation tenant and produces typed immutable snapshots:

- descriptor snapshot, resolved by namespace, ID, and requested version;
- draft snapshot;
- review plus owning draft snapshot;
- fix proposal plus owning draft snapshot;
- package preview plus owning draft snapshot;
- activation request plus owning draft snapshot.

Batch resolver methods are required for aggregate review, fix, preview, and activation data so filtering does not introduce N+1 store calls. Resolver results never cross tenants and never fall back to another tenant when an ID is absent locally.

### 7.4 Typed Projectors

Projectors enforce closure on outputs that can contain more than one descriptor or derived reference:

- `AgentDescriptorSearchVisibilityProjector`
- `AgentTopologyVisibilityProjector`
- `AgentContextPackVisibilityProjector`
- `AgentDraftArtifactVisibilityProjector`

The names may be consolidated during implementation where DTO ownership makes that clearer, but the responsibilities remain separate.

The topology projector removes denied nodes and every incident edge, then recomputes node counts, edge counts, and topology diagnostics. The context-pack projector or builder hook starts from visible focus and traversal sources; it must not build a full pack and attempt string-level cleanup afterward. The draft artifact projector handles review inventories, comparison descriptors, fix diagnostics, package contents, evidence, readiness data, and any other nested descriptor-bearing DTO.

Projectors are exhaustive over known DTO types. An unsupported nested descriptor-bearing type is omitted fail-closed and recorded through protected internal security telemetry.

### 7.5 Diagnostic Explanation Policy

`ExplainDiagnostics` uses a dedicated typed policy because caller-supplied diagnostic text is not authoritative metadata. The current request DTO has an optional `DraftId`; each diagnostic contains only `Code`, `Severity`, `Message`, `Path`, and `RelatedDiagnosticCode`.

- With `DraftId`, the resolver establishes the tenant-owned draft kind and applies normal explicit-target semantics.
- Without `DraftId`, only a kind-agnostic, code-table explanation may be produced: code meaning, severity meaning, and generic remediation.
- Generic explanation never echoes, interpolates, parses, or derives output from caller-provided `Message` or `Path`, and never repeats identifier-like fragments from those fields.
- `Code` and `RelatedDiagnosticCode` are treated only as keys into an allowlisted generic explanation table, not as descriptor associations or free-form response content. Unknown codes receive a fixed generic fallback rather than echoing the supplied code or diagnostic fields.

If a future diagnostic contract adds typed associations such as `DescriptorRef`, `DraftId`, `ProposalId`, `ReviewResultId`, `PackagePreviewId`, or `ActivationRequestId`, each association requires tenant-safe authoritative resolution. Explanations associated with a denied kind are omitted from a broad request or cause denial when the association is an explicit target. Unresolvable protected associations fail closed. Message or path parsing must never substitute for typed resolution.

## 8. Invocation Pipeline

The facade uses the following staged path:

```text
1. Validate expected tool name and invocation context
2. Resolve manifest descriptor
3. Coarse authorization: runtime prohibition, tool, actor, permission,
   mode, category, read/mutation, and activation-handoff policy
4. Create immutable AgentDescriptorVisibilityScope
5. Resolve explicit resource or aggregate source within TenantId
6. Apply explicit-target kind decision or aggregate visibility filter
7. Execute against the same resolved snapshot / visible source
8. Apply typed nested-artifact projection
9. Compute visible-only paging, counts, topology, and diagnostics
10. Record audit and return
```

The coarse authorization API must not represent "kind not resolved yet" by passing a null constraint into the existing fail-closed kind check. Tool authorization and descriptor visibility are separate stages with explicit types. This also guarantees denied actors, denied tools, forged tool names, and ungranted mutations perform zero store reads.

For a single-resource operation, step 5 resolves once. Step 7 must reuse that snapshot rather than loading the resource again. This prevents time-of-check/time-of-use differences where authorization sees one kind and execution acts on another revision or owner. During staged delivery, any descriptor-bearing tool not yet migrated to this pipeline fails closed; it must not continue through the nullable `DescriptorKindConstraint` path.

## 9. Operation Semantics

### 9.1 Direct Kind Operations

`CreateDescriptorDraft` and kind-filtered `SearchDescriptors` evaluate the request kind directly before accessing descriptor or draft stores. A denied explicit kind returns `Denied`.

### 9.2 Single Descriptor or Draft Operations

Descriptor references are version-aware. Draft operations use the authoritative tenant-owned draft kind. The resolved snapshot is used for both the decision and the business operation. A same-tenant target with an authoritative denied kind returns `Denied` in the internal Control Plane.

### 9.3 Aggregate Queries

Broad descriptor, draft, review, and fix queries execute over visible sources. Filtering precedes deterministic ordering and paging. Related artifacts inherit the owning draft's kind.

### 9.4 Graph and Context Operations

For relationship queries, the subject is an explicit target and must be visible. Neighbors of denied kinds and their connecting edges are omitted. Topology and context packs are built from the visible descriptor universe, not post-processed from an unrestricted serialized result.

If a context-pack request explicitly names a denied focus descriptor, the request is denied. Traversal from an allowed focus stops at denied nodes and does not expose their refs, edges, diagnostics, or counts.

### 9.5 Indirect Resources

Review results, fix proposals, package previews, readiness previews, and activation requests inherit visibility from their owning draft. Resolution follows stored typed IDs inside the same tenant. Caller-provided owner IDs are cross-checked against stored ownership where a request contains both.

### 9.6 Nested Artifacts

Top-level authorization is not enough when an allowed artifact embeds inventories, topology, descriptor refs, or diagnostics derived from other kinds. Typed projectors apply closure recursively. In particular:

- review proposed inventories contain visible descriptors only;
- package and evidence inventories contain visible descriptors only;
- topology nodes, edges, diagnostics, and counts are recomputed from visible nodes;
- diagnostic explanations do not echo hidden paths or refs;
- readiness blockers do not disclose denied artifacts;
- comparison output cannot embed a denied active descriptor.

### 9.7 Denied Versus Not Found

The internal Control Plane preserves `Denied` for a same-tenant explicit target whose authoritative kind was resolved and denied. This supports deterministic policy behavior, auditing, and tests.

A missing target or a target that cannot be resolved within the invocation tenant returns `NotFound` or `AUTHORIZATION_CONTEXT_UNAVAILABLE` according to the existing resource contract, without querying another tenant or proving cross-tenant existence. The response must not distinguish "missing" from "exists in another tenant."

Future external MCP or HTTP adapters may map internal `Denied` to `NotFound` as an anti-probing hardening mode. That mapping belongs at the adapter boundary, must be documented and applied consistently, and must preserve the internal denial classification in protected audit telemetry.

## 10. Complete Tool Coverage Matrix

The static manifest currently contains 30 tools. Every manifest entry must declare exactly one resource shape and visibility strategy in a table-driven test. Coverage is bidirectional: every manifest tool has one coverage entry, every coverage entry names an existing manifest tool, and duplicate names fail the test. The current count is informational, not a permanent acceptance gate.

| # | Tool | Resource shape | Visibility behavior |
|---:|---|---|---|
| 1 | `BuildMetadataContextPack` | Multi-focus descriptor graph | Deny any explicitly denied focus; build traversal and diagnostics from the visible universe. |
| 2 | `BuildRuntimeScenarioContextPack` | Multi-focus descriptor graph and traversal recipe | Deny any explicitly denied focus; stop traversal at denied nodes and build the pack from visible inputs. |
| 3 | `GetDescriptorByRef` | Single versioned descriptor | Resolve namespace, ID, and version once; deny a denied kind; return the same snapshot. |
| 4 | `SearchDescriptors` | Explicit-kind or broad descriptor aggregate | Deny a denied explicit kind; otherwise filter before ordering, bounding, `TotalCount`, and `WasTruncated`. |
| 5 | `ListDescriptorRelationships` | Single subject plus graph neighbors | Deny a denied subject; remove denied neighbors and all incident edges. |
| 6 | `GetTopologySummary` | Whole descriptor graph aggregate | Build from visible nodes and edges; recompute every count and diagnostic. |
| 7 | `CreateDescriptorDraft` | Direct requested kind | Deny before store access when the request kind is denied. |
| 8 | `UpdateDescriptorDraft` | Single draft | Resolve the tenant-owned draft once; deny its kind; update that snapshot/revision path. |
| 9 | `GetDescriptorDraft` | Single draft | Resolve once and deny its kind before returning it. |
| 10 | `ListDescriptorDrafts` | Draft aggregate | Filter by each draft's kind before ordering, paging, and `TotalCount`; deny a denied explicit kind filter if one is added. |
| 11 | `CancelDescriptorDraft` | Single draft mutation | Resolve once and deny its kind before state change. |
| 12 | `CompareDescriptorDraft` | Draft plus active descriptor | Deny the draft kind; resolve the versioned active descriptor in the same snapshot flow; project nested output. |
| 13 | `ValidateDescriptorDraft` | Single draft plus validation artifacts | Deny the draft kind; validate the resolved snapshot; project descriptor-bearing diagnostics. |
| 14 | `ReviewDescriptorDraft` | Single draft mutation plus review artifacts | Deny the draft kind before running or persisting review; project nested inventory and topology. |
| 15 | `GetDraftReviewResult` | Review result owned by a draft | Resolve review and owning draft in-tenant; deny the owner's kind; project nested artifacts. |
| 16 | `ListDraftReviewResults` | Optional explicit draft or broad review aggregate | With `DraftId`, treat as a single target; without it, batch-resolve owners and filter before returning. |
| 17 | `ExplainDiagnostics` | Optional draft target or caller-supplied diagnostics | With `DraftId`, resolve and deny by the tenant-owned draft. Without it, current unassociated diagnostics receive allowlisted code-table explanations only; never echo `Message`, `Path`, or identifier-like fragments. Future typed descriptor-bearing associations require authoritative tenant-safe resolution; denied associations are omitted for broad input or denied when explicitly targeted. |
| 18 | `SuggestDescriptorDraftFixes` | Single draft mutation plus proposals | Deny the draft kind before generating or persisting proposals; project proposal diagnostics. |
| 19 | `GetFixProposal` | Proposal owned by a draft | Resolve proposal and owner in-tenant; deny the owner's kind before returning. |
| 20 | `ListFixProposals` | Optional explicit draft or broad proposal aggregate | With `DraftId`, deny by that draft; without it, batch-resolve owners and filter results. |
| 21 | `ApplyFixProposalToDraft` | Proposal plus explicit draft mutation | Resolve both once, require stored owner match, deny the owner kind, then mutate only that snapshot path. |
| 22 | `PreviewDescriptorPackage` | Single draft mutation plus nested package | Deny by owning draft before persisting; construct and project package inventory from visible descriptors. |
| 23 | `BuildPackageEvidencePreview` | Single draft mutation plus package/evidence graph | Deny by owning draft before persisting; project nested package, evidence, refs, and diagnostics. |
| 24 | `BuildActivationReadinessPreview` | Single draft plus review/package dependencies | Deny by owning draft; resolve dependencies in-tenant and project blockers and diagnostics. |
| 25 | `GetPackagePreview` | Preview owned by a draft | Resolve preview and owner in-tenant; deny owner kind; project nested package content. |
| 26 | `SubmitActivationRequest` | Draft plus referenced review/package artifacts | Resolve draft and referenced artifacts once, verify common tenant and ownership, deny draft kind before handoff persistence. |
| 27 | `GetActivationRequestStatus` | Activation request owned by a draft | Resolve request and owner in-tenant; deny owner kind before returning status or nested data. |
| 28 | `CancelActivationRequest` | Activation request mutation owned by a draft | Resolve request and owner once; deny owner kind before state change. |
| 29 | `ListAgentTools` | Static manifest, no descriptor data | Kind visibility is not applicable; tool-level authorization and manifest semantics remain unchanged. |
| 30 | `GetAgentToolDescriptor` | Static manifest entry, no descriptor data | Kind visibility is not applicable; do not expose effective denied-kind configuration through the descriptor. |

Adding, removing, renaming, splitting, or combining a tool requires the manifest and coverage table to remain duplicate-free and set-equal before tests pass; no fixed-count assertion is used.

## 11. Errors, Cancellation, and Audit

### 11.1 Error Contract

- `DESC_KIND_DENIED`: an explicit target has an authoritative denied kind.
- `AUTHORIZATION_CONTEXT_UNAVAILABLE`: a protected single or indirect target exists but its authoritative kind/owner cannot be established safely.
- `NotFound`: the requested target is absent from the invocation tenant, without revealing whether it exists elsewhere.
- `RESULTS_SECURITY_TRIMMED`: optional generic diagnostic emitted consistently for restrictive scopes, never with kind names or hidden counts.
- Store or projector failures during aggregate closure return `Failed` and no value.

Not-found handling remains tenant-scoped. The resolver must not query outside the invocation tenant to distinguish missing from cross-tenant. Internal same-tenant policy denial remains `Denied`; future external adapters may consistently map it to `NotFound` while retaining the protected internal audit reason.

### 11.2 Cancellation

Every resolver, batch load, builder, projector, and auditor receives the invocation `CancellationToken`. `OperationCanceledException` is never swallowed by defensive lookup code. It propagates or maps through the facade's one established cancellation result; it must not become unknown-kind denial, ordinary failure, or an empty aggregate.

### 11.3 Audit and Telemetry

Invocation audit records include only resources visible to the caller in touched-resource fields. They do not record hidden refs or hidden counts in caller-retrievable diagnostics.

Protected internal security telemetry may record the policy rule, filtered count, tenant, actor, tool, and correlation ID for operations and incident response. Access to that telemetry is outside the Agent tool surface. It must not serialize descriptor payloads unnecessarily.

Denied coarse authorization is audited without resource access. Denied explicit targets are audited with the supplied opaque target ID where existing audit policy permits, but without resolved hidden metadata in the response.

## 12. Tenant Boundary

Visibility closure is evaluated inside `CurrentTenant` / `AgentToolInvocationContext.TenantId`, never instead of the tenant boundary.

- Every direct resolver key includes `TenantId`.
- Every batch join between review/fix/preview/activation records and drafts includes `TenantId`.
- An artifact whose owner is missing in the current tenant fails closed; it is not joined to an owner from another tenant.
- Counts and graph construction use the tenant-visible source before applying kind visibility.
- Caches include tenant and effective policy identity/version so a permissive result cannot be reused under a restrictive scope.
- Error behavior must not reveal whether the same ID exists in another tenant.

## 13. AoT and Dependency Constraints

- No runtime reflection, assembly scanning, `dynamic`, or serializer-based object graph cleanup.
- DTO projection uses explicit generated or handwritten strongly typed code.
- The implementation stays in `CrestCreates.Agent.ControlPlane`; abstractions remain free of implementation policy unless an external adapter later requires a stable public visibility contract.
- Runtime Agent projects do not gain dependencies on Framework API/Web or concrete persistence providers.
- Metadata/context/topology builders receive filtered typed inputs or typed predicates through existing dependency directions.
- New DTO variants and projectors must remain trim-safe and source-generator friendly.
- The solution's existing dependency-boundary tests remain authoritative.

## 14. Landing Sequence

The closure lands through three reviewable PRs or Issue #40 sub-issues. All three use the same policy evaluator, visibility scope, resolver contracts, and invocation pipeline; they must not introduce parallel interpretations of kind policy.

### PR A - Policy and Pipeline Closure

- Add the shared evaluator, development open-world and production/hardened closed-world policy semantics, effective allow set, and immutable visibility scope.
- Separate coarse authorization from visibility resolution and remove nullable-kind bypass from the migrated path.
- Add tenant-safe snapshots for `CreateDescriptorDraft`, `SearchDescriptors`, `GetDescriptorByRef`, and direct draft operations.
- Add the bidirectional manifest-to-coverage table skeleton and fail closed for every descriptor-bearing tool not yet migrated.

### PR B - Aggregate, Topology, and Context-Pack Closure

- Apply visible-only ordering, paging, `TotalCount`, and truncation semantics to search and list aggregates.
- Build topology and relationship results from visible nodes and edges.
- Build metadata and runtime-scenario context packs from the visible universe with denied-focus semantics.
- Expand the shared coverage table and remove fail-closed migration guards only for completed tools.

### PR C - Indirect and Nested Artifact Closure

- Add tenant-safe batch owner resolution for review, fix, package, evidence, readiness, and activation resources.
- Add typed nested projectors, diagnostic explanation policy, cancellation propagation, tenant isolation, and audit leak tests.
- Complete bidirectional coverage for the entire current manifest and remove the remaining migration guards.

There is no supported long-term dual path. Unmigrated descriptor-bearing tools fail closed between PRs; they never retain a permissive nullable `DescriptorKindConstraint` exemption. Issue #40 is complete only after PR C passes the full acceptance suite and the documentation and `memory.md` are updated.

## 15. Acceptance Tests

### 15.1 Policy and Pipeline

- A denied tool, actor, permission, forged tool name, ungranted mutation, or ungranted activation handoff performs zero descriptor/draft/artifact store reads.
- Explicit allows do not override `DeniedDescriptorKinds`.
- Development open-world policy permits a valid newly introduced kind unless denied; production/hardened closed-world policy denies valid kinds absent from `AllowedDescriptorKinds`.
- Invalid wire values and missing required kind values fail closed in every policy mode.
- A denied explicit kind request returns `Denied`.
- An unresolved protected single target returns `AUTHORIZATION_CONTEXT_UNAVAILABLE`, and the action is not executed.
- Authorization and execution share one resource snapshot; each direct resource and owner is read once.
- Versioned descriptor authorization resolves the exact requested version.

### 15.2 Aggregate Visibility

- Broad search omits denied kinds and computes `TotalCount` and `WasTruncated` after filtering.
- A denied explicit search kind is denied rather than returned as an empty result.
- Draft, review, and fix aggregates omit denied owners; their totals and paging use visible results.
- A restrictive-scope diagnostic has identical outward form whether zero or many records were filtered.
- An aggregate classification/projector failure returns no partial value.

### 15.3 Graph and Context Closure

- Topology contains no denied nodes, incident edges, refs, related diagnostics, or denied-kind keys.
- `TotalNodeCount`, `TotalEdgeCount`, `NodeCountsByKind`, and `EdgeCountsByKind` are recomputed from the visible graph.
- Relationship queries deny a denied subject and omit denied neighbors of an allowed subject.
- A denied context-pack focus is denied.
- Traversal from an allowed focus cannot reveal a denied node, crossing edge, diagnostic, or count.
- Metadata and runtime-scenario context packs apply identical visibility semantics.

### 15.4 Indirect and Nested Closure

- Review, fix, package preview, readiness, and activation reads inherit the owning draft kind.
- Activation submit/read/cancel all enforce the same owner kind.
- A proposal/draft or activation-artifact ownership mismatch is rejected before mutation.
- Review proposed inventories and topology are projected.
- Package and evidence inventories, blockers, refs, counts, and diagnostics are projected.
- Unknown descriptor-bearing nested artifacts fail closed rather than being serialized unchanged.
- `ExplainDiagnostics` without `DraftId` returns only kind-agnostic allowlisted code meaning, severity meaning, and generic remediation; it never echoes caller `Message`, `Path`, unknown code text, or identifier-like fragments.
- Future typed diagnostic associations are resolved in-tenant; denied explicit associations are denied and denied associations in broad input are omitted.

### 15.5 Tenant, Cancellation, and Compatibility

- A resource from another tenant cannot be used to resolve kind, satisfy ownership, influence counts, or prove existence.
- Missing and cross-tenant targets have indistinguishable outward semantics; same-tenant authoritative policy denial remains internally auditable as `Denied`.
- An external adapter configured for anti-probing maps internal `Denied` to `NotFound` consistently without losing the protected audit classification.
- Batch owner resolution is tenant-scoped and does not produce N+1 reads.
- Cancellation during resolution, projection, building, or audit is not swallowed and never returns partial data.
- All existing production authorization defaults continue to deny mutating and activation handoff tools unless explicitly granted.
- Manifest tool names and visibility coverage names are duplicate-free and bidirectionally `SetEquals`; missing, stale, unknown, and duplicate entries fail without asserting a permanent tool count.
- Focused Control Plane tests, dependency-boundary tests, solution build, and trim/AoT-relevant verification pass.

## 16. Completion Criteria

The visibility closure is complete only after PR C, when every descriptor-derived response is either denied as an explicit target or produced entirely from the invocation's visible descriptor universe. Every current manifest tool must have exactly one validated coverage entry, production/hardened policies must use a closed-world effective allow set, and diagnostic explanations must satisfy the typed-association or generic-code-only rules. There must be no nullable-kind bypass, temporarily unguarded unmigrated tool, post-serialization cleanup, hidden pre-filter count, cross-tenant owner lookup, duplicate authorization/execution read, or unclassified manifest tool.
