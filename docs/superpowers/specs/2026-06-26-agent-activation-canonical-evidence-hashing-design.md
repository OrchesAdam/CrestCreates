# Agent Activation Canonical Evidence Hashing — Design Spec

**Issue**: #47  
**Supersedes**: #45  
**Date**: 2026-06-26  
**Status**: Draft  

---

## 1. Context

#45 targeted removal of the temporary `sha256-adhoc-v1` Agent activation binding
hash helpers. That local fix would remove the immediate ad hoc path in
`DefaultAgentControlPlaneToolService`, but it would still leave package and
evidence hashing on a string digest main chain.

#47 replaces that direction with a breaking end-state migration. Agent
activation evidence, package manifests, package evidence, and evidence
envelopes must use the canonical hash runtime directly. String digest values
may exist only in DTO or display projections; they must not remain the source
of truth for package or activation evidence binding.

Current code facts:

- `DefaultAgentControlPlaneToolService` computes review hashes with local
  pipe-delimited string input and `sha256-adhoc-v1` metadata.
- `DescriptorPackageHashComputer` computes `ContentHash`, `EvidenceHash`, and
  `EnvelopeHash` as string digests.
- `BindingHashes` already stores `CanonicalHash`, but the producer side can
  still create fake runtime hashes.
- `DefaultActivationEvidenceRechecker` already compares full `CanonicalHash`
  records. That behavior is correct and must remain.

## 2. Goals

- Make Agent activation evidence hashing canonical end to end.
- Remove `sha256-adhoc-v1` and pipe-delimited hash input from activation,
  review, package, evidence, and envelope binding paths.
- Move ReviewResult canonical hashing ownership into DescriptorDraft.
- Move package manifest/evidence/envelope canonical hashing ownership into
  Metadata DescriptorPackage.
- Keep Agent Control Plane as a consumer and validator of hashes, not a hash
  producer.
- Replace package/evidence string digest source-of-truth models with
  `CanonicalHash`.
- Preserve full `CanonicalHash` equality for stale evidence detection.
- Keep all canonical writers AoT-safe and deterministic.

## 3. Non-Goals

- Do not preserve `sha256-adhoc-v1` compatibility.
- Do not add an adapter that wraps existing string digests while leaving the
  package hash main chain for later.
- Do not change the Agent activation state machine, approval policy, rejection
  policy, self-approval checks, or `IRuntimeActivationGate` semantics.
- Do not make `CanonicalHash.Scope` perform visibility filtering. Scope is
  domain-separation metadata only.
- Do not move ReviewResult hashing into Agent Control Plane.
- Do not make Metadata core depend on DescriptorDraft.
- Do not weaken evidence recheck to compare only `CanonicalHash.Value`.

## 4. Architecture Direction

Use a three-owner model:

```text
Metadata.Abstractions
  Owns CanonicalHash, ICanonicalHashComputer, Artifact/Purpose/Scope names.

DescriptorDraft
  Owns ReviewResult canonical projections and review hash service.

Metadata DescriptorPackage
  Owns package manifest/evidence/envelope canonical projections and hash set.

Agent.ControlPlane
  Consumes hash services, validates BindingHashes slots, stores/resolves hashes.
```

Hard rules:

- ReviewResult canonical hashing is owned by DescriptorDraft.
- Package/evidence/envelope canonical hashing is owned by Metadata
  DescriptorPackage.
- Agent Control Plane must not define ReviewResult or package canonical writers.
- Agent Control Plane production code must not manually instantiate production
  `CanonicalHash` values for computed review, package, evidence, envelope,
  contract, or definition hashes.
- Metadata canonical hash runtime must not reference DescriptorDraft review
  models.

Correct dependency direction:

```text
Agent.ControlPlane
  -> DescriptorDraft.Abstractions
  -> Metadata.Abstractions

DescriptorDraft
  -> DescriptorDraft.Abstractions
  -> Metadata.Abstractions
  -> Metadata

Metadata
  -> Metadata.Abstractions
```

Forbidden dependency direction:

```text
Metadata -> DescriptorDraft
DescriptorDraft -> Agent.ControlPlane
Agent.ControlPlane owns ReviewResult writer
```

## 5. File Placement and Naming Rules

The design spec lives at:

```text
docs/superpowers/specs/2026-06-26-agent-activation-canonical-evidence-hashing-design.md
```

The future implementation plan should live at:

```text
docs/superpowers/plans/2026-06-26-agent-activation-canonical-evidence-hashing.md
```

