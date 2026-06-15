# Sample — Descriptor Control Plane Golden Scenario: Implementation Plan

> **Date:** 2026-06-15 | **Status:** Draft | **Parent Issue:** [#28](https://github.com/OrchesAdam/CrestCreates/issues/28) | **Spec:** `docs/superpowers/specs/2026-06-15-sample-descriptor-control-plane-golden-scenario-design.md`

---

## Task 1 — Create Sample Project Skeleton

**Goal:** Add the sample project without runtime behavior.

Files:

```text
samples/CrestCreates.Samples.DescriptorControlPlane/
  CrestCreates.Samples.DescriptorControlPlane.csproj
  README.md
```

Requirements:

- Target the repo default `net10.0`.
- Reference only existing framework projects required by later tasks.
- Do not add to production modules.
- README states this is a golden scenario, not a product module.

Verification:

```bash
dotnet build samples/CrestCreates.Samples.DescriptorControlPlane
```

---

## Task 2 — Create Sample Test Project Skeleton

**Goal:** Add a focused test project for golden scenario tests.

Files:

```text
framework/test/CrestCreates.Samples.Tests/
  CrestCreates.Samples.Tests.csproj
```

Requirements:

- Reference the sample project.
- Add xUnit / FluentAssertions using central package versions.
- Add the project to `CrestCreates.slnx`.
- No test logic yet.

Verification:

```bash
dotnet test framework/test/CrestCreates.Samples.Tests
```

---

## Task 3 — Build Company Certification Descriptor Inventory

**Goal:** Add strongly typed baseline descriptors.

Files:

```text
CompanyCertificationDescriptors.cs
CompanyCertificationDescriptorInventory.cs
```

Requirements:

- Include all Schema/Form/Capability/HumanTask/Workflow/Event descriptors from the Spec.
- Use internal `Id` values that follow current ref-validation prefixes (`schema_`, `form_`, `cap_`, `ht_`, `wf_`, `evt_`) and keep issue/spec names in `Name`.
- Model both HumanTask outcomes:
  - `Approve -> ApproveCompanyCertification`
  - `Reject -> RejectCompanyCertification`
- Use `CapabilityDescriptor.Permissions`, not `RequiredPermission`.
- Include event payload schemas for approved/rejected events.
- Keep hash values deterministic strings for now; do not introduce reflection hashing.

Verification:

```bash
dotnet build samples/CrestCreates.Samples.DescriptorControlPlane
```

---

## Task 4 — Add Change Scenario Factory

**Goal:** Add prepared before/after inventories for control-plane tests.

Files:

```text
CompanyCertificationChangeScenarios.cs
```

Scenarios:

- Baseline
- Optional field addition
- Required field removal
- Permission change
- Missing workflow target
- Unsupported subworkflow

Requirements:

- Each scenario returns `before` and `after` inventories.
- Mutations must preserve the rest of the inventory so impact traversal can see consumers.
- Missing target should only break the intended workflow reference.

Verification:

```bash
dotnet build samples/CrestCreates.Samples.DescriptorControlPlane
```

---

## Task 5 — Add Control-Plane Runner Core

**Goal:** Run Phase 6a-6f over a selected scenario.

Files:

```text
CompanyCertificationControlPlaneRunner.cs
CompanyCertificationControlPlaneReport.cs
```

Requirements:

- Compose:
  - `IDescriptorTopologyBuilder`
  - `IDescriptorChangeSetBuilder`
  - `IDescriptorImpactAnalyzer`
  - `IDescriptorCompatibilityAnalyzer`
  - `IDescriptorLifecycleGovernanceService`
  - `IDescriptorPackageBuilder`
- Return topology, impact, compatibility, governance, and package facts.
- Activation operation must be `DescriptorLifecycleOperation.Activate`.
- Package report must expose `ContentHash`, `EvidenceHash`, and `EnvelopeHash`.

Verification:

```bash
dotnet build samples/CrestCreates.Samples.DescriptorControlPlane
```

---

## Task 6 — Add Control-Plane Tests 1-3

**Goal:** Establish first regression slice before runtime work.

Files:

```text
CompanyCertificationControlPlaneTests.cs
```

Tests:

```text
Baseline_Should_Build_Healthy_Topology
Removing_Required_Schema_Field_Should_Be_Breaking_And_ReviewRequired
Missing_Workflow_Target_Should_Block_Activation
```

Verification:

```bash
dotnet test framework/test/CrestCreates.Samples.Tests --filter CompanyCertificationControlPlaneTests
```

---

## Task 7 — Add Control-Plane Tests 4-7

**Goal:** Complete descriptor governance coverage.

Tests:

```text
Optional_Field_Addition_Should_Be_Compatible
Permission_Change_Should_Be_SecuritySensitive
Unsupported_SubWorkflow_Should_Surface_Warning
Package_Should_Include_Manifest_Snapshot_Evidence_And_StableHash
```

Requirements:

- Stable hash test must build the same package twice and compare hashes.

Verification:

```bash
dotnet test framework/test/CrestCreates.Samples.Tests --filter CompanyCertificationControlPlaneTests
```

---

## Task 8 — Add Sample Runtime Store and Events

**Goal:** Add sample-owned in-memory business state.

Files:

```text
CompanyCertificationRuntimeModels.cs
InMemoryCompanyCertificationStore.cs
CompanyCertificationEvents.cs
```

Requirements:

- Store only sample runtime state.
- Do not add framework persistence abstractions.
- Add approved/rejected event types only as sample events.

Verification:

```bash
dotnet build samples/CrestCreates.Samples.DescriptorControlPlane
```

---

## Task 9 — Add Capability Invokers

**Goal:** Execute submit/approve/reject through the existing Capability pipeline.

Files:

```text
CompanyCertificationCapabilityInvokers.cs
```

Requirements:

- Implement `ICapabilityHandlerInvoker` for:
  - SubmitCompanyCertification
  - ApproveCompanyCertification
  - RejectCompanyCertification
- Use `ICapabilityContextAwareHandlerInvoker` for handlers that need to emit domain events through `CapabilityExecutionContext.Items["__domainEvents"]`.
- Register invokers through the existing `CapabilityHandlerResolver`.
- Approve invoker must emit `CompanyCertificationApproved` through the existing event publishing path.
- Do not call workflow APIs from capability handlers.

Verification:

```bash
dotnet build samples/CrestCreates.Samples.DescriptorControlPlane
```

---

## Task 10 — Add Runtime Host Factory

**Goal:** Build the minimal DI host used by runner/tests.

Files:

```text
CompanyCertificationGoldenScenarioHost.cs
```

Requirements:

- Register local event bus:
  - `LocalEventBusOptions`
  - `DefaultLocalEventDispatcher`
  - `DefaultLocalEventBus`
- Register descriptor/control-plane services.
- Register Capability, Workflow, HumanTask runtimes.
- Register sample inventory into each concrete registry.
- Register sample store and event sink.
- Use in-memory/fake identity only where needed.

Verification:

```bash
dotnet build samples/CrestCreates.Samples.DescriptorControlPlane
```

---

## Task 11 — Add Golden Scenario Runner

**Goal:** Gate runtime execution through control-plane governance.

Files:

```text
CompanyCertificationGoldenScenarioRunner.cs
CompanyCertificationGoldenScenarioReport.cs
CompanyCertificationGoldenScenarioOptions.cs
```

Requirements:

- Reuse `CompanyCertificationControlPlaneRunner`.
- If governance is `Blocked`, return without runtime execution.
- If allowed/review-required under options, execute:
  - `IWorkflowEngine.ExecuteAsync`
  - locate waiting HumanTask
  - `IHumanTaskRuntime.CompleteAsync`
  - observe workflow completed
  - observe approved event captured
- Do not manually call `IWorkflowContinuationService`.

Verification:

```bash
dotnet build samples/CrestCreates.Samples.DescriptorControlPlane
```

---

## Task 12 — Add Runnable Golden Scenario Tests 1-3

**Goal:** Prove the happy path.

Tests:

```text
GoldenScenario_Baseline_Should_Start_And_Run_ControlPlane
GoldenScenario_HappyPath_Should_Complete_CompanyCertificationWorkflow
GoldenScenario_Approval_Should_Publish_CompanyCertificationApprovedEvent
```

Verification:

```bash
dotnet test framework/test/CrestCreates.Samples.Tests --filter GoldenScenario
```

---

## Task 13 — Add Runtime Gate Tests

**Goal:** Prove blocked governance prevents runtime execution.

Tests:

```text
GoldenScenario_BreakingSchemaChange_Should_Be_Detected_Before_RuntimeActivation
GoldenScenario_MissingWorkflowTarget_Should_Block_RuntimeActivation
```

Requirements:

- At least one scenario must assert `RuntimeExecutionAttempted == false`.
- Missing workflow target must assert governance `Blocked`.

Verification:

```bash
dotnet test framework/test/CrestCreates.Samples.Tests --filter GoldenScenario
```

---

## Task 14 — README and One-Command Output

**Goal:** Make the regression target obvious.

Files:

```text
README.md
```

Requirements:

- Document:
  - one command: `dotnet test --filter GoldenScenario`
  - expected report fields
  - non-goals
  - control-plane gate rule

Verification:

```bash
dotnet test --filter GoldenScenario
```

---

## Task 15 — Final Audit

**Goal:** Verify #28 completion against the Spec.

Checklist:

- All descriptors exist.
- All required relationships are asserted.
- All six change scenarios are tested.
- Package hashes are stable.
- Runtime happy path uses existing Capability/Workflow/HumanTask/local event runtime.
- Blocked governance prevents runtime execution.
- `dotnet test --filter GoldenScenario` passes.

Verification:

```bash
dotnet test
dotnet test --filter GoldenScenario
```
