# Phase 10b — Business Construction Friction Review

Date: 2026-09-03
Baseline: Phase 10a Asset Management Golden Application (#85), merged as PR #89
Comparison sample: Procurement Approval Golden Sample (#65)
Promotion targets: #87 (AI-assisted evolution) and #88 (framework-gap decisions)

## Executive conclusion

The Asset application demonstrates that CrestCreates materially removed transport,
workflow-state-machine, durable-runtime, accountability, and source-generated JSON
infrastructure from ordinary business code. The remaining domain/application code is
not accidental: asset lifecycle transitions, assignment identity, maintenance policy,
organization visibility, deterministic ordering, and the choice of a business SQLite
authority are legitimate application responsibilities.

The review found a small set of two bounded, cross-domain usability candidates for
#88. NativeAOT outbox consumer activation still requires a host-written concrete
factory even though the durable delivery registry already owns the consumer contract
metadata. Contract/host JSON root composition also repeats safe resolver wiring in
both samples. Both candidates have bounded owners and pre-implementation failing
acceptance cases; neither is a new #86 framework implementation.

The review found no new capability gap. The three production changes exposed by #85
were legitimate framework-owned corrections and are closed with business acceptance
evidence. Descriptor/projection/JSON verbosity is either intentional authority, an
explicit NativeAOT constraint, or a discoverability issue. Those observations are
sent to #87 or deferred; they are not promoted to #88 merely because they require
several declarations.

## Evidence baseline

The frozen #85 construction record is the source inventory. Every row is represented
by one `F` entry below, while the three framework corrections are recorded as `I`
incidents. Evidence links point to production sample code, tests, or the merged #85
record; reviewer interpretation and follow-up disposition are kept separate from
those observed facts.

### Frozen #85 field coverage

| #85 field | Review entry | Evidence |
| --- | --- | --- |
| Application files/code | F01 | `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Domain/Entities/Asset.cs` |
| Descriptors | F02 | `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Host/AssetDescriptorCatalog.cs` |
| Capabilities/handlers | F03 | `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Application/Handlers/AssetHandlers.cs` |
| Manual registration | F04 | `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Host/Program.cs` |
| Projection-specific code | F05 | `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Contracts/McpTools/AssetMcpTools.cs` |
| Permission/DataPermission wiring | F06 | `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Application/AssetApplicationService.cs` |
| Persistence-specific code | F07 | `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Persistence/SqliteAssetStore.cs` |
| Serialization-specific code | F08 | `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Host/Json/AssetHostJsonContext.cs` |
| Framework glue | F09 | `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Host/AssetMaintenanceWorkflowService.cs` |
| Workarounds | F10 | `samples/AssetManagement/README.md` |
| Framework modification? | F11 | `memory.md` (Issue #85 entry) |

The production path reviewed is:

```text
Asset domain policy
  -> Capability and generated Minimal API
  -> Permission / DataPermission
  -> SQLite business authority
  -> Workflow / HumanTask for maintenance
  -> PostgreSQL Runtime / Outbox
  -> Accountability
  -> MCP / Agent read projections
  -> source-generated JSON
  -> NativeAOT publish-link-run
```

This review does not count lines, files, test-only shortcuts, or the existence of two
databases as usability scores. A construction step is classified only after its
business requirement and semantic owner are identified.

## Requirement-by-requirement friction map

Each entry has exactly one primary classification and one disposition. `Cross-domain
repeated?` means the same semantic obligation, not merely similar-looking code.

### F01 — Asset lifecycle and business application code

- Business requirement: Register, update, assign, return, transfer, and maintain an asset while preserving lifecycle invariants.
- Observed construction work: The application defines the aggregate, assignment record, maintenance record, validation, deterministic ordering, and DTO projection in Domain/Application code.
- Evidence: `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Domain/Entities/Asset.cs`; `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Application/AssetApplicationService.cs`; `samples/AssetManagement/tests/CrestCreates.Sample.AssetManagement.Tests/AssetDesignCaseTests.cs`
- Semantic owner: Asset domain and Asset application policy.
- Primary classification: Business-specific complexity.
- Cross-domain repeated?: No; Procurement has a different aggregate lifecycle and approval policy.
- Human discoverability: High once the sample is opened; the aggregate methods make policy visible.
- Agent discoverability: Medium; an agent must inspect the aggregate before changing status or assignment behavior.
- Workaround?: No.
- Acceptance case: `AssignedMaintenance_ApproveAndReject_PreserveAssignmentInvariant` and `AssignedTransfer_IsRejectedBeforeOwnershipCanDrift` must remain green.
- Disposition: Keep.
- Rationale: An ideal framework cannot choose Asset assignment identity, transfer eligibility, or maintenance-state preservation without taking application authority.

### F02 — Schema, Capability, Workflow, HumanTask, and Form descriptors

- Business requirement: Expose typed Asset commands/queries and a maintenance review interaction with stable cross-descriptor references.
- Observed construction work: The host declares schemas, capabilities, the form, HumanTask outcomes, and the Workflow step in one catalog.
- Evidence: `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Host/AssetDescriptorCatalog.cs`; `samples/ProcurementApproval/src/CrestCreates.Sample.Procurement.Host/ProcurementDescriptorCatalog.cs`
- Semantic owner: Metadata contracts plus the Asset/Procurement applications for business names, fields, outcomes, and references.
- Primary classification: Business-specific complexity.
- Cross-domain repeated?: Yes; both samples declare the same descriptor categories and versioned Workflow-to-HumanTask-to-Form relationship.
- Human discoverability: Medium; the canonical catalog exists, but its relationship is learned by following references.
- Agent discoverability: Low-to-medium; an agent needs repository search to learn that descriptor IDs are the stable join keys.
- Workaround?: No.
- Acceptance case: Descriptor lookup must resolve the Asset maintenance Form, HumanTask, Workflow, and all Capability schema references without reflection.
- Disposition: #87.
- Rationale: The declarations carry different semantic contracts, so generation would not remove the business choices. The discoverability path is suitable for an AI-assisted evolution scenario.

### F03 — Capability and handler binding

- Business requirement: Run Asset operations through one authorized business invocation path regardless of transport.
- Observed construction work: The application declares Capability metadata, implements handlers, and registers the handler module; handlers delegate to `AssetApplicationService`.
- Evidence: `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Application/Handlers/AssetCapabilityModule.cs`; `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Application/Handlers/AssetHandlers.cs`; `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Host/Program.cs`
- Semantic owner: Capability runtime for dispatch; Asset Application for business action and error mapping.
- Primary classification: Framework usability friction.
- Cross-domain repeated?: Yes; Procurement also declares handler bindings and routes HTTP/MCP/Agent actions into the Capability path.
- Human discoverability: Medium; the module, registry, and service registration are separate files.
- Agent discoverability: Medium; generated metadata identifies the path, but the host registry is manual.
- Workaround?: No; the path is canonical and semantically correct.
- Acceptance case: Asset HTTP operations and read projections must reach the same Capability handler and preserve authorization/accountability outcomes.
- Disposition: #87.
- Rationale: Capability already removes transport-specific business dispatch. The remaining cost is locating the binding and is therefore a discoverability/usability observation, not a missing semantic.

### F04 — Host registration and infrastructure composition

- Business requirement: Run the Asset host with the chosen Runtime provider, registries, endpoints, identity, authorization, delivery, and serialization composition.
- Observed construction work: `Program.cs` explicitly registers generated registries, PostgreSQL Runtime persistence, delivery, permission grants, the consumer, endpoint projections, and JSON resolvers.
- Evidence: `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Host/Program.cs`; `samples/ProcurementApproval/src/CrestCreates.Sample.Procurement.Host/Program.cs`
- Semantic owner: Host/application for infrastructure choices; each framework subsystem for its own registration contract.
- Primary classification: Framework usability friction.
- Cross-domain repeated?: Yes; both hosts explicitly compose Runtime, registries, permissions, consumer activation, and JSON options.
- Human discoverability: Medium; the correct order and split between generated and manual registration require reading the host startup.
- Agent discoverability: Low-to-medium; an agent can find registrations but cannot infer all ordering constraints from a single declaration.
- Workaround?: No; explicit provider and security choices must remain visible.
- Acceptance case: A fresh Asset host must start with the selected PostgreSQL Runtime provider and no in-memory fallback, then execute the golden scenario.
- Disposition: #88.
- Rationale: This is a bounded usability candidate only where repeated registration is framework-owned and order-sensitive. Provider choice, credentials, and permission grant policy remain explicit application composition.

### F05 — HTTP, MCP, and Agent projections

- Business requirement: Offer Asset reads over generated HTTP plus read-only MCP and Agent tools without creating independent business behavior.
- Observed construction work: HTTP endpoint metadata and MCP/Agent tool attributes are declared separately; MCP and Agent invocation enters the shared `asset-management.assets.get` Capability.
- Evidence: `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Host/Endpoints/AssetEndpoints.cs`; `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Contracts/McpTools/AssetMcpTools.cs`; `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Contracts/AgentTools/AssetAgentTools.cs`; `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Host/Projections/AssetCompatibilityProjection.cs`
- Semantic owner: Projection contracts for transport/tool policy; Capability/Application for business semantics.
- Primary classification: Business-specific complexity.
- Cross-domain repeated?: Yes; Asset and Procurement both reuse a read Capability for MCP/Agent projection while keeping projection metadata separate.
- Human discoverability: Medium; shared capability identity is explicit, but adapter files are transport-specific.
- Agent discoverability: High for the tool surface because tool metadata is source-declared; medium for choosing Compatibility versus MCP/Agent.
- Workaround?: No.
- Acceptance case: HTTP, MCP, and Agent reads must return the same tenant/organization-filtered Asset result and must not create a second write path.
- Disposition: Keep.
- Rationale: Tool selection, side-effect, approval, and compatibility policies are distinct contracts. Merging adapters would hide authority rather than remove business construction.

### F06 — Permission, DataPermission, and organization visibility

- Business requirement: Restrict Asset commands by operation permission and restrict reads by tenant plus the application-chosen organization scope.
- Observed construction work: The application declares permissions, composes the framework authorization chain, seeds sample grants, and applies a fail-closed organization guard before querying.
- Evidence: `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Contracts/AssetPermissions.cs`; `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Application/AssetApplicationService.cs`; `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Host/AssetPermissionGrantRepository.cs`; `samples/AssetManagement/tests/CrestCreates.Sample.AssetManagement.Tests/AssetDesignCaseTests.cs`
- Semantic owner: Framework Permission/DataPermission authority plus Asset application visibility policy.
- Primary classification: Business-specific complexity.
- Cross-domain repeated?: Yes; both samples explicitly choose permissions and tenant context, while Asset additionally chooses organization visibility.
- Human discoverability: Medium; the framework chain is correct but sample grant seeding is separate from runtime checks.
- Agent discoverability: Medium; permission IDs are visible, but the fail-closed organization shape requires reading the application service.
- Workaround?: No; sample-owned grant storage is a testable adapter, not a bypass.
- Acceptance case: Organization scope with neither `OrganizationId` nor plural IDs returns no Asset rows; unauthorized mutation is rejected.
- Disposition: Keep.
- Rationale: The framework owns enforcement; the application owns whether Asset visibility is tenant-wide or organization-scoped. Grant seeding is sample/test composition, not a product-runtime gap.

### F07 — Business SQLite and durable Runtime PostgreSQL authorities

- Business requirement: Persist business Asset data transactionally while using the production durable Runtime provider for Workflow, HumanTask, and Outbox state.
- Observed construction work: The sample implements a SQLite business store and explicitly composes PostgreSQL Runtime persistence, including schema/migration and delivery settings.
- Evidence: `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Persistence/SqliteAssetStore.cs`; `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Host/Program.cs`; `samples/AssetManagement/tests/CrestCreates.Sample.AssetManagement.E2E.Tests/GoldenScenarioProcessTests.cs`
- Semantic owner: Asset business store for Asset data; Runtime Persistence for Runtime state and Outbox.
- Primary classification: Business-specific complexity.
- Cross-domain repeated?: No; Procurement currently uses a different business persistence shape and its sample scope does not prove that two authorities are universally required.
- Human discoverability: High for the explicit boundary; medium for the cross-authority failure path.
- Agent discoverability: Medium; the ownership boundary is explicit but the legal abort path must be found in Runtime contracts.
- Workaround?: No; two authorities are an intentional deployment/application choice.
- Acceptance case: A business-store failure after Workflow suspension must leave no live Runtime lease; normal completion must persist both business and Runtime outcomes.
- Disposition: Keep.
- Rationale: Two stores are not a defect by themselves. The review preserves the explicit provider choice and evaluates only the legal cross-authority compensation contract in incident I01.

### F08 — Source-generated JSON and NativeAOT roots

- Business requirement: Serialize Asset contracts, host responses, MCP/Agent payloads, and durable Runtime facts without runtime reflection.
- Observed construction work: Contracts and host responses have separate source-generated contexts; the host composes a combined resolver and an explicit AOT consumer factory.
- Evidence: `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Contracts/Json/AssetJsonContext.cs`; `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Host/Json/AssetHostJsonContext.cs`; `samples/AssetManagement/tests/CrestCreates.Sample.AssetManagement.AotFixture.Tests/AssetAotFixtureTests.cs`
- Semantic owner: Json contracts for payload roots; host for host-only response roots; NativeAOT host for explicit activation.
- Primary classification: Framework usability friction.
- Cross-domain repeated?: Yes; Asset and Procurement both have contract/host contexts and combined resolvers.
- Human discoverability: Medium; source-generated roots are explicit but split across Contracts and Host.
- Agent discoverability: Low-to-medium; an agent must identify the correct context and resolver before adding a payload.
- Workaround?: Explicit NativeAOT consumer factory is required by the current activation surface.
- Acceptance case: A clean linux-x64 publish-link-run must execute the Asset golden scenario with reflection disabled and all required payload roots available.
- Disposition: #88.
- Rationale: Source generation and explicit roots are legitimate NativeAOT constraints. The repeated activation/context composition is a bounded usability candidate only where the framework can own the safe default without hiding application payload authority.

### F09 — Workflow, HumanTask, continuation, and accountability composition

- Business requirement: Maintenance approval suspends on a HumanTask, resumes through durable completion, applies the decision once, and records responsibility.
- Observed construction work: The application starts the Workflow, validates the suspended lease, completes the HumanTask with a durable fact, waits for the business result, and composes the required Outbox consumer.
- Evidence: `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Host/AssetMaintenanceWorkflowService.cs`; `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Host/Program.cs`; `samples/AssetManagement/tests/CrestCreates.Sample.AssetManagement.Tests/AssetAcceptanceTests.cs`
- Semantic owner: Workflow/HumanTask/Outbox/Accountability runtime for lifecycle and delivery; Asset application for maintenance decision meaning.
- Primary classification: Business-specific complexity.
- Cross-domain repeated?: Yes; Procurement uses the same Workflow → HumanTask → durable completion → Capability continuation shape.
- Human discoverability: Medium; the canonical lifecycle is available but spans several runtime abstractions.
- Agent discoverability: Low-to-medium; required consumer identity, durable fact, and completion path are not inferable from the business method alone.
- Workaround?: No; the orchestration is a real business requirement, not duplicate application state-machine code.
- Acceptance case: Maintenance completion reaches Available/Assigned, preserves assignment state, and emits the expected accountability evidence.
- Disposition: Keep.
- Rationale: CrestCreates removed the durable state machine and delivery mechanics; the application still has to express when maintenance starts, who requested it, and what business outcome is accepted.

### F10 — Cross-authority failure and AOT activation workarounds

- Business requirement: Preserve business and Runtime invariants when construction crosses a business store, durable Runtime, Outbox, and NativeAOT activation boundary.
- Observed construction work: The sample calls the canonical Workflow abort authority after a business-store failure, uses stable operation receipts for replay, and supplies a concrete consumer factory for NativeAOT.
- Evidence: `samples/AssetManagement/README.md`; `samples/AssetManagement/src/CrestCreates.Sample.AssetManagement.Host/AssetMaintenanceWorkflowService.cs`; `src/Runtime/Workflow/CrestCreates.Workflow/WorkflowAbortService.cs`; `tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/PostgreSqlRuntimeIntegrationTests.cs`
- Semantic owner: Runtime Workflow/Delivery for compensation and activation contracts; Asset host for the explicit composition choice.
- Primary classification: Framework usability friction.
- Cross-domain repeated?: Yes for explicit AOT consumer activation; no for Asset's particular SQLite-to-PostgreSQL failure trigger.
- Human discoverability: Medium; the legal abort owner is now canonical, but it was discovered through a failing business case.
- Agent discoverability: Low; an agent could easily attempt direct Workflow/HumanTask mutation without the closed contract evidence.
- Workaround?: Yes; the factory and cross-authority receipt are explicit boundary composition.
- Acceptance case: A failed Asset save after suspension must atomically cancel the HumanTask, fail the Workflow, emit `workflow.failed`, and replay the same operation receipt without a second mutation.
- Disposition: Defer.
- Rationale: The compensation semantics are closed in #85 and must not be reopened. AOT activation is already captured by candidate C88-01; the Asset-specific failure trigger has no second-domain capability gap.

### F11 — Production framework corrections exposed by #85

- Business requirement: Make the real Asset golden scenario correct at Runtime boundaries, including abort, Outbox metadata integrity, and Agent completion confirmation.
- Observed construction work: Three failing business cases required production framework contract changes; the Asset sample then consumed those canonical contracts.
- Evidence: `memory.md` (Issue #85 entry); `samples/AssetManagement/tests/CrestCreates.Sample.AssetManagement.Tests/AssetDesignCaseTests.cs`; `tests/Runtime/Agent/CrestCreates.Agent.Tools.Tests/Invocation/AgentToolInvokerTests.cs`; `tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/PostgreSqlRuntimeIntegrationTests.cs`
- Semantic owner: Workflow Runtime, Outbox canonical writer, and Agent confirmation contract respectively.
- Primary classification: Framework capability gap.
- Cross-domain repeated?: Partly; the corrected contracts are reusable, but the triggering Asset failure was not generalized from occurrence count alone.
- Human discoverability: High after closure evidence is documented; before the fix, the missing owner was visible only through a failing business path.
- Agent discoverability: Medium after closure; stable owners and tests exist, but the decision rule must be learned from the incident record.
- Workaround?: No valid application workaround; direct Runtime mutation or Asset-specific JSON comparison would violate canonical ownership.
- Acceptance case: `BusinessStoreFailureAfterWorkflowStart_AbortsRuntimeLease`; PostgreSQL 100ns-tail Outbox round-trip hash verification; Agent semantic JSON object-reorder acceptance with value/array-reorder rejection.
- Disposition: Keep.
- Rationale: These are retrospective examples of legitimate framework ownership. They are closed during #85, are not promoted as new #88 work, and must not be reopened without a downstream contract failure.

## Repeated glue inventory

| Concern | Asset evidence | Procurement evidence | Same semantic obligation? | Ownership decision |
| --- | --- | --- | --- | --- |
| Schema / Capability / Workflow / HumanTask / Form | `AssetDescriptorCatalog.cs` | `ProcurementDescriptorCatalog.cs` | Yes | Different declarations remain because fields, outcomes, and policy differ; discoverability goes to #87. |
| Capability handler binding | `AssetCapabilityModule.cs` + `AssetHandlers.cs` | `Procurement.Application/Handlers/*` | Yes | Shared Capability owner; host/module composition is a bounded #87/#88 observation. |
| HTTP / MCP / Agent projections | Asset endpoint + tool attributes | Procurement endpoint + tool attributes | Yes | Invocation semantics are shared; projection metadata and policy stay explicit. |
| Serialization roots / resolver | `AssetJsonContext.cs` + `AssetHostJsonContext.cs` | `ProcurementJsonContext.cs` + `ProcurementHostJsonContext.cs` | Yes | NativeAOT constraint is intentional; repeated safe composition is C88-02 candidate material. |
| Host DI / composition | Asset `Program.cs` | Procurement `Program.cs` | Yes | Provider, credentials, permissions, and business adapters remain application choices; repeated framework registration is C88-01/C88-02 evidence. |
| Durable provider composition | PostgreSQL Runtime + SQLite business store | Runtime provider + Procurement business store | No | Different authority shapes are not a defect; do not generalize two-store composition. |
| NativeAOT activation | Explicit Asset consumer factory | Explicit Procurement consumer factory | Yes | Runtime Delivery should be evaluated by #88 for a safe generated activation path. |
| Accountability | Asset host sinks and Runtime delivery | Procurement host sinks and Runtime delivery | Yes | Shared responsibility boundary is reused; business facts remain application-specific. |

Repetition is evidence, not ownership. A declaration is not duplicate glue when it
owns a distinct contract or keeps an important provider, projection, security, or
authority choice visible.

## Asset vs Procurement comparison

The comparison above uses semantic obligations rather than LOC. Asset is stronger
evidence for organization visibility, assigned-maintenance preservation, and a
business-store failure after Runtime suspension. Procurement is stronger evidence for
approval decision replay and a different business persistence/host shape. Neither
sample proves that its weaker case is a general framework requirement.

The shared conclusions are:

- Capability is the shared invocation authority for business actions and read tools.
- Workflow/HumanTask composition removes duplicated state-machine implementation, but
  each application still owns its decision fact and business transition.
- HTTP, MCP, and Agent metadata are intentionally projection-specific while their
  selected read Capability is shared.
- Permission enforcement and tenant context are framework-owned; visibility policy and
  grant seeding remain application/sample choices.
- Durable Runtime and Accountability composition are reusable platform boundaries;
  business persistence remains an explicit application/provider choice.
- Source-generated JSON and NativeAOT roots are real constraints, with repeated safe
  composition captured as bounded #88 input rather than a blanket “framework defect”.

## Human discoverability findings

Human discoverability is adequate for an experienced maintainer but not short-path:
Human discoverability differs from Agent discoverability because a maintainer can
follow code ownership manually while an agent needs those ownership joins stated in
the change context.

- H01: The canonical descriptor path is `AssetDescriptorCatalog`, but the relation
  between Schema, Capability, Form, HumanTask, and Workflow is learned by following
  versioned IDs across files. Send to #87 as an evolution scenario.
- H02: The legal Workflow abort owner and the durable completion consumer contract are
  discoverable after reading Runtime tests and the #85 closure, but not from the
  Asset application service alone. Keep the closed incident and defer new framework
  work unless a new failing business case appears.
- H03: Contract versus Host JSON contexts are correct but require repository
  archaeology. Send to #87; candidate C88-02 is limited to repeated safe composition,
  not the removal of application payload roots.

## Agent discoverability findings

Agent discoverability differs from human discoverability:

- A01: An agent can see the generated Capability/tool metadata, but choosing the
  canonical shared Capability instead of adding a projection-specific business path
  requires an explicit evolution instruction. Send to #87.
- A02: An agent is likely to confuse application-owned policy with framework-owned
  Workflow, DataPermission, or Outbox state unless semantic owners are stated near the
  change. Send to #87 as a bounded authoring/review scenario.
- A03: The explicit AOT consumer factory is easy to miss and appears unrelated to the
  business requirement. Record as #88 candidate C88-01 only because Asset and
  Procurement provide the same semantic repetition.

## Closed-during-#85 incident review

### I01 — Canonical Workflow abort authority

- Business acceptance case: A maintenance request suspends Workflow/HumanTask, then the business store fails; no live Runtime lease may remain.
- Observed failure: Application code had a business-store failure after Runtime suspension but no legal canonical way to transition `Suspended -> Failed`, cancel the HumanTask, and emit normal failure accountability.
- Original owner: Workflow Runtime transaction kernel.
- Why application workaround was invalid: Direct mutation of Workflow/HumanTask state would bypass Runtime lifecycle, transaction, audit, and replay ownership.
- Framework contract added/fixed: `IWorkflowAbortService` atomically performs the abort, HumanTask cancellation, and `workflow.failed` accountability fact; stable operation receipts classify replay.
- Tests proving the fix: Asset `BusinessStoreFailureAfterWorkflowStart_AbortsRuntimeLease`; PostgreSQL Workflow abort commit/rollback tests in `PostgreSqlRuntimeIntegrationTests.cs`.
- Whether the lesson generalizes: Yes, the abort contract is reusable for cross-authority Runtime compensation; the Asset SQLite trigger is not itself a generic feature request.

### I02 — Outbox producer timestamp precision normalization

- Business acceptance case: An Outbox message containing a producer timestamp with a 100ns tail must round-trip through PostgreSQL and preserve the canonical v1 integrity hash.
- Observed failure: Provider timestamp precision could differ from the contract precision, making persistence/recomputation disagree even though business metadata was otherwise valid.
- Original owner: Provider-neutral Outbox canonical writer and PostgreSQL persistence boundary.
- Why application workaround was invalid: An Asset-specific timestamp adjustment would duplicate the canonical hash contract and leave other producers/provider paths inconsistent.
- Framework contract added/fixed: Normalize producer metadata to contract microsecond precision before the unchanged v1 hash and persistence.
- Tests proving the fix: InMemory rejection of manually constructed high-precision messages and PostgreSQL 100ns-tail round-trip/hash verification.
- Whether the lesson generalizes: Yes, canonical contract normalization belongs to the Outbox owner; no new #88 candidate remains after closure.

### I03 — Agent completion semantic JSON comparison

- Business acceptance case: Equivalent completion JSON with object-property reordering is accepted, while changed values or array order are rejected.
- Observed failure: Byte/string comparison treated semantically equivalent JSON as different and would have encouraged an Asset-specific comparison workaround.
- Original owner: Agent completion confirmation contract.
- Why application workaround was invalid: The semantic equality rule applies to every Agent completion payload and cannot be owned by Asset business code.
- Framework contract added/fixed: Completion confirmation compares structured JSON semantics with the required object/array/value boundaries.
- Tests proving the fix: `AgentToolPreDispatchFinalizerSemanticEqualityTests` and the Agent invocation completion contract tests.
- Whether the lesson generalizes: Yes, semantic JSON equality is a reusable confirmation contract; it is closed and not a new #88 item.

These three incidents are decision-rule exemplars, not reopened backlog. Phase 9
durability, transaction, Outbox, cache, and Accountability semantics remain frozen
unless a downstream contract failure demonstrates otherwise.

## Promote / Reject / Defer decisions

### #88 candidates

#### C88-01 — Safe NativeAOT Outbox consumer activation

- Legitimate reusable business requirement: A business golden application with a durable Outbox consumer must publish and run under NativeAOT.
- Correct framework semantic owner: Runtime Delivery consumer activation/registration.
- Current unnecessary cost: Asset and Procurement both register delivery metadata and then repeat a host-written concrete constructor factory to avoid runtime discovery.
- Evidence: Asset `Program.cs` and Procurement `Program.cs`, plus both NativeAOT fixtures.
- Bounded proposed responsibility: Provide a generated or otherwise AOT-safe activation path for registered consumers while keeping constructor dependencies and provider choices explicit.
- Pre-implementation failing acceptance case: Remove the host factory from both sample hosts, retain canonical required-consumer registration, publish-link-run each sample, and assert durable completion reaches its business terminal state. The current contract cannot pass this case without a factory.
- Classification: Framework usability friction.
- Disposition: #88.

#### C88-02 — Safe composition of contract and host JSON roots

- Legitimate reusable business requirement: A NativeAOT host must expose application contracts and host-only response types through one source-generated resolver.
- Correct framework semantic owner: JSON contract/build composition boundary.
- Current unnecessary cost: Asset and Procurement each repeat a contract context, host context, and combined resolver setup.
- Evidence: Asset and Procurement `*JsonContext.cs` / `*HostJsonContext.cs` files.
- Bounded proposed responsibility: Evaluate a compile-time composition aid that does not hide application-owned roots or provider/runtime payload ownership.
- Pre-implementation failing acceptance case: A host containing one contract context and one host response context, with no handwritten resolver glue, must publish-link-run and serialize both roots under reflection-disabled options. Current samples require handwritten composition.
- Classification: Framework usability friction.
- Disposition: #88.

These are intentionally small usability candidates, not capability-gap claims. No new
capability-gap candidate is promoted: the only proven capability gap in the baseline
is I01, already closed in #85.

### #87 input list

| ID | Evolution scenario | Why it belongs to #87 |
| --- | --- | --- |
| 87-01 | Add a new Asset maintenance outcome while preserving Form/HumanTask/Workflow references. | Tests descriptor discovery and stable cross-descriptor authoring. |
| 87-02 | Expose an existing Asset read Capability through a new agent-facing projection. | Tests finding and reusing the canonical invocation path. |
| 87-03 | Change organization visibility policy and obtain a reviewable activation proposal. | Tests separating application policy from framework authorization/DataPermission. |
| 87-04 | Add a new durable completion payload under NativeAOT. | Tests finding contract roots, resolver composition, and required consumer identity. |

#87 must exercise these as business evolution scenarios; it must not implement a
framework cleanup merely because an agent encounters an authoring obstacle.

### Keep

- Asset lifecycle, assignment/return/transfer eligibility, maintenance requester and deterministic ordering.
- Organization visibility policy and explicit permission grant seeding.
- Business SQLite versus Runtime PostgreSQL authority choice.
- Projection-specific tool policy and business decision facts.
- Workflow/HumanTask business orchestration around the canonical Runtime lifecycle.

### Reject

- Generate all Asset descriptors into one opaque declaration: fields, outcomes, visibility, and references carry different authority.
- Merge HTTP, MCP, Agent, and Compatibility adapters: their side-effect, approval, serialization, and compatibility contracts differ.
- Add a generic helper solely because `Program.cs` is long: the provider, credential, security, and AOT choices should remain visible.
- Score framework usability using LOC, file count, or test-fixture setup.

### Defer

- Unifying Asset SQLite and Procurement/business persistence behind a new abstraction: there is no same-semantic provider requirement and it would hide authority.
- Reopening the #85 Workflow abort, Outbox precision, or Agent JSON semantics without a new downstream failing acceptance case.
- Treating one sample-specific cross-authority failure trigger as a generic framework capability gap.

## Remaining uncertainty

- C88-01 and C88-02 are decision candidates, not approved framework changes. #88 must validate whether the repeated cost is large enough to justify generated composition without obscuring ownership.
- The Asset and Procurement samples establish two-domain repetition for several concerns, but they do not establish a third-domain frequency threshold.
- Human/Agent discoverability is inferred from repository navigation and review intervention evidence, not from a benchmark. #87 should test these observations through realistic change tasks.
- The review does not claim distributed exactly-once behavior, generic cross-database atomicity, or automatic provider selection.

## Closure statement

| Exit criterion | Result |
| --- | --- |
| Every major #85 friction field classified | Pass — F01 through F11 cover the frozen record. |
| Evidence and semantic owner recorded | Pass — every F entry has both fields. |
| Asset vs Procurement comparison complete | Pass — all eight minimum shared dimensions are compared. |
| Human and Agent discoverability separated | Pass — separate findings are recorded. |
| Three #85 framework corrections traced | Pass — I01 through I03 include acceptance, failure, owner, fix, and tests. |
| No application policy promoted as framework work | Pass — policy entries are Keep; #88 candidates are bounded composition concerns. |
| No Phase 9 reopening | Pass — incidents are closed exemplars only. |
| #87 receives bounded evolution inputs | Pass — 87-01 through 87-04. |
| #88 receives a small evidence-backed set | Pass — C88-01 and C88-02 only. |
| #88 candidates have failing acceptance skeletons | Pass — both candidates include pre-implementation cases. |
| Rejected/deferred candidates have reasons | Pass — explicit Reject and Defer sections. |
| Review closure tests are green | Verified by `Phase10bBusinessConstructionFrictionReviewTests`. |
| No production Runtime feature implemented in #86 | Pass — this change adds only review documentation, contract tests, and CI wiring. |
