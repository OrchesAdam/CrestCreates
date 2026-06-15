# Sample — Descriptor Control Plane Golden Scenario: Design Spec

> **Date:** 2026-06-15 | **Status:** Draft | **Parent Issue:** [#28 — Sample — Descriptor Control Plane Golden Scenario](https://github.com/OrchesAdam/CrestCreates/issues/28)

---

## 1. Overview

### 1.1 Goal

Issue #28 is the first runnable golden scenario for the descriptor-native control plane.

It must prove this chain end to end:

```text
Company Certification descriptor inventory
  -> relationship extraction
  -> topology snapshot and diagnostics
  -> impact analysis
  -> compatibility analysis
  -> lifecycle governance decision
  -> package / manifest / snapshot / evidence / stable hash
  -> runtime activation gate
  -> minimal capability / workflow / humantask / event happy path
```

The sample is not a product module. It is a regression guard showing that descriptor governance can safely allow or block runtime execution.

### 1.2 Architectural Decision

Treat #28 as a two-plane golden scenario:

```text
Layer A: Descriptor Control Plane
  descriptor inventory, relationship, topology, impact, compatibility,
  lifecycle governance, package evidence

Layer B: Runtime Execution Plane
  capability execution, workflow start/continuation, humantask completion,
  domain state mutation, event capture
```

The control plane is not a side report. Its lifecycle decision is the activation gate before the runtime plane can execute.

### 1.3 Current Codebase Fit

The codebase already has the required main-chain building blocks:

- `AddRelationshipKernel()`, `AddTopologyKernel()`
- `AddDescriptorImpactAnalysis()`
- `AddDescriptorCompatibilityAnalysis()`
- `AddDescriptorLifecycleGovernance()`
- `AddDescriptorPackaging()`
- `AddCapabilityRuntime()`
- `AddWorkflowEngine()`
- `AddHumanTaskRuntime()`
- Local event bus via `DefaultLocalEventBus` + `DefaultLocalEventDispatcher`

Phase 6f exists in the current codebase, so package / manifest / snapshot / evidence / stable hash are in scope for the first implementation.

---

## 2. Scope Boundary

### 2.1 In Scope

- New sample project:

```text
samples/CrestCreates.Samples.DescriptorControlPlane/
```

- New or reused sample test project:

```text
framework/test/CrestCreates.Samples.Tests/
```

- Company Certification descriptor inventory.
- Prepared descriptor change scenarios.
- Golden scenario runner returning a structured report.
- Minimal in-memory runtime state for company certification.
- In-memory / fake event capture.
- Tests runnable with:

```bash
dotnet test --filter GoldenScenario
```

### 2.2 Out of Scope

- Real OCR integration.
- Real database provider or production persistence.
- UI.
- Distributed event bus.
- Marketplace / package repository.
- LLM draft generation.
- API / MCP / AgentTool exposure.
- Production approval workflow persistence.
- New runtime scanner, reflection fallback, or alternate workflow engine.

### 2.3 Allowed Simplifications

- In-memory stores.
- Fake tenant/current user/reviewer identity.
- OCR-free input.
- Fake event sink.
- Minimal `ServiceProvider` test host instead of a web app.

These simplifications must not bypass the framework runtime path. The happy path must use the existing Capability, Workflow, HumanTask, and local event runtime services.

---

## 3. Descriptor Inventory

### 3.1 Naming

Use two descriptor identity surfaces:

- `Id` is the internal stable reference key used by `VersionedDescriptorRef` and the current generator/ref-validation path. It must follow existing prefix conventions so the sample stays on the framework mainline:

```text
schema_company_certification_submit_input
schema_company_certification_review_input
schema_company_certification_result
schema_company_certification_approved_payload
schema_company_certification_rejected_payload

form_company_certification_submit
form_company_certification_review

cap_submit_company_certification
cap_approve_company_certification
cap_reject_company_certification

ht_review_company_certification

wf_company_certification

evt_company_certification_submitted
evt_company_certification_approved
evt_company_certification_rejected
```

- `Name` is the human-readable descriptor identity used by the issue/spec narrative:

```text
schema.CompanyCertificationSubmitInput
schema.CompanyCertificationReviewInput
schema.CompanyCertificationResult
schema.CompanyCertificationApprovedPayload
schema.CompanyCertificationRejectedPayload

form.CompanyCertificationSubmitForm
form.CompanyCertificationReviewForm

capability.SubmitCompanyCertification
capability.ApproveCompanyCertification
capability.RejectCompanyCertification

humantask.ReviewCompanyCertification

workflow.CompanyCertificationWorkflow

event.CompanyCertificationSubmitted
event.CompanyCertificationApproved
event.CompanyCertificationRejected
```

### 3.2 Required Relationships

The baseline topology must include these relationship classes:

- Form -> Schema.
- Capability -> InputSchema / OutputSchema.
- Capability -> Event through `Produces`.
- Workflow -> Capability.
- Workflow -> HumanTask.
- HumanTask -> Form through `Interaction`.
- HumanTask outcome -> Capability for both approve and reject.
- Event -> PayloadSchema.

### 3.3 Outcome Modeling

The descriptor baseline must model both outcomes:

```text
ReviewCompanyCertification
  Outcome.Approve -> Capability.ApproveCompanyCertification
  Outcome.Reject  -> Capability.RejectCompanyCertification
```

The first runtime happy path executes only `Approve`. The reject path is descriptor-covered but not required as a runtime acceptance path in #28.

### 3.4 Permission Modeling

Current `CapabilityDescriptor` has `Permissions : IReadOnlyList<string>`, not `RequiredPermission`.

The security-sensitive change scenario must modify `Permissions` or `RiskLevel`, for example:

```text
ApproveCompanyCertification.Permissions:
  ["CompanyCertification.Approve"]
    -> ["CompanyCertification.SuperApprove"]
```

---

## 4. Golden Scenario Runner

### 4.1 Contract

Add a reusable runner:

```csharp
public sealed class CompanyCertificationGoldenScenarioRunner
{
    public Task<CompanyCertificationGoldenScenarioReport> RunAsync(
        CompanyCertificationGoldenScenarioOptions? options = null,
        CancellationToken ct = default);
}
```

The runner is the single execution path reused by tests and any optional console/sample app.

### 4.2 Report

```csharp
public sealed record CompanyCertificationGoldenScenarioReport
{
    public required bool ControlPlanePassed { get; init; }
    public required string GovernanceDecision { get; init; }
    public required bool RuntimeExecutionAttempted { get; init; }
    public required string WorkflowStatus { get; init; }
    public required string HumanTaskStatus { get; init; }
    public required bool ApprovedEventCaptured { get; init; }
    public required string PackageContentHash { get; init; }
    public required string PackageEvidenceHash { get; init; }
    public required string PackageEnvelopeHash { get; init; }
}
```

### 4.3 Options

```csharp
public sealed record CompanyCertificationGoldenScenarioOptions
{
    public string ChangeScenario { get; init; } = "Baseline";
    public bool ExecuteRuntimeWhenReviewRequired { get; init; } = true;
}
```

Gate rule:

```text
Allowed -> execute runtime
ReviewRequired -> execute runtime only when ExecuteRuntimeWhenReviewRequired is true
Blocked -> do not execute runtime
```

This keeps review-required behavior explicit without weakening the blocker gate.

### 4.4 Runner Algorithm

```text
1. Build baseline descriptor inventory.
2. Build the selected after-inventory/change scenario.
3. Build topology from the after-inventory.
4. Build change set from before/after inventories.
5. Run impact analysis.
6. Run compatibility analysis.
7. Run lifecycle governance for Activate.
8. Build descriptor package with topology, impact, compatibility, and governance evidence.
9. If governance blocks activation, return report with RuntimeExecutionAttempted=false.
10. Register baseline descriptors into runtime registries.
11. Register strongly named capability invokers for Submit/Approve/Reject.
12. Execute CompanyCertificationWorkflow through IWorkflowEngine.
13. Complete ReviewCompanyCertification through IHumanTaskRuntime.CompleteAsync.
14. Let HumanTaskCompletedEvent resume the workflow through the existing local event runtime.
15. Capture CompanyCertificationApproved event through the local event bus path.
16. Return report.
```

Do not manually mutate `WorkflowInstance` to simulate completion. The test must observe the existing workflow continuation path.

---

## 5. Runtime Happy Path

### 5.1 Flow

```text
SubmitCompanyCertification capability
  -> creates in-memory company certification state: Submitted
  -> workflow advances to ReviewCompanyCertification HumanTask
  -> reviewer completes task with Approve
  -> HumanTaskCompletedEvent resumes workflow
  -> ApproveCompanyCertification capability executes
  -> state becomes Approved
  -> CompanyCertificationApproved event is captured
  -> workflow completes
```

### 5.2 Runtime State

Use a sample-local in-memory store:

```csharp
public sealed class InMemoryCompanyCertificationStore
{
    public CompanyCertificationState State { get; }
}
```

Keep the model minimal:

- request ID
- enterprise name
- unified social credit code
- status: `None`, `Submitted`, `Approved`, `Rejected`

This is sample-owned state, not a new framework persistence abstraction.

### 5.3 Event Capture

The approve handler should emit a `CompanyCertificationApproved` domain event through the existing Capability event publishing path.

Use `ICapabilityContextAwareHandlerInvoker` when the sample handler needs to add domain events to `CapabilityExecutionContext.Items["__domainEvents"]`. This keeps event emission inside the Capability pipeline and avoids a sample-only event bypass.

The acceptance assertion is event capture, not distributed delivery.

---

## 6. Control-Plane Scenarios

### 6.1 Healthy Baseline Topology

Expected:

- no topology errors;
- all required relationship classes are present;
- package diagnostics contain no error;
- governance is not `Blocked`.

### 6.2 Compatible Optional Field Addition

Change:

```text
Add optional ContactEmail to CompanyCertificationSubmitInput.
```

Expected:

- impact reaches submit form, submit capability, workflow;
- compatibility max level is `Compatible`;
- lifecycle decision is `Allowed` or `ReviewRequired`, but not `Blocked`.

### 6.3 Breaking Required Field Removal

Change:

```text
Remove required UnifiedSocialCreditCode from CompanyCertificationSubmitInput.
```

Expected:

- impact reaches affected consumers;
- compatibility contains a breaking finding;
- lifecycle decision is `ReviewRequired` by default or `Blocked` under strict options;
- runtime must not execute if the selected scenario returns `Blocked`.

### 6.4 Security-Sensitive Permission Change

Change:

```text
ApproveCompanyCertification.Permissions changes.
```

Expected:

- compatibility contains `SecuritySensitive`;
- lifecycle decision is `ReviewRequired` for activation.

### 6.5 Missing Workflow Target

Change:

```text
Workflow step points to missing Capability or HumanTask.
```

Expected:

- topology has `MISSING_TARGET`;
- lifecycle activation is `Blocked`;
- runner report has `RuntimeExecutionAttempted=false`.

### 6.6 Unsupported Future Reference

Change:

```text
Workflow uses SubWorkflowTarget.
```

Expected:

- topology emits `UNSUPPORTED_REFERENCE` warning;
- impact surfaces the topology diagnostic when relevant;
- lifecycle outcome follows current operation/options;
- runtime happy path is not required for this scenario.

---

## 7. Acceptance Tests

### 7.1 Control-Plane Golden Scenario Tests

```text
Baseline_Should_Build_Healthy_Topology
Optional_Field_Addition_Should_Be_Compatible
Removing_Required_Schema_Field_Should_Be_Breaking_And_ReviewRequired
Permission_Change_Should_Be_SecuritySensitive
Missing_Workflow_Target_Should_Block_Activation
Unsupported_SubWorkflow_Should_Surface_Warning
Package_Should_Include_Manifest_Snapshot_Evidence_And_StableHash
```

### 7.2 Runnable Golden Scenario Tests

```text
GoldenScenario_Baseline_Should_Start_And_Run_ControlPlane
GoldenScenario_HappyPath_Should_Complete_CompanyCertificationWorkflow
GoldenScenario_Approval_Should_Publish_CompanyCertificationApprovedEvent
GoldenScenario_BreakingSchemaChange_Should_Be_Detected_Before_RuntimeActivation
GoldenScenario_MissingWorkflowTarget_Should_Block_RuntimeActivation
```

### 7.3 One-Command Regression

The implementation is complete only when this passes:

```bash
dotnet test --filter GoldenScenario
```

The report should make these facts obvious:

```text
ControlPlane: Passed
Governance: Allowed/ReviewRequired as expected
Workflow: Completed
HumanTask: Approved
Event: CompanyCertificationApproved captured
Package: Stable hashes verified
```

---

## 8. Implementation Constraints

1. Do not add a runtime reflection scanner or fallback.
2. Do not add a second workflow execution path.
3. Do not bypass the existing `HumanTaskCompletedEvent` -> workflow continuation bridge.
4. Do not add production persistence abstractions for the sample store.
5. Do not put business sample shortcuts into framework projects.
6. Keep descriptor construction strongly typed.
7. Keep tasks small enough that each OpenCode step can be reviewed independently.
