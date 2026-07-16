# Phase 8f Agent Tool Projection Implementation Plan

> Workers implement one bounded task at a time. Do not commit, do not edit files
> outside the assigned ownership set, and report exact tests run. The primary
> agent owns cross-slice integration, architecture review, and final verification.

**Goal:** Project explicitly authored Capabilities into provider-neutral,
governed Agent Tools that bind exact source-generated JSON contracts and execute
only through the captured `CapabilityDescriptor` Dispatcher mainline.

**Design:** `docs/superpowers/specs/2026-07-16-phase-8f-agent-tool-projection-design.md`

**Tech stack:** .NET 10, incremental Roslyn generator targeting netstandard2.0,
System.Text.Json source generation, FrozenDictionary, xUnit, FluentAssertions,
NativeAOT linux-x64 publish-and-run.

## Global constraints

- Preserve one execution path: Agent Tool governance →
  `ICapabilityDispatcher.DispatchAsync(capturedDescriptor,
  InvocationSource.Agent, exactInput)` → Capability Pipeline → Handler.
- Never add an Agent→MCP, Metadata→Runtime, Metadata→Integrations, direct
  Handler, DynamicApi, AppService, ASP.NET, provider SDK, runtime scan,
  reflection JSON, or `Dictionary<string, object?>` fallback.
- Keep the Phase 7c Control Plane `AgentToolDescriptor` unchanged.
- Keep `CrestCreates.Agent.Runtime` as an empty future composition root.
- Unknown enum values fail closed. SelectionPolicy and CallOrigin remain
  separate. Title is in Agent Tool ContractHash.
- Logical invocation, attempt/lease, approval evidence, budget reservation, and
  invocation terminal state remain separate identities/state machines.
- No in-memory adapter may claim restart durability, cross-node atomicity, or
  distributed exactly-once.
- Preserve Phase 8e MCP canonical bytes/hashes, package/snapshot JSON, E2E, and
  NativeAOT behavior during shared-kernel extraction.
- Do not update `memory.md` to “implemented” until every exit gate passes.
- Never delete files directly; move obsolete files to `99_RecycleBin/`.

## Task ownership and integration rule

Workers may create files only in their task paths. They must not edit
`CrestCreates.slnx`, `solutions/CrestCreates.All.slnx`, central build files,
`memory.md`, or another worker's project file unless the task explicitly owns
it. The primary agent performs project/solution wiring after each round so
parallel work cannot overwrite shared XML.

## Task 0 — Shared Schema/JSON kernel extraction and MCP gate

**Ownership:**

- `src/Metadata/CrestCreates.Schema.Abstractions/`
- `src/Metadata/CrestCreates.Schema/`
- existing MCP projector/parity files under `src/Integrations/CrestCreates.Mcp/`
- focused Schema and MCP regression tests only

**Work:**

- [x] Extract protocol-neutral JSON Schema projection into Schema runtime.
- [x] Extract protocol-neutral Schema/JsonTypeInfo directional parity logic.
- [x] Leave MCP facades, errors, options, bindings, and result contracts MCP-owned.
- [x] Preserve the exact Phase 8e supported subset and rejection behavior.
- [x] Add byte-stable shared-kernel tests and keep MCP tests unchanged/green.
- [x] Do not migrate `McpCapabilityReference` in this task.

**Focused verification:** Schema tests and MCP projector/parity/runtime tests.

## Task 1 — Shared Capability projection reference compatibility

**Ownership:**

- `src/Metadata/CrestCreates.Metadata.Abstractions/DescriptorCapability/`
- `src/Metadata/CrestCreates.Metadata.Mcp.Abstractions/McpToolDescriptor.cs`
- MCP generator reference-emission files
- MCP canonical/package/snapshot/generator compatibility tests

**Work:**

- [x] Add `CapabilityProjectionReference` with the approved four-field shape.
- [x] Change MCP mainline property/generated output to the shared type.
- [x] Add a time-bounded obsolete source wrapper with implicit conversion.
- [x] Keep MCP runtime semantics: non-null ExpectedContractHash still rejected.
- [x] Freeze pre/post canonical bytes, ContractHash, DefinitionHash, generated
      descriptor semantics, and package/snapshot JSON.
- [x] If any compatibility vector cannot be retained, stop the migration and
      report; do not weaken or rewrite golden data.

**Focused verification:** Metadata canonical/package tests, MCP generator,
runtime, E2E, and existing AOT fixture test.

## Task 2 — Agent Tool metadata descriptor and generic governance

**Ownership:**

- new `src/Metadata/CrestCreates.Metadata.AgentTool.Abstractions/`
- DescriptorKind/name files
- Agent Tool canonical profiles under `src/Metadata/CrestCreates.Metadata/`
- Agent Tool metadata/hash/topology/package tests