DescriptorDraft review hashing contracts:

```text
src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/CanonicalHashing/
  IDescriptorDraftReviewHashService.cs
  ReviewResultSourceBindingProjection.cs
  ReviewResultIntegrityProjection.cs
  ReviewDiagnosticProjection.cs
  DescriptorDraftReviewCanonicalShapeVersions.cs
```

DescriptorDraft review hashing implementation:

```text
src/Metadata/Draft/CrestCreates.DescriptorDraft/CanonicalHashing/
  DefaultDescriptorDraftReviewHashService.cs
  ReviewResultSourceBindingCanonicalHashWriter.cs
  ReviewResultIntegrityCanonicalHashWriter.cs
```

DescriptorPackage hashing contracts:

```text
src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorPackage/
  IDescriptorPackageCanonicalHashComputer.cs
  DescriptorPackageHashSet.cs
  DescriptorPackageEvidenceEnvelope.cs
  DescriptorPackageCanonicalShapeVersions.cs
```

DescriptorPackage hashing implementation:

```text
src/Metadata/CrestCreates.Metadata/DescriptorPackage/CanonicalHashing/
  DefaultDescriptorPackageCanonicalHashComputer.cs
  DescriptorPackageManifestCanonicalHashWriter.cs
  DescriptorPackageEvidenceCanonicalHashWriter.cs
  DescriptorPackageEvidenceEnvelopeCanonicalHashWriter.cs
```

Agent activation validation:

```text
src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Activation/
  ActivationBindingHashValidator.cs
```

Naming rules:

- File name must match the main type name.
- Canonical writer types use the `CanonicalHashWriter` suffix.
- Review hash service is named `IDescriptorDraftReviewHashService` /
  `DefaultDescriptorDraftReviewHashService`.
- Package hash computer is named `IDescriptorPackageCanonicalHashComputer` /
  `DefaultDescriptorPackageCanonicalHashComputer`.
- Binding validator is named `ActivationBindingHashValidator`.
- Do not introduce `HashHelper`, `HashUtils`, `ComputeSha256Helper`, or
  unowned helper-style hash types.
- Canonical shape versions must be centralized in version constant classes.
- `DefaultAgentControlPlaneToolService`,
  `DefaultDescriptorActivationRequestService`, and
  `DefaultActivationEvidenceRechecker` must not grow private hash helpers.

## 6. Canonical Artifact Model

Add precise artifact names instead of overloading `Package` for every package
sub-artifact.

Required artifact names:

- `ReviewResult`
- `PackageManifest`
- `PackageEvidence`
- `PackageEvidenceEnvelope`
- `Descriptor`

Required binding hash slot metadata:

| Binding field | ArtifactKind | Purpose | Scope |
| --- | --- | --- | --- |
| `SourceReviewHash` | `ReviewResult` | `SourceBinding` | `InternalFull` |
| `ReviewManifestHash` | `ReviewResult` | `Integrity` | `InternalFull` |
| `PackageManifestHash` | `PackageManifest` | `Integrity` | `InternalFull` |
| `EvidenceHash` | `PackageEvidence` | `AuditEvidence` | `InternalFull` |
| `EnvelopeHash` | `PackageEvidenceEnvelope` | `AuditEvidence` | `InternalFull` |
| `ContractHash` | `Descriptor` | `Contract` | `InternalFull` |
| `DefinitionHash` | `Descriptor` | `Definition` | `InternalFull` |

`EnvelopeHash` purpose is fixed to `AuditEvidence`. It is not left open to
choose between `Integrity` and `AuditEvidence` during implementation.

## 7. BindingHashes Breaking Model

Replace the ambiguous `ManifestHash` slot with explicit review and package
manifest slots:

```csharp
public sealed record BindingHashes
{
    public required CanonicalHash SourceReviewHash { get; init; }
    public required CanonicalHash ReviewManifestHash { get; init; }
    public required CanonicalHash PackageManifestHash { get; init; }
    public required CanonicalHash EvidenceHash { get; init; }
    public required CanonicalHash EnvelopeHash { get; init; }
    public required CanonicalHash ContractHash { get; init; }
    public required CanonicalHash DefinitionHash { get; init; }
}
```

Semantics:

