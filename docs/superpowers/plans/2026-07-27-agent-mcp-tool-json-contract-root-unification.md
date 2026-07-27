# Agent/MCP Tool JSON Contract Root Unification Implementation Plan

**Goal:** Complete Issue #62 by deriving Agent/MCP Tool JSON binding roots from
the existing Tool-Spec declarations, generating immutable binding manifests,
and making existing Contributors consume them without changing runtime
identity, binding keys, resolver composition, wire shape, or NativeAOT behavior.

**Spec:** `docs/superpowers/specs/2026-07-27-agent-mcp-tool-json-contract-root-unification-design.md`

**Issue:** #62

**Branch:** `feature/issue-62-unify-agent-mcp-json-contract-roots`

**Spec status:** APPROVED

**Plan status:** APPROVED FOR IMPLEMENTATION

---

## 1. Execution rules

- Use `rtk` for shell commands and `apply_patch` for edits.
- Preserve unrelated work and do not overwrite unrelated files.
- Relocate owned production files with normal Git moves; do not create a
  duplicate compatibility implementation.
- Do not add a concrete Metadata project dependency to an Abstractions project.
- Do not introduce reflection, assembly scans, `DefaultJsonTypeInfoResolver`,
  or a second contributor/resolver mainline.
- Do not generate MCP binding keys, contributor identities, or module markers.
- Do not treat nested DTO/member types as binding roots.
- Every behavior starts with a named failing test and ends with the focused
  test, regression set, and `git diff --check` green.
- No `AcceptanceSkeleton.Pending` or issue-owned `Fact(Skip=...)` remains at
  final gate.
- `NativeAOT-verified` requires publish, native link, and execution of the
  original produced binary with all scenario sentinels.
- Keep Spec approval independent from implementation status. Record approved
  deviations in the implementation evidence; do not rewrite the approved
  boundary to match accidental code.

## 2. Requirement-to-test matrix

### 2.1 Happy cases

| ID | Test | Requirement |
|---|---|---|
| H01 | `AgentToolSpec_ProducesExactInputAndOutputRoots` | Agent adapter reads exact roles |
| H02 | `McpToolSpec_ProducesExactInputAndOutputRoots` | MCP adapter reads exact roles |
| H03 | `AgentMemoryGeneratedRoots_MatchSpecsExactly` | fourteen Agent roots, no nested extras |
| H04 | `McpMemoryGeneratedRoots_MatchSpecsExactly` | six deduplicated MCP roots |
| H05 | `RepresentativeAgentToolPayloads_RoundTrip` | Agent wire compatibility |
| H06 | `RepresentativeMcpPayloads_RoundTrip` | MCP wire compatibility |
| H07 | both publish-and-run tests | NativeAOT behavior retained |

### 2.2 Boundary cases

| ID | Test | Requirement |
|---|---|---|
| B01 | `SharedRoots_AreDeduplicated` | exact root emitted once |
| B02 | `SharedRoots_RetainAllSpecProvenance` | diagnostics remain traceable |
| B03 | `NestedMemberTypes_AreNotBindingRoots` | STJ owns closure |
| B04 | `MissingInputOrOutput_ContributesOnlyPresentRoot` | optional roles supported |
| B05 | `MixedInterfaceAndToolSpecSurfaces_KeepBindingRoleExact` | common model does not blur role |
| B06 | `RemovingSpec_RemovesStaleRoot` | #58 incremental chain handles deletion |
| B07 | `GeneratedBindingManifest_IsOrdinalStable` | deterministic output |
| B08 | `AgentMemoryPublicManifest_IsConsumableAndImmutable` | cross-assembly use is safe |

### 2.3 Failure cases

| ID | Test | Expected |
|---|---|---|
| F01 | `Fail_UnsupportedSurfaceAdapter` | `CJC002` |
| F02 | `Fail_OpenGenericToolSpecRoot` | `CJC005` |
| F03 | `Fail_UnresolvedToolSpecRoot` | `CJC007` |
| F04 | repository handwritten contributor guard | architecture failure |
| F05 | repository handwritten Context-root guard | architecture failure |
| F06 | existing duplicate ownership test | startup failure unchanged |
| F07 | every-root JsonTypeInfo test | missing metadata fails |
| F08 | snapshot/parity tests | wire/converter drift fails |

