# Phase 7b Stabilization — Multi-version Context Pack Test Coverage

**Issue**: #37
**Date**: 2026-06-18
**Status**: Approved / Implementation Ready

## Context

Phase 7b Metadata Context Pack implements version-aware resolution through `MetadataContextDescriptorSource.Resolve()`, which produces `ResolvedDescriptor` with canonical refs and ambiguity detection. The #36 stabilization work established:

- Unpinned refs resolve to canonical versioned refs when a single version exists
- Multiple versions for an unpinned ref emit `CTXPACK_AMBIGUOUS_DESCRIPTOR_REF`
- Traversal internals use topology refs; canonicalization only at output time
- Pack closure invariant: relationships only included when both endpoints have descriptor entries
- Summary focus refs are canonicalized

However, most existing tests are effectively single-version. Multi-version descriptors (same Namespace+Id, different Version) must remain distinct in context packs, and this invariant must be locked down by tests before later Phase 7 Agent Tool Surface work depends on ContextPack as context input.

## Scope

### In Scope

- Add test coverage proving multi-version descriptor identity preservation
- Verify exact versioned descriptor resolution (no fallback to another version)
- Verify traversal does not collapse versions
- Verify filters and truncation stay deterministic under multi-version inventory
- Verify stable hash enrichment uses the selected exact descriptor version
- Verify RuntimeScenario traversal preserves versioned refs across steps
- Verify canonical output consistency (descriptors, relationships, summary all use same canonical refs)
- Verify pack closure invariant holds under multi-version scenarios

### Out of Scope

- No new public API unless a test exposes a necessary bug fix
- No LLM integration, prompt formatting, or Agent Tool Surface
- No DescriptorDraft generation, registry mutation, or topology rebuilding
- No changes to production code unless tests reveal a bug

## Design

### Shared Test Infrastructure

**`AssertRelationshipsClosedOverDescriptors(pack)`** — reusable assertion helper:

```csharp
private static void AssertRelationshipsClosedOverDescriptors(MetadataContextPack pack)
{
    var descriptorRefs = pack.Descriptors.Select(d => d.Ref).ToHashSet();
    pack.Relationships.Should().OnlyContain(r =>
        descriptorRefs.Contains(r.From) && descriptorRefs.Contains(r.To));
}
```

This helper is called in all multi-version tests that produce relationships (K2–K4, M1–M3, N5, O3–O4).

### K. Version Identity Preservation

Tests proving that multi-version descriptors with the same Namespace+Id but different Version remain distinct in context packs. Acceptance criterion: "same kind/id with different versions must remain distinct."

**K1. `FocusOnly_With_TwoVersions_Resolves_Exact_Version`**
- Inventory: CapA@v1 + CapA@v2
- Focus: CapA@v2
- Assert: pack.Descriptors contains only CapA@v2 entry, not CapA@v1

**K2. `DirectDependencies_Preserves_Dependency_Version`**
- Topology: Workflow@v2 → CapA@v2 (Uses), Workflow@v1 → CapA@v1 (Uses)
- Focus: Workflow@v2
- Assert:
  - Descriptors contain Workflow@v2 + CapA@v2, not CapA@v1
  - Relationships: only Workflow@v2→CapA@v2
  - Relationship endpoints version correct
  - Summary.FocusRefs version correct
  - Pack closure invariant holds

**K3. `DirectDependents_Preserves_Dependent_Version`**
- Topology: CapA@v2 depends on Schema@v2, CapA@v1 depends on Schema@v1
- Focus: Schema@v2
- Assert:
  - Descriptors contain Schema@v2 + CapA@v2, not CapA@v1
  - Relationships: only CapA@v2→Schema@v2
  - Relationship endpoints version correct
  - Summary.FocusRefs version correct
  - Pack closure invariant holds

**K4. `ImpactRadius_Does_Not_Collapse_SameId_DifferentVersions`**
- Topology: two parallel chains (v1 lane and v2 lane) with same Namespace+Id nodes
- Focus: v2 root
- Assert:
  - Traversal stays within v2 lane
  - No v1 descriptors or relationships appear
  - Relationship endpoints version correct
  - Summary.FocusRefs version correct
  - Pack closure invariant holds