- `SourceReviewHash`: source-binding view of `DescriptorDraftReviewResult`.
- `ReviewManifestHash`: integrity view of `DescriptorDraftReviewResult`.
- `PackageManifestHash`: canonical package manifest hash.
- `EvidenceHash`: canonical package evidence hash.
- `EnvelopeHash`: canonical evidence envelope hash.
- `ContractHash` and `DefinitionHash`: descriptor canonical hashes produced by
  `IDescriptorStableHashBuilder`.

`ReviewManifestHash` and `PackageManifestHash` must not be collapsed into a
single generic `ManifestHash` name.

## 8. DescriptorDraft Review Hashing

Add a review hash service in DescriptorDraft abstractions:

```csharp
public interface IDescriptorDraftReviewHashService
{
    CanonicalHash ComputeSourceReviewHash(
        DescriptorDraftReviewResult reviewResult);

    CanonicalHash ComputeReviewManifestHash(
        DescriptorDraftReviewResult reviewResult);
}
```

`DescriptorDraftReviewResult` has two independent canonical views:

- `ReviewResultSourceBindingProjection`
- `ReviewResultIntegrityProjection`

The integrity projection must not contain `SourceReviewHash`. The two hashes
are sibling views of the same review result, not nested hashes of each other.

The service implementation must:

- build explicit projection records from `DescriptorDraftReviewResult`,
- use dedicated canonical writers,
- call `ICanonicalHashComputer.ComputeFromProjection`,
- never use `JsonSerializer`,
- never use runtime reflection,
- never use pipe-delimited string input,
- never compute SHA-256 locally.

Source review hashing continues to bind internal review result semantics. It
must not silently switch to tenant-visible projected review data. Visibility
projection remains owned by Agent artifact projection and package construction.

## 9. DescriptorPackage Breaking Model

`DescriptorManifest` must not contain its own `PackageManifestHash`. A manifest
cannot include the hash of itself without creating a self-hash cycle.

Package-level hashes live on `DescriptorPackage` or a sibling hash set, not
inside `DescriptorManifest`:

```csharp
public sealed class DescriptorPackage
{
    public DescriptorManifest Manifest { get; init; } = new();
    public DescriptorSnapshot Snapshot { get; init; } = new();
    public DescriptorPackageEvidence Evidence { get; init; } = new();
    public DescriptorPackageEvidenceEnvelope EvidenceEnvelope { get; init; } = new();

    public required DescriptorPackageHashSet Hashes { get; init; }
}
```

The hash set is the package hash source of truth:

```csharp
public sealed record DescriptorPackageHashSet
{
    public required CanonicalHash PackageManifestHash { get; init; }
    public required CanonicalHash EvidenceHash { get; init; }
    public required CanonicalHash EnvelopeHash { get; init; }
}
```

`ContentHash` is removed from domain/source-of-truth models. It may exist only
in DTO or display projections as a derived value:

```csharp
public string ContentHash => PackageManifestHash.Value;
```

Derived string digest values must not flow back into package building,
activation binding, resolver storage, recheck, gate execution, or canonical
hash computation.

`DescriptorPackagePreview` changes to canonical hashes:

```csharp
public sealed record DescriptorPackagePreview
{
    public required CanonicalHash PackageManifestHash { get; init; }
    public required CanonicalHash EvidenceHash { get; init; }
    public required CanonicalHash EnvelopeHash { get; init; }
    public required IReadOnlyList<string> DescriptorIds { get; init; }
}
```

If an agent-facing DTO needs display strings, the DTO mapper may project
`CanonicalHash.Value`, but the runtime preview model remains `CanonicalHash`.

## 10. DescriptorPackage Hash Computer

Add a package hash computer contract:

```csharp
public interface IDescriptorPackageCanonicalHashComputer
{
    DescriptorPackageHashSet ComputeHashSet(
        DescriptorManifest manifest,
        DescriptorPackageEvidence evidence,
        DescriptorPackageEvidenceEnvelope envelope);
}
```

The implementation must:

- produce all package/evidence/envelope hashes in one call,
- use `ICanonicalHashComputer.ComputeFromProjection`,
- use dedicated canonical writers for each artifact,
- return a single `DescriptorPackageHashSet`,
- not expose string digest values as canonical input,
- not keep `DescriptorPackageHashComputer` as a production main chain.

`DefaultDescriptorPackageBuilder` should call
`IDescriptorPackageCanonicalHashComputer` after building the manifest,
evidence, and evidence envelope.

## 11. Activation Runtime Flow

### 11.1 ReviewDescriptorDraftAsync