### 2.4 Composition cases

| ID | Test | Requirement |
|---|---|---|
| C01 | `AgentMemoryContributor_IdOrderModuleIdRemainUnchanged` | Agent identity frozen |
| C02 | `McpBindingKeys_RemainUnchanged` | explicit key mappings frozen |
| C03 | resolver composition suites | order/options/freeze unchanged |
| C04 | schema parity suites | shared nested types remain compatible |
| C05 | module selection suites | disabled contributor behavior unchanged |

## 3. Slice 0 — Baseline ledger and acceptance skeleton

### Files

- Add `tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests/Semantic/ToolSpecSurfaceTests.cs`.
- Add migration contract tests under Agent Memory Tools and MCP Memory tests.
- Add repository architecture tests in the dependency/architecture test area.

### RED tests

Create the named tests from the Spec before production edits. Initial failures
must prove one of these concrete gaps:

- BuildTask rejects a marked Tool-Spec class as `CJC002`;
- generated model has no Binding roots;
- Context still contains handwritten `JsonSerializable` roots;
- Contributor still contains a handwritten root collection.

### Root ledger

Record the authoritative direct roots:

```text
Agent: 7 InputType + 7 OutputType = 14 unique roots
MCP:   4 InputType + 4 OutputType = 6 unique roots after shared expand pair
```

Record every direct Context property/serializer call and classify it as:

```text
Binding Root / transitive member / fixture-only / unrelated Context
```

No direct non-Spec Agent/MCP extras are expected. If discovered, add an
explicit-root attribute and a direct-use test; do not rely on transitive STJ
metadata.

### Verify

```bash
rtk dotnet test tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests --filter "FullyQualifiedName~ToolSpecSurfaceTests"
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Tests
rtk dotnet test tests/Integrations/CrestCreates.Mcp.Tests
rtk git diff --check
```

## 4. Slice 1 — Common Binding root model and writer

### Production changes

Extend `JsonContractContextModel` with `BindingRoots`. Extend provenance with a
strongly typed source kind and deterministic declaration/role identities.

The merge contract becomes:

```text
SurfaceRoots = interface roots union Tool-Spec roots
BindingRoots = Tool-Spec roots only
AllDirectRoots = SurfaceRoots union ExplicitRoots
```

Update deduplication to merge all provenance entries by exact symbol and sort
them ordinally.

Update `JsonContractSourceWriter` to emit `BindingRootTypes` using the same
immutable FrozenSet implementation as the other manifest sets. Preserve the
existing Public/Internal accessibility setting.

### Tests

- `WriteManifest_ContainsBindingRoots`
- `WriteManifest_BindingRootsAreFrozenAndReadOnly`
- `GeneratedBindingManifest_IsOrdinalStable`
- existing Surface/Explicit/All writer regressions

### Verify

```bash
rtk dotnet test tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests --filter "FullyQualifiedName~JsonContractManifestWriterTests|FullyQualifiedName~JsonContractSourceWriterTests"
rtk git diff --check
```

## 5. Slice 2 — Tool-Spec semantic adapters

### Production changes

Add exact symbol identities for Agent/MCP container and spec attributes.
Resolve them opportunistically: interface-only consumers must not fail merely
because Agent/MCP abstractions are absent.

Add one semantic walker that accepts an adapter descriptor:

```text
container attribute metadata name
spec attribute metadata name
input named argument = InputType
output named argument = OutputType
```

For each accepted container:

1. enumerate nested spec types using the same depth policy as its Tool
   generator;
2. find the exact spec attribute symbol;
3. read InputType and OutputType typed constants;
4. pass each present type through the shared root validator;
5. add spec name and role provenance;
6. deduplicate exact constructed symbols;
7. return roots to both Surface and Binding sets.

Extract the #58 root-shape checks into a shared semantic helper so interface
and Tool-Spec adapters produce identical `CJC004`-`CJC007` behavior. Preserve
method-specific diagnostics for interface surfaces and declaration-specific
provenance for Tool specs.

### Tests

- all BuildTask semantic skeleton tests;
- interface-only projects without Agent/MCP references still pass;
- one Context combining interface, Agent, and MCP surfaces remains stable;
- shared MCP expand roots retain both spec/role provenance entries.