**K5. `InventoryOnly_MultiVersion_Focus_Keeps_Versions_Distinct`**
- No topology nodes for focus; inventory: CapA@v1 + CapA@v2
- Focus: CapA@v2
- Assert: only CapA@v2 descriptor entry, not CapA@v1

### L. Exact Version Resolution and Unpinned Ref Ambiguity

Tests proving that when Version is provided, the builder resolves by exact match; when Version is absent, only unambiguous single-version matches are allowed.

Resolution rules:
- Versioned refs must resolve by exact Namespace + Id + Version.
- Unpinned refs may resolve by Namespace + Id only when exactly one matching version exists.
- Multiple versions must produce `CTXPACK_AMBIGUOUS_DESCRIPTOR_REF`.

**L1. Covered by H4**: Unpinned single-version ref resolves and outputs canonical versioned ref.

**L2. Covered by H5**: Unpinned multi-version focus emits `CTXPACK_AMBIGUOUS_DESCRIPTOR_REF` and skips.

**L3. `Unpinned_Edge_Target_Multiple_Versions_Does_Not_Guess`**
- Focus: resolvable versioned descriptor
- Traversal discovers unpinned edge target with v1+v2 in inventory
- Assert:
  - `CTXPACK_AMBIGUOUS_DESCRIPTOR_REF` diagnostic emitted for the target
  - Target v1 and v2 are not in Descriptors
  - Relationship is excluded (pack closure)
  - Source focus descriptor is still present

**L4. `Missing_Exact_Version_Does_Not_Fallback_To_Another_Version`**
- Topology: contains CapA@v3 node
- Inventory: CapA@v1 + CapA@v2 (no v3)
- Focus: CapA@v3
- Assert:
  - No CapA entry in Descriptors (not v1, not v2)
  - `CTXPACK_DESCRIPTOR_MISSING_FOR_TOPOLOGY_REF` diagnostic emitted
  - No fallback to v1 or v2

**L5. `Missing_Exact_Version_With_Single_Other_Version_Does_Not_Fallback`**
- Topology: contains CapA@v3 node
- Inventory: CapA@v1 only (no v3)
- Focus: CapA@v3
- Assert:
  - No CapA entry in Descriptors (not v1)
  - `CTXPACK_DESCRIPTOR_MISSING_FOR_TOPOLOGY_REF` diagnostic emitted
  - Must NOT resolve CapA@v3 to CapA@v1 — this is the single-version fallback that the ambiguous case hides

### M. Version-aware Traversal

Tests proving traversal and step boundaries preserve versioned refs.

**M1. `RuntimeScenario_Preserves_Versioned_Boundary_Between_Steps`**
- Step 1 discovers HumanTask@v2
- Step 2 boundary = HumanTask@v2 only
- Assert: v1 and unpinned equivalents are not used as step boundary
- Assert: pack closure invariant holds

**M2. `RuntimeScenario_MultiStep_Preserves_Versioned_Relationships`**
- Step 1 discovers Workflow@v2
- Step 2 traverses from Workflow@v2 to CapA@v2
- Assert:
  - Relationship endpoints are canonical versioned refs (Workflow@v2, CapA@v2)
  - No v1 relationships appear
  - Pack closure invariant holds

**M3. `ImpactRadius_MultiVersion_Traversal_Preserves_Versioned_Edge_Endpoints`**
- BFS from v2 root stays on v2 channel
- Assert:
  - All descriptor refs preserve version
  - All relationship endpoint refs preserve version
  - Pack closure invariant holds

### N. Deterministic Ordering and Bounds Under Multi-version Inventory

Tests proving filters and bounds operate on selected versioned refs, not id-collapsed refs.

**N1. `IncludeKinds_Does_Not_Collapse_MultipleVersions`**
- Traversal candidate set contains CapA@v1 + CapA@v2
- IncludeKinds = Capability
- Assert: both versions remain in output

**N2. `ExcludeKinds_Does_Not_Change_VersionIdentity`**
- Traversal candidate set contains Schema@v1 + Schema@v2 + CapA@v1
- ExcludeKinds = Schema
- Assert: both Schema versions excluded, CapA@v1 remains