```text
IDescriptorDraftReviewService.ReviewAsync
  -> IDescriptorDraftReviewHashService.ComputeSourceReviewHash(reviewResult)
  -> IDescriptorDraftReviewHashService.ComputeReviewManifestHash(reviewResult)
  -> IActivationBindingArtifactResolver.StoreReviewHashes(...)
```

Agent Control Plane does not compute review hashes.

### 11.2 PreviewDescriptorPackageAsync

```text
IDescriptorPackageBuilder.Build(...)
  -> DescriptorPackage.Hashes
  -> IActivationBindingArtifactResolver.StorePackageHashSet(...)
```

Package, evidence, and envelope hashes are stored as one
`DescriptorPackageHashSet`. They must not be split across unrelated resolver
methods.

### 11.3 BuildPackageEvidencePreviewAsync

`BuildPackageEvidencePreviewAsync` must prefer an existing package preview for
the same tenant, draft, and visible universe when available. It must not
perform unbounded duplicate package builds that can create evidence previews
bound to different package build outputs.

If a reusable preview is available:

```text
resolve package preview
  -> reuse DescriptorPackageHashSet
  -> build/project evidence preview from the same package artifact
  -> StoreEvidenceHashSet(...)
```

If no reusable preview is available:

```text
build package once
  -> create package preview snapshot
  -> create evidence preview snapshot
  -> store the same DescriptorPackageHashSet for both references
```

Snapshot records should preserve enough identity to make reuse safe:

- `TenantId`
- `DraftId`
- draft version
- visibility scope key or equivalent scope identity
- `DescriptorPackageHashSet`
- package/evidence artifact data needed for evidence preview projection

### 11.4 SubmitActivationRequestAsync

Submit validates references and hash slots, then creates the activation
request. It must not compute any hash.

### 11.5 Recheck and Gate

`DefaultActivationEvidenceRechecker` resolves current hashes and recomputes
descriptor contract/definition hashes through `IDescriptorStableHashBuilder`.
It keeps comparing full `CanonicalHash` records.

`DefaultDescriptorActivationRequestService` must validate binding hash slots
again immediately before calling `IRuntimeActivationGate.ActivateAsync`.

## 12. ActivationBindingHashValidator

`ActivationBindingHashValidator` is a deterministic validator. It does not
compute hashes, access registries, perform visibility filtering, or mutate
state.

It must be called:

- during `SubmitActivationRequestAsync`,
- inside `DefaultActivationEvidenceRechecker.RecheckAsync` before comparison,
- immediately before runtime gate execution.

It validates:

- all seven `BindingHashes` fields are present,
- all canonical hash metadata fields are non-empty,
- each slot has the required `ArtifactKind`, `Purpose`, and `Scope`,
- algorithm and contract metadata are present,
- current resolved hashes also match the expected slot metadata before stale
  comparison.

Validation failures fail closed through activation diagnostics. Add semantic
diagnostic codes if the current set is too broad, such as:

- `ACTIVATION_BINDING_HASH_SLOT_INVALID`
- `ACTIVATION_BINDING_HASH_METADATA_MISMATCH`

## 13. Canonical Writer Rules

All canonical writers must:

- use `Utf8JsonWriter`,
- write the complete canonical envelope,
- write properties in fixed order,
- use explicit collection ordering,
- distinguish null and empty string,
- write booleans as JSON booleans,
- write enums as stable string names unless an existing canonical profile for
  the same artifact already requires stable integer values,
- never use `JsonSerializer`,
- never use `JsonTypeInfo`,
- never use runtime `Type`,
- never use reflection,
- never read ambient context,
- never use current time.

`DateTimeOffset` formatting:

- Normalize to UTC before writing.
- Write as ISO-8601 round-trip string using the `O` format.
- Do not preserve input offset differences in canonical output.

Number formatting:

- Use invariant formatting where values are written as strings.
- Prefer `Utf8JsonWriter.WriteNumberValue` for integer values.
- If decimal values appear, the writer must define one stable representation
  before implementation. Do not rely on current culture.

Canonical shape version constants:

```csharp
public static class DescriptorDraftReviewCanonicalShapeVersions
{
    public const string SourceBindingV1 =
        "descriptor-draft-review-source-binding-v1";

    public const string IntegrityV1 =
        "descriptor-draft-review-integrity-v1";
}
```

```csharp
public static class DescriptorPackageCanonicalShapeVersions
{
    public const string PackageManifestV1 =
        "descriptor-package-manifest-v1";

    public const string PackageEvidenceV1 =
        "descriptor-package-evidence-v1";

    public const string PackageEvidenceEnvelopeV1 =
        "descriptor-package-evidence-envelope-v1";
}
```