### Verify

```bash
rtk dotnet test tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests --filter "FullyQualifiedName~ToolSpecSurfaceTests|FullyQualifiedName~SurfaceInference"
rtk dotnet test tests/Tooling/CrestCreates.JsonContracts.Build.PackageTests
rtk git diff --check
```

## 6. Slice 3 — Layered Agent declaration ownership

### Production changes

Move `AgentMemoryToolSpecifications.cs` and
`AgentMemoryToolCapabilityIds.cs` from the runtime project into
`Agent.Memory.Tools.Abstractions`; do not retain compatibility copies.
Add runtime-assembly type forwarders for both public outer declarations; CLR
nested identities resolve through the forwarded outer type and cannot carry
individual forwarding attributes.

Add the CodeGenerator analyzer to the Abstractions project. Add a property to
`AgentToolSpecsAttribute` controlling concrete descriptor-registry
auto-registration. Default remains enabled for all existing containers.
Agent Memory opts out because its declaration assembly must remain independent
of `CrestCreates.Metadata` concrete.

Update the generator model/emitter:

- descriptor provider and binding code are still generated;
- `DescriptorProviderRegistry` using/module initializer is omitted only when
  explicitly configured;
- all default containers retain byte-for-byte equivalent registration shape.

Add `InternalsVisibleTo` for the paired Agent Memory runtime assembly. Register
the generated `AgentCapabilityToolDescriptor` provider explicitly in the
existing `AgentMemoryToolDescriptorProviders.EnsureRegistered` mainline.

### Tests

- generator default still emits registry module initializer;
- opt-out emits provider/bindings but no concrete registry reference;
- Agent Abstractions project has no direct `CrestCreates.Metadata` reference;
- Agent descriptors and bindings remain registered exactly once;
- contributor identity and module selection remain unchanged.

### Verify

```bash
rtk dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests --filter "FullyQualifiedName~AgentToolGenerator"
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Tests
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
rtk git diff --check
```

## 7. Slice 4 — Agent Memory Context and Contributor migration

### Production changes

In `AgentMemoryToolJsonSerializerContext`:

- retain the exact `JsonSourceGenerationOptions`;
- remove all handwritten `[JsonSerializable]` entries;
- add `[JsonContractSurface(typeof(AgentMemoryToolSpecifications))]`;
- keep the Context public and partial.

In the Abstractions project:

- reference Core.Abstractions and the JSON BuildTasks transport;
- set `CrestCreatesJsonContractManifestAccessibility=Public`;
- import the repository props/targets exactly once.

In `AgentMemoryToolJsonContextContributor`:

- remove `Roots`;
- return `AgentMemoryToolJsonSerializerContext.RootManifest.BindingRootTypes`;
- preserve Id, Order, ModuleId, and Create behavior.

### Tests

- exact 14-root set equals Tool specs;
- nested DTO/enums are absent from Binding roots but their JsonTypeInfo exists;
- separate runtime assembly consumes the Public immutable manifest;
- contributor identity and resolver composition are unchanged;
- representative payload snapshots/round trips are unchanged.

### Verify

```bash
rtk dotnet build src/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Abstractions
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Tests
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Tools.Tests
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.E2E.Tests
rtk git diff --check
```

## 8. Slice 5 — MCP Memory Context and Contributor migration

### Production changes

In `McpMemoryJsonSerializerContext`:

- retain exact STJ options;
- replace handwritten roots with
  `[JsonContractSurface(typeof(McpMemoryTools))]`.

Import the BuildTasks repository transport once and reference Core.Abstractions
and BuildTasks without leaking task assets to runtime output. Keep manifest
accessibility Internal.

In `McpMemoryJsonContextContributor`:

- remove `_bindingRootTypes`;
- return the generated Binding manifest;
- use that same manifest for ownership registration;
- leave every explicit binding key and JsonTypeInfo property mapping untouched.

### Tests

- exact six-root set equals four spec pairs after deduplication;
- `ctx_recall_*`, `memory_recall_*`, `ctx_expand_*`, and
  `memory_source_expand_*` mappings are unchanged;
- duplicate root ownership still fails;
- resolver composition/schema parity/round trip remain green.

### Verify