**Work:**

- [x] Add `DescriptorKind.AgentTool = 9` and canonical name.
- [x] Add the independent `AgentCapabilityToolDescriptor` and safe enums.
- [x] Implement validation-independent canonical profiles with Title and every
      model-selection/governance field in ContractHash.
- [x] Add a strong Capability relationship extractor contract/integration.
- [x] Add stable-hash coverage and package/snapshot ref/kind/hash round trips.
- [x] Keep Agent Draft/Authoring/Control Plane mutation allowlists closed.

**Focused verification:** Metadata hash/package/topology tests and Agent
supported-kind policy tests.

## Task 3 — Agent execution context and Agent.Tools abstractions

**Ownership:**

- `src/Runtime/Agent/CrestCreates.Agent.Abstractions/` context files only
- new `src/Runtime/Agent/CrestCreates.Agent.Tools.Abstractions/`
- abstraction contract tests only

**Work:**

- [x] Add safe `AgentToolCallOrigin`, trusted `AgentExecutionContext`, accessor.
- [x] Add authoring attributes with safe explicit defaults.
- [x] Add catalog, discovery, invocation request/outcome, binding, and JSON
      contract-registration surfaces.
- [x] Add invocation gate/lease/fencing, approval, budget, and audit contracts.
- [x] Give every new state/decision enum an `Unknown = 0` fail-closed value.
- [x] Keep provider and infrastructure implementation types out of abstractions.

**Focused verification:** abstraction contract/default-value tests and build.

## Task 4 — Agent Tool source generator

**Ownership:**

- new `src/Tooling/CrestCreates.CodeGenerator/AgentToolGenerator/`
- Agent Tool generator tests under `tests/Tooling/CrestCreates.CodeGenerator.Tests/`

**Work:**

- [x] Implement incremental discovery and semantic validation for the approved
      top-level static partial container/direct nested class shape.
- [x] Emit descriptor provider, exact input binder, exact output serializer,
      and CLR JSON type registrations keyed by descriptor Id/version.
- [x] Map risk-floor values explicitly rather than numeric enum casts.
- [x] Implement ATP001–ATP016 and whole-container suppression on any Error.
- [x] Add source guards against forbidden dependencies/fallbacks.

**Focused verification:** all Agent Tool generator tests plus full generator suite.

## Task 5 — Runtime descriptor validation and immutable snapshot

**Ownership:**

- new runtime files under `src/Runtime/Agent/CrestCreates.Agent.Tools/` for
  registry, validation, JSON contracts, parity adapter, snapshot, and startup
- corresponding runtime tests, excluding invocation/governance tests

**Work:**

- [x] Implement lifecycle-aware descriptor validation and ATP101–ATP125 issues.
- [x] Resolve/capture Exact or Latest Capability once at startup.
- [x] Verify ExpectedContractHash and exact Schema references.
- [x] Resolve/freeze application-owned source-generated JsonTypeInfo only.
- [x] Reuse the shared Schema projection/parity kernel.
- [x] Derive effective side effect, risk, approval, and audit floors.
- [x] Publish Active-only immutable ToolName snapshot; never lazy-empty fallback.

**Focused verification:** registry/validator/JSON/parity/snapshot/startup tests.

## Task 6 — Provider-neutral discovery

**Ownership:**

- Agent Tool catalog/discovery runtime files
- discovery tests

**Work:**

- [x] Return only Active entries visible to trusted roles and current CallOrigin.
- [x] Exclude ExplicitOnly from automatic discovery.
- [x] Sort ToolName and roles with Ordinal semantics.
- [x] Include Title, description, schemas, contract identity, and effective
      governance summary without Handler or mutable registry objects.
- [x] Recheck roles/origin during invocation and collapse denial to UnknownTool.

**Focused verification:** complete selection matrix, role/no-oracle, ordering,
multi-context isolation tests.

## Task 7 — Canonical fingerprint and logical invocation gate

**Ownership:**

- Agent Tool fingerprint/canonical JSON files
- invocation-gate/in-memory development adapter files
- focused fingerprint/concurrency tests

**Work:**

- [x] Build `agent-tool-invocation-v1` canonical payload including CallOrigin.
- [x] Implement logical key → permanent fingerprint binding.
- [x] Implement attempt lease, monotonic fencing, renewal, atomic
      TryMarkDispatchStarted, Completed replay, and Indeterminate blocking.
- [x] Support Released pre-dispatch attempts without unbinding fingerprint.
- [x] Make stale lease transitions fail and same-lease transitions idempotent.
- [x] Clearly label the in-memory adapter dev/test or restart-risk single-node.

**Focused verification:** deterministic barrier/fake-clock concurrency matrix.