## 14. CanonicalHash Construction Rule

Consumption and orchestration layers must not manually instantiate production
`CanonicalHash` values.

Forbidden in production consumption/orchestration layers:

- Agent Control Plane services,
- Agent tool service,
- activation request service,
- activation evidence rechecker,
- activation resolver call sites,
- package preview orchestration,
- report builders when producing activation binding hashes.

Allowed:

- `DefaultCanonicalHashComputer`,
- canonical hash computer implementations,
- canonical writer/hash unit tests,
- test fixtures,
- JSON parser and DTO deserialization,
- explicit validation tests.

The intent is not to ban `new CanonicalHash` globally. The intent is to ensure
computed production hashes cross the canonical computation boundary.

## 15. Resolver Contract

The activation artifact resolver should store package hash sets atomically:

```csharp
void StoreReviewHashes(
    string tenantId,
    string reviewResultId,
    CanonicalHash sourceReviewHash,
    CanonicalHash reviewManifestHash);

void StorePackageHashSet(
    string tenantId,
    string packagePreviewId,
    DescriptorPackageHashSet hashSet);

void StoreEvidenceHashSet(
    string tenantId,
    string evidencePreviewId,
    DescriptorPackageHashSet hashSet);
```

Resolved artifacts should expose the same slot names used by `BindingHashes`:

- `CurrentSourceReviewHash`
- `CurrentReviewManifestHash`
- `CurrentPackageManifestHash`
- `CurrentEvidenceHash`
- `CurrentEnvelopeHash`
- `CurrentContractHash`
- `CurrentDefinitionHash`

Do not store `PackageManifestHash`, `EvidenceHash`, and `EnvelopeHash` through
unrelated resolver methods.

## 16. DTO and Display Projection Policy

String digest values are allowed only as display or wire projections when a
consumer benefits from concise text output.

Allowed examples:

- report text,
- CLI/table display,
- DTO summary fields explicitly named as value/display fields.

Rules:

- Display strings are derived from `CanonicalHash.Value`.
- Display strings must not be passed back into production runtime APIs as hash
  inputs.
- Domain/runtime models keep `CanonicalHash`.
- DTO names should make projection explicit, for example `PackageManifestHash`
  as full object or `PackageManifestHashValue` as string.
- Do not keep `ContentHash` on source-of-truth models.

## 17. Tests

DescriptorDraft review hashing tests:

```text
tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests/CanonicalHashing/
  DescriptorDraftReviewHashServiceTests.cs
  ReviewResultCanonicalHashWriterTests.cs
  GoldenFiles/
    review-result-source-binding-v1.json
    review-result-integrity-v1.json
```

DescriptorPackage hashing tests:

```text
tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorPackage/CanonicalHashing/
  DescriptorPackageCanonicalHashComputerTests.cs
  DescriptorPackageCanonicalHashWriterTests.cs
  GoldenFiles/
    descriptor-package-manifest-v1.json
    descriptor-package-evidence-v1.json
    descriptor-package-evidence-envelope-v1.json
```

Agent activation tests:

```text
tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/Activation/
  ActivationBindingHashValidatorTests.cs
  ActivationEvidenceRecheckerCanonicalHashTests.cs
  ActivationBindingCanonicalHashFlowTests.cs
```

Boundary/guard tests:

```text
tests/Boundary/CrestCreates.DependencyBoundaries.Tests/
  AgentActivationCanonicalHashGuardTests.cs
```

Required behavior tests:

- ReviewResult source-binding hash uses `ReviewResult + SourceBinding +
  InternalFull`.
- ReviewResult manifest hash uses `ReviewResult + Integrity + InternalFull`.
- Package manifest hash uses `PackageManifest + Integrity + InternalFull`.
- Evidence hash uses `PackageEvidence + AuditEvidence + InternalFull`.
- Envelope hash uses `PackageEvidenceEnvelope + AuditEvidence + InternalFull`.
- Same semantic input produces stable `CanonicalHash`.
- Relevant input changes change the hash.
- Collection ordering is deterministic.
- DateTimeOffset output is UTC-normalized and stable.
- Number formatting is culture-invariant.
- Null and empty string are distinct.
- Visibility projection does not silently change internal source-binding
  semantics.
- Metadata mismatch causes activation recheck to fail stale even if hash
  `Value` matches.
