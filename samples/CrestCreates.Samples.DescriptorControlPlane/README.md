# CrestCreates.Samples.DescriptorControlPlane

A **two-plane golden scenario** for the Company Certification business domain. This sample proves the descriptor-native control plane can gate and drive a real runnable workflow end-to-end.

## Two-Plane Architecture

### Layer A: Descriptor Control Plane
```
descriptor inventory → relationship extraction → topology → impact →
compatibility → lifecycle governance → package / manifest / evidence / stable hash
```

### Layer B: Runtime Execution Plane
```
capability execution → workflow start / continuation → human task creation →
human task approval → approval capability execution → event capture
```

The control plane is the activation gate: `Allowed` executes runtime, `ReviewRequired` may execute only with explicit option, `Blocked` must not execute.

## Project Structure

| File | Purpose |
|---|---|
| `CompanyCertificationDescriptors.cs` | 15 static descriptors (schemas, forms, capabilities, humantask, workflow, events) |
| `CompanyCertificationDescriptorInventory.cs` | Typed collections grouped by descriptor kind |
| `CompanyCertificationChangeScenarios.cs` | 6 before/after change scenarios with AOT-safe deep copy |
| `CompanyCertificationControlPlaneRunner.cs` | Synchronous control-plane analysis pipeline |
| `CompanyCertificationControlPlaneReport.cs` | Structured report with convenience pass/fail projections |
| `CompanyCertificationRuntimeModels.cs` | Domain data types for the runtime execution plane |
| `InMemoryCompanyCertificationStore.cs` | Thread-safe in-memory store for certification records |
| `CompanyCertificationEvents.cs` | `ILocalEvent` implementations for submission, approval, rejection |
| `CompanyCertificationCapabilityInvokers.cs` | Three `ICapabilityContextAwareHandlerInvoker` implementations |
| `CompanyCertificationGoldenScenarioHost.cs` | DI host factory composing all control-plane and runtime services |
| `CompanyCertificationGoldenScenarioRunner.cs` | Two-plane runner with activation gate |
| `CompanyCertificationGoldenScenarioReport.cs` | Runtime report record |

## One-Command Regression

```bash
dotnet test --filter GoldenScenario
```

Expected output:
```
ControlPlane: Passed
Governance: ReviewRequired (baseline) or Blocked (breaking scenarios)
Workflow: Completed
HumanTask: Approved
Event: CompanyCertificationApproved captured
```

## Test Coverage

### Control-Plane Tests (7 tests)
- Baseline healthy topology
- Optional field addition → compatible
- Required field removal → breaking, review-required
- Permission change → security-sensitive
- Missing workflow target → blocked
- Unsupported subworkflow → warning
- Package manifest/evidence/stable hash

### Golden Scenario Tests (5 tests)
- Baseline control-plane + runtime
- Happy-path workflow completion
- Approval event publication
- Breaking change blocks runtime activation
- Missing workflow target blocks runtime activation

## Descriptor Inventory

| Kind | Count | IDs |
|---|---|---|
| Schema | 5 | SubmitInput, ReviewInput, Result, ApprovedPayload, RejectedPayload |
| Form | 2 | SubmitForm, ReviewForm |
| Capability | 3 | Submit, Approve, Reject |
| HumanTask | 1 | ReviewCompanyCertification (both Approve + Reject outcomes) |
| Workflow | 1 | 3-step: submit → review (human task) → finalize approval |
| Event | 3 | Submitted, Approved, Rejected |