## Task 8 — Approval, budget, and governance audit orchestration

**Ownership:**

- Agent Tool approval/budget/audit runtime files
- explicit in-memory development adapters
- focused governance tests

**Work:**

- [x] Implement fail-closed effective approval with non-lowering floors.
- [x] Bind/claim EvidenceId to the same logical invocation/fingerprint and
      recheck expiry/revocation on a new attempt.
- [x] Implement reservation per AttemptId with Reserved → Released/Committed/
      Indeterminate and idempotent ReservationId finalization.
- [x] Permit a new reservation after a Released attempt; never reserve replay.
- [x] Implement required pre-audit and independent post-dispatch finalization.
- [x] Keep Budget and Invocation terminal states independent.

**Focused verification:** evidence replay, released-attempt retry, capacity,
settlement, audit failure, and malformed/unknown decision tests.

## Task 9 — Governed invoker and Dispatcher integration

**Ownership:**

- Agent Tool invoker, result mapper, context item names, idempotency builder
- invoker and Capability integration tests

**Work:**

- [x] Implement the exact 17-step order from the design.
- [x] Normalize/validate arguments before any governance side effect.
- [x] Run one lease-renewal loop owned by the invoker.
- [x] Call only the captured descriptor Dispatcher overload with
      `InvocationSource.Agent`, canonical InputJson, and stable logical key.
- [x] Serialize exact output, validate OutputSchema, and return safe outcomes.
- [x] Persist Completed only after known output, budget, required audit, and
      terminal state; otherwise persist/return Indeterminate.
- [x] Prove every pre-dispatch denial calls Dispatcher zero times.

**Focused verification:** invoker ordering, Capability middleware, output,
safe-error, replay, cancellation, and mixed terminal-state tests.

## Task 10 — DI, project/solution wiring, and boundary guards

**Primary-agent ownership:**

- all new/modified `.csproj`
- `CrestCreates.slnx`, `solutions/CrestCreates.All.slnx`
- DI composition files if shared by runtime tasks
- boundary tests

**Work:**

- [x] Add project references with the exact approved dependency direction.
- [x] Register eager startup build and require all governance adapters for
      Active tools; install no permissive production default.
- [x] Add projects to both canonical solutions.
- [x] Freeze forbidden runtime/generated/project dependencies.
- [x] Prove Control Plane CLR contract and allowlists remain unchanged.

**Focused verification:** solution build, boundary tests, startup DI tests.

## Task 11 — Generator-backed E2E and NativeAOT closure

**Ownership:**

- new `tests/Runtime/Agent/CrestCreates.Agent.Tools.E2E.Tests/`
- new `tests/Runtime/Agent/CrestCreates.Agent.Tools.AotFixture/`
- new `tests/Runtime/Agent/CrestCreates.Agent.Tools.AotFixture.Tests/`

**Work:**

- [x] Build a generated Query/Command host with application JSON context.
- [x] Cover discovery, approval, budget, audit, concurrency, replay,
      Released-attempt retry, Indeterminate blocking, and exact output failures.
- [x] Publish linux-x64 with `PublishAot`, complete native link, and execute the
      original native binary through discovery and terminal replay.
- [x] Reject Agent Tool path IL2026/IL3050 warnings.
- [x] Do not use issue #61 Generated CRUD contracts.

**Focused verification:** E2E suite and AOT fixture test.

## Task 12 — Documentation, memory, and final review

**Primary-agent ownership:**

- Agent Tool usage documentation
- `memory.md`
- plan checkboxes and Issue #60 acceptance evidence

**Work:**

- [x] Document the short application path and trusted Host responsibilities.
- [x] Document in-memory durability limitations and Indeterminate reconciliation.
- [x] Run focused suites, full solution tests, MCP regression/AOT, and Agent AOT.
- [x] Review every changed public API and canonical hash vector against the Spec.
- [x] Search generated/runtime code for forbidden dependencies/fallbacks.
- [x] Update memory to Implemented only after all executable gates pass.

## Review gates

After every worker task, the primary agent must inspect the full diff, reject
scope expansion, run the focused tests, and verify no unrelated user changes
were overwritten. Before final acceptance, review these invariants explicitly:

1. safe-zero enums and separate SelectionPolicy/CallOrigin;
2. immutable captured Capability/Schema/JsonTypeInfo mainline;
3. fingerprint includes CallOrigin and stable contract hashes;
4. one DispatchStarted per logical invocation with lease fencing;
5. Evidence replay is same logical invocation/fingerprint only;
6. Released reservation may retry with a new reservation;
7. Budget Committed + Invocation Indeterminate is representable;
8. Title is in ContractHash;
9. MCP compatibility vectors are byte/hash stable;
10. actual linux-x64 NativeAOT publish-link-run evidence exists.