**N3. `MaxDescriptorCount_Truncates_Deterministically_With_MultipleVersions`**
- Focus descriptors retained first
- Same semantic graph with shuffled inputs produces same retained set
- Tests deterministic stability of truncation, not a specific sort contract

**N4. `Deterministic_Ordering_With_MultipleVersions_SameKind`**
- Multiple versions of same kind/id
- Output order is stable across shuffled input
- Tests deterministic stability of ordering, not a specific sort contract

**N5. `Relationships_With_MultipleVersions_Are_Not_Deduped_By_Unversioned_Id`**
- v1/v2 relationships with same kind/id pair remain distinct
- Relationship ordering uses canonical From/To refs
- No dedup collapse between v1 and v2 relationship pairs

### O. Canonical Output Consistency and Pack Closure

Tests proving descriptor entries, relationships, and summary focus refs use the same canonical output ref semantics.

**O1. `Summary_FocusRefs_Are_Canonicalized_For_Unpinned_SingleVersion_Focus`**
- Unpinned focus ref, inventory has single v1
- Assert: Summary.FocusRefs matches descriptor entry Ref (both canonical versioned)

**O2. Covered by J3**: Unpinned edge endpoints produce canonical relationship endpoint refs.

**O3. `Relationships_Are_Closed_Over_Output_DescriptorRefs`**
- Explicit test for pack closure invariant under multi-version
- Uses `AssertRelationshipsClosedOverDescriptors(pack)` helper
- Every relationship From/To exists in pack.Descriptors.Select(d => d.Ref)

**O4. `Canonical_Ref_Consistency_Across_Descriptors_Relationships_Summary`**
- Unpinned focus + unpinned traversal target, single-version inventory
- Assert all three output types use the same canonical versioned ref values:
  - DescriptorEntry.Ref == expected canonical ref
  - Relationship.From/To == expected canonical refs
  - Summary.FocusRefs == expected canonical refs
- Pack closure invariant holds

### P. Enrichment Under Multi-Version

Tests proving enrichment uses the exact selected version, not another version of the same id.

**P1. `StableHashes_Computed_For_Selected_Exact_Version_In_Traversal`**
- ImpactRadius from v2 root discovers CapA@v2
- Hash builder receives CapA@v2 descriptor instance, not CapA@v1
- Verify via Mock<IDescriptorStableHashBuilder>.Callback + BeSameAs

**P2. `GovernanceEntry_Uses_Selected_Descriptor_Version_State`**
- CapA@v1 = Draft, CapA@v2 = Active
- Focus: CapA@v2
- Assert: governance entry shows Active, RequiresReview=false
- Not Draft from v1

This test does not validate lifecycle governance rules. It only verifies that lightweight governance state is populated from the selected exact descriptor version.

## Test Count Summary

| Group | New Tests | Cross-refs | Total |
|-------|-----------|------------|-------|
| K | 5 | 0 | 5 |
| L | 3 | 2 (H4, H5) | 3 |
| M | 3 | 0 | 3 |
| N | 5 | 0 | 5 |
| O | 3 | 1 (J3) | 3 |
| P | 2 | 0 | 2 |
| **Total** | **21** | **3** | **21** |

Existing test count: 45. After: 66.

## Acceptance Criteria Traceability

| Criterion | Tests |
|-----------|-------|
| Same kind/id with different versions remain distinct | K1–K5 |
| Builder never resolves by kind/id alone when version is available | L1–L5, K1–K5 |
| Traversal and relationship collection preserve versioned refs | M1–M3, K2–K4 |
| Deterministic ordering remains stable with multiple versions | N1–N5 |
| Tests prevent accidental collapse to first descriptor matching kind/id | K1–K5, L3–L4, N5 |
| Descriptor entries, relationships, summary focus refs use same canonical refs | O1–O4 |
| Every relationship endpoint exists in descriptor entry set | O3 (helper reused in K2–K4, M1–M3, N5, O4) |
| No new public API unless test exposes necessary bug fix | By construction |