```bash
rtk dotnet build src/Integrations/CrestCreates.Mcp.Memory
rtk dotnet test tests/Integrations/CrestCreates.Mcp.Tests
rtk dotnet test tests/Integrations/CrestCreates.Mcp.E2E.Tests
rtk git diff --check
```

## 9. Slice 6 — Repository enforcement

### Guard scope

The production scan covers `src/Runtime/Agent` and `src/Integrations` while
excluding `bin`, `obj`, generated files, tests, fixtures, and unrelated JSON
contexts.

The guard must fail when an externally bound Tool Contributor declares a
production root collection or when a BuildTask-marked Tool Context handwrites
`JsonSerializable` binding roots.

Avoid a brittle ban on all `Type[]`, `HashSet<Type>`, or test contributors.
The guard is ownership-aware and limited to concrete implementations of
`IAgentToolJsonContextContributor` and `IMcpToolJsonContextContributor` plus
their paired marked Contexts.

Add a classification ledger asserting that the only production externally
bound Contexts are Control Plane, Agent Memory, and MCP Memory; later additions
must explicitly choose generated or justified non-generated ownership.

### Tests

- both repository guard names from the Spec;
- every generated binding root resolves JsonTypeInfo;
- enabled contributors own each generated root exactly once;
- no issue-owned skipped acceptance test remains.

## 10. Slice 7 — NativeAOT publish-link-run

Use the existing dedicated fixtures:

- `CrestCreates.Agent.Memory.Tools.AotFixture.Tests`;
- `CrestCreates.Mcp.AotFixture.Tests`.

Verify each test:

1. publishes with `CrestCreatesPublishMode=aot` for linux-x64;
2. confirms the native executable exists;
3. executes that exact binary rather than `dotnet <dll>`;
4. validates every named scenario marker;
5. validates the final success sentinel;
6. fails on missing `JsonTypeInfo` or reflection fallback warnings/errors.

Run:

```bash
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.AotFixture.Tests
rtk dotnet test tests/Integrations/CrestCreates.Mcp.AotFixture.Tests
```

These tests may take several minutes. Publish success without original binary
execution is not acceptance evidence.

## 11. Slice 8 — Full verification and architecture review

### Focused gates

```bash
rtk dotnet test tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests
rtk dotnet test tests/Tooling/CrestCreates.JsonContracts.Build.PackageTests
rtk dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Tests
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Tools.Tests
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.E2E.Tests
rtk dotnet test tests/Integrations/CrestCreates.Mcp.Tests
rtk dotnet test tests/Integrations/CrestCreates.Mcp.E2E.Tests
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
```

### Canonical gates

```bash
rtk dotnet build CrestCreates.slnx
rtk dotnet test CrestCreates.slnx --no-build
rtk git diff --check
```

If unrelated/external-service projects prevent the canonical test command,
record exact project/test evidence and do not disguise the failure as success.

### Architecture review checklist

- one handwritten root authority per migrated projection;
- no runtime discovery/reflection fallback;
- no Abstractions-to-concrete Metadata dependency;
- no second descriptor or binding registration path;
- Public manifest only where cross-assembly consumption requires it;
- immutable manifest instances;
- MCP key mappings unchanged;
- contributor identity/order/module semantics unchanged;
- root deletion is incremental and deterministic;
- both NativeAOT fixtures execute original binaries.

Review the complete branch diff against the approved Spec and label findings by
P0/P1/P2. Fix every P0/P1 issue-owned finding before publication.

## 12. Documentation and publication

Update `memory.md` with:

- #62 implemented state;
- exact Agent/MCP root ownership mainline;
- focused test counts;
- both NativeAOT publish-link-run results;
- explicit non-goals/follow-ups.

Do not change the Spec's APPROVED state. Record implementation status and any
approved deviations in `memory.md` and the PR body.

Before commit:

```bash
rtk git status --short
rtk git diff --stat
rtk git diff --check
rtk rg -n "AcceptanceSkeleton.Pending|Fact\\(Skip" tests/Tooling tests/Runtime/Agent tests/Integrations
```

Commit intentionally scoped files, push
`feature/issue-62-unify-agent-mcp-json-contract-roots`, create a PR linked with
`Closes #62`, and comment the requirement/test/AOT evidence on the PR.