- `BuildPackageEvidencePreviewAsync` reuses package preview/hash set when a
  matching package preview exists.
- Resolver stores package/evidence/envelope hashes as `DescriptorPackageHashSet`
  instead of scattered method state.

## 18. Golden Output Tests

Canonical JSON golden output tests are required for each new writer.

Golden files:

```text
tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests/CanonicalHashing/GoldenFiles/
  review-result-source-binding-v1.json
  review-result-integrity-v1.json

tests/Metadata/Core/CrestCreates.Metadata.Tests/DescriptorPackage/CanonicalHashing/GoldenFiles/
  descriptor-package-manifest-v1.json
  descriptor-package-evidence-v1.json
  descriptor-package-evidence-envelope-v1.json
```

Rules:

- Writer output must match golden JSON bytes exactly.
- Hash values must be computed from the exact golden bytes.
- Golden cases must cover collection order, DateTimeOffset, numeric values,
  null, and empty string.
- Golden file updates require explicit review because they are canonical shape
  changes.

## 19. Guard Tests

Guard tests combine strict bans with heuristic detection to avoid false
positives.

Scan production paths:

```text
src/Runtime/Agent/CrestCreates.Agent.ControlPlane/**/*.cs
src/Metadata/Draft/CrestCreates.DescriptorDraft/**/*.cs
src/Metadata/CrestCreates.Metadata/DescriptorPackage/**/*.cs
```

Strict bans:

- `sha256-adhoc-v1`
- `ComputeSha256(` in scanned production paths
- `ComputeSourceReviewHash(` in Agent production path
- `ComputeReviewManifestHash(` in Agent production path
- `new CanonicalHash` in Agent production path, except parser/deserialization
  code if explicitly allowlisted

Heuristic bans:

- same method contains `StringBuilder`, `Append('|')`, and hash/SHA keywords,
- package source-of-truth model assigns or stores `ContentHash`,
- production activation binding assigns `EvidenceHash` or `EnvelopeHash` from
  `pkg.Manifest.*Hash` string fields,
- production resolver stores package manifest/evidence/envelope hashes through
  separate methods instead of `DescriptorPackageHashSet`.

Allowlisted cases:

- `DefaultCanonicalHashComputer`,
- canonical hash computer implementations,
- canonical writer tests,
- test fixtures,
- golden output tests,
- DTO/display projection derived from `CanonicalHash.Value`.

## 20. Migration Notes

This issue is breaking by design.

Expected migration impact:

- `BindingHashes.ManifestHash` becomes `ReviewManifestHash` and
  `PackageManifestHash`.
- Package preview models switch from string hashes to `CanonicalHash`.
- Package manifest source-of-truth no longer contains `ContentHash`,
  `EvidenceHash`, or `EnvelopeHash` string digest fields.
- Package diff and serializer tests must move to canonical hash fields.
- Agent DTO mappers may add display string projections if needed.
- Existing tests that construct placeholder `CanonicalHash` may continue doing
  so as fixtures, but production flow tests must use canonical services.

Do not keep a long-term duplicate path where both string digest hashes and
canonical hashes are valid production sources of truth.

## 21. Verification

Minimum verification:

```bash
dotnet test tests/Metadata/Draft/CrestCreates.DescriptorDraft.Tests
dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests
dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
dotnet build
```

Broaden testing if changes touch additional Metadata, Runtime, DTO, serializer,
or sample projects.

## 22. Acceptance Criteria

- Agent activation/package/evidence binding paths contain no
  `sha256-adhoc-v1`.
- Agent activation/package/evidence binding paths contain no local
  `ComputeSha256`.
- Agent activation/package/evidence binding paths contain no pipe-delimited hash
  protocol.
- `DefaultAgentControlPlaneToolService` contains no private production hash
  helpers.
- ReviewResult hashes are produced by `IDescriptorDraftReviewHashService`.
- Package/evidence/envelope hashes are produced as `DescriptorPackageHashSet`
  by `IDescriptorPackageCanonicalHashComputer`.
- `BindingHashes` has explicit `ReviewManifestHash` and `PackageManifestHash`
  slots.
- `ContentHash` is not a domain/source-of-truth package hash.
- `ActivationBindingHashValidator` runs at submit, recheck, and pre-gate
  boundaries.
- Full `CanonicalHash` equality remains the stale evidence comparison.
- Canonical JSON golden output tests exist for every new writer.
- Boundary guard tests prevent the ad hoc hash path from returning.
