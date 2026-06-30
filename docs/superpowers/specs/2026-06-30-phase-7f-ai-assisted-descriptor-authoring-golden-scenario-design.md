# Phase 7f - AI-assisted Descriptor Authoring Golden Scenario

> Date: 2026-06-30  
> Status: APPROVED / READY FOR IMPLEMENTATION  
> Issue: #32  
> Depends on: #43 first closure, Phase 7a-7e descriptor governance foundation

## 1. Goal

Build the first end-to-end AI-assisted descriptor authoring golden scenario on
top of the deterministic descriptor governance chain.

This phase proves that an AI/agent can help author descriptor changes without
becoming the runtime authority, governance authority, approval authority, or
activation authority.

The required chain is:

```text
Human / LLM intent
  -> MetadataContextPack
  -> AgentMemoryPack
  -> AgentAuthoringContext
  -> deterministic descriptor authoring agent
  -> DescriptorDraftSet
  -> sequential DescriptorDraft review/materialization
  -> Review Report / Fix Proposal
  -> activation request with evidence binding
  -> HumanTask approval when policy requires review
  -> RuntimeActivationGate
  -> fresh runtime host built from approved final inventory
  -> updated Company Certification runtime behavior
```

The golden scenario uses the existing Company Certification sample:

```text
Intent: Add second-level finance review before approving company certification.
```

Expected runtime behavior after approval and activation:

```text
SubmitCompanyCertification
  -> Initial Review HumanTask
  -> Finance Review HumanTask
  -> ApproveCompanyCertification
  -> CompanyCertificationApproved event
```

## 2. Non-Goals

This phase must not introduce:

- a real LLM provider adapter;
- production prompt management;
- HTTP, Dynamic API, MCP, CLI, TUI, or UI authoring surfaces;
- production durable authoring provider;
- a second DescriptorDraft model;
- a second draft review service;
- a second review report or fix proposal chain;
- a second activation request service;
- a second runtime activation gate;
- runtime registry hot reload;
- framework-level batch draft review unless a later phase explicitly designs it.

This is a sample-level orchestration phase, not a core redesign.

## 3. Hard Boundary Principles

### 3.1 Sample-level orchestration, not core redesign

Phase 7f composes existing platform services into a golden scenario. It may add
a narrow authoring adapter boundary and sample-local orchestration, but it must
not redesign core DescriptorDraft, Control Plane, activation, HumanTask, or
runtime registry contracts.

The authoring layer is allowed to produce draft plans and drafts only. It must
not own review, governance, approval, activation, or runtime mutation.

### 3.2 Draft set all-or-block

The finance-review change is a multi-descriptor change:

```text
create HumanTaskDescriptor
update WorkflowDescriptor
```

It must behave as one scenario-level activation unit. The implementation must
not activate a finance HumanTask without the workflow update, or activate the
workflow update without the finance HumanTask descriptor.

If any draft in the set fails validation, materialization, review, governance,
or activation readiness, the whole authored change is blocked for runtime
execution.

### 3.3 Runtime proof uses a fresh host from approved final inventory

Current runtime registries are one-shot `Build()` snapshots. Current
`InMemoryRuntimeActivationGate` records activation success but does not mutate
runtime descriptor registries.

Therefore the runtime proof must execute against a fresh sample runtime host
built from the approved final inventory after activation. Running the updated
workflow against the static baseline host is not acceptable.

Correct proof shape:

```text
baseline sample host
  -> author/review/materialize/approve/activate final inventory
  -> create fresh activated runtime host from approved final inventory
  -> execute Company Certification flow against activated host
```

## 4. Existing Chain to Reuse

Phase 7f must reuse the existing main-chain components:

- `AgentAuthoringRequest`, `AgentAuthoringContext`, `AgentMemoryPack`, and
  `IAgentAuthoringContextBuilder` from Agent Memory.
- `MetadataContextPack` from Metadata ContextPack.
- `DescriptorDraft`, `IDescriptorDraftStore`, and
  `IDescriptorDraftReviewService` from DescriptorDraft.
- Review report and fix proposal contracts from Agent Control Plane.
- `IDescriptorActivationRequestService`, `IActivationReviewOrchestrator`, and
  `IRuntimeActivationGate` from Agent Control Plane activation.
- `CompanyCertificationDescriptors`,
  `CompanyCertificationControlPlaneRunner`, and the existing Company
  Certification runtime sample.

Do not introduce a parallel chain for any of these responsibilities.

## 5. Authoring Context Boundary

The input to the authoring agent is `AgentAuthoringContext`.

`AgentAuthoringContext` is composed from:

```text
AgentAuthoringRequest
  + MetadataContextPack
  + AgentMemoryPack
  = AgentAuthoringContext
```

The #43 memory/context layer owns memory recall and context compression. Phase
7f consumes the already composed context.

The authoring agent must not:

- query raw conversation stores;
- query raw task history stores;
- query memory stores or retrievers;
- query descriptor registries;
- query draft stores;
- query Control Plane internals;
- query activation state;
- query HumanTask state;
- call runtime handlers.

Memory is non-authoritative recalled context. If memory conflicts with
metadata, review, governance, package evidence, activation evidence,
authorization, lifecycle, or runtime gate state, memory loses.

## 6. Authoring Adapter Contract

Add a narrow authoring adapter boundary for this phase:

```text
IDescriptorAuthoringAgent
DescriptorAuthoringPlan
DescriptorAuthoringResult
DescriptorDraftSet
FakeCompanyCertificationAuthoringAgent
```

The exact implementation names may follow local code style, but the conceptual
contract is fixed:

- `IDescriptorAuthoringAgent` consumes only `AgentAuthoringContext`.
- `DescriptorAuthoringPlan` describes deterministic planned changes.
- `DescriptorAuthoringResult` carries plan, draft set, diagnostics, and
  authoring metadata.
- `DescriptorDraftSet` contains existing
  `CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft` instances.
- `FakeCompanyCertificationAuthoringAgent` is deterministic and sample-scoped.

No real LLM adapter is added in this phase.

## 7. Authored Draft Set

For the intent:

```text
Add second-level finance review before approving company certification.
```

the fake authoring agent produces a draft set equivalent to:

### 7.1 Create HumanTaskDescriptor

Create:

```text
ht_finance_review_company_certification
```

The finance review HumanTask should:

- reuse the existing Company Certification review form/schema where possible;
- carry finance-specific permission or assignee context;
- preserve an `Approve` outcome targeting
  `cap_approve_company_certification`;
- preserve a `Reject` outcome targeting
  `cap_reject_company_certification`.

### 7.2 Update WorkflowDescriptor

Update:

```text
wf_company_certification
```

The final workflow shape is:

```text
step_submit
  -> step_review
  -> step_finance_review
  -> step_approve
```

`step_finance_review` targets
`ht_finance_review_company_certification`.

The existing initial review remains in place. Approval still happens only after
the finance review completes with the approve outcome.

## 8. Draft Set Review and Materialization

Current `IDescriptorDraftReviewService` is single-draft oriented. Phase 7f
should not force a framework-level batch review redesign.

The golden scenario runner may sequentially apply drafts:

```text
currentInventory
  -> review/materialize draft A
  -> proposedInventory1
  -> review/materialize draft B against proposedInventory1
  -> finalProposedInventory
```

Rules:

- Every draft is reviewed through the existing `IDescriptorDraftReviewService`.
- Each subsequent draft is reviewed against the previous proposed inventory.
- The final proposed inventory is the only inventory eligible for package
  evidence and runtime proof.
- Each draft may produce its own `DescriptorDraftReviewResult`, but the golden
  scenario must derive one final scenario-level decision from the complete
  final proposed inventory. Only this final decision can feed package/evidence
  binding and activation request creation.
- Any validation, materialization, review, governance, compatibility, topology,
  or package evidence blocker on any draft blocks the entire draft set.
- A failing draft path must still produce a review report and fix proposal path
  so agent remediation remains governed.

## 9. Activation Binding

Current activation request contracts are single-draft subject contracts:

```text
SubmitActivationRequestRequest.DraftId
ActivationBindingSnapshot.DraftId
```

Phase 7f must avoid per-draft partial activation. The scenario-level activation
handoff is:

```text
finalProposedInventory
  -> final review/package/evidence binding
  -> one activation request using the final workflow or aggregate subject
  -> approval path when policy requires review
  -> RuntimeActivationGate
```

If a sample-local aggregate/envelope subject is needed, it must remain local to
the golden scenario unless a later design promotes batch activation into a core
contract.

In the first implementation, prefer using the workflow update draft as the
activation subject if the existing single-`DraftId` contract is preserved. The
activation binding must still cover the complete final proposed inventory,
including both the finance HumanTask creation and the workflow update. A
sample-local aggregate/envelope subject may be used only if it avoids changing
core activation contracts.

The binding snapshot must bind the final reviewed state, including review,
package, evidence, contract, and definition hashes. Memory evidence must not be
treated as activation evidence.

## 10. Control Plane and Approval Rules

The fake authoring agent may only produce a draft plan and draft set.

It must not:

- approve its own changes;
- mutate active descriptors;
- bypass draft review;
- bypass Control Plane analysis;
- bypass review report or fix proposal generation;
- bypass activation policy;
- bypass HumanTask approval when required;
- call `IRuntimeActivationGate` directly;
- call capability, workflow, or HumanTask runtime handlers directly.

The only valid approval and activation path is:

```text
DescriptorDraftReviewResult / package evidence
  -> SubmitActivationRequestRequest
  -> IDescriptorActivationRequestService
  -> IActivationReviewOrchestrator when ReviewRequired
  -> ApproveActivationRequestAsync or RejectActivationRequestAsync
  -> IRuntimeActivationGate
```

`IRuntimeActivationGate` remains the activation executor. The sample runtime
handoff after the gate is proof infrastructure, not an alternate approval or
activation authority.

## 11. Runtime Proof

The Company Certification runtime sample must be extended so the activated
runtime host can be built from an explicit descriptor inventory.

The runtime proof must execute this flow:

```text
SubmitCompanyCertification
  -> Initial Review HumanTask created
  -> Initial Review HumanTask completed with Approve
  -> Finance Review HumanTask created
  -> Finance Review HumanTask completed with Approve
  -> ApproveCompanyCertification capability runs
  -> CompanyCertificationApproved event captured
```

The runner must no longer assume there is only one waiting HumanTask. It should
complete suspended HumanTasks in order until the workflow reaches a terminal
state.

The runtime report must include enough evidence to prove that the activated
inventory was used:

```text
ActivatedWorkflowDescriptorId
ActivatedWorkflowVersion
ActivatedHumanTaskDescriptorIds
ObservedHumanTaskDescriptorIds
WorkflowStepSequence
InitialReviewHumanTaskInstanceId
FinanceReviewHumanTaskInstanceId
CompletedHumanTaskCount
ApprovedEventCaptured
ActivatedInventoryHash
ActivatedPackageEvidenceHash
```

`CompletedHumanTaskCount == 2` alone is not sufficient. The report must also
show the descriptor ids, workflow step sequence, and approved inventory/evidence
identity.

## 12. Error and Block Semantics

Phase 7f distinguishes these states:

- Authoring failure: fake agent cannot produce a deterministic draft set.
- Draft set blocked: at least one draft fails validation, materialization,
  review, topology, impact, compatibility, governance, or package readiness.
- ReviewRequired: governance allows handoff only after human approval.
- Activation stale: bound evidence no longer matches current reviewed state.
- Activation rejected: approval decision rejects the request.
- Runtime proof failure: activation succeeded, but runtime host built from the
  approved inventory does not execute the expected two-review flow.

Only the final state may execute runtime behavior:

```text
all drafts reviewed
  + final package/evidence binding valid
  + activation approved or auto-activatable
  + RuntimeActivationGate succeeds
```

All other states block runtime execution.

## 13. AOT and Determinism

The phase must preserve the project direction:

- no reflection scanner or runtime discovery path as the primary chain;
- no stringly typed hidden authority;
- no service locator behavior inside business handlers;
- no generated-less dynamic fallback for authoring authority;
- deterministic fake authoring output for identical context input;
- deterministic draft ordering;
- deterministic report and activation evidence binding;
- no real network or external LLM calls in tests.

The authoring layer may use descriptor ids as existing platform identities, but
must not invent ad hoc identity protocols for governance or activation.

## 14. Acceptance Criteria

Phase 7f is complete when:

- The golden scenario uses sample-level orchestration and does not redesign
  core draft review, activation, HumanTask, or runtime registry contracts.
- The fake authoring agent consumes `AgentAuthoringContext` only.
- The fake authoring agent produces deterministic output for identical input.
- The authored draft set creates the finance review HumanTask descriptor.
- The authored draft set updates the Company Certification workflow descriptor.
- Draft set handling is all-or-block.
- Sequential draft review/materialization produces one final proposed
  inventory.
- Topology for the final inventory includes the workflow-to-finance-HumanTask
  relationship.
- Control Plane review/governance runs on the authored final inventory.
- At least one invalid authored draft path produces review report and fix
  proposal output.
- Activation request binds final review/package/evidence hashes.
- ReviewRequired activation uses HumanTask approval when policy requires it.
- Runtime execution is blocked before approval/activation.
- Runtime proof uses a fresh host built from approved final inventory.
- The runtime sample creates and completes initial review and finance review in
  order.
- `ApproveCompanyCertification` runs only after finance review approval.
- `CompanyCertificationApproved` is captured after the second review.
- Memory is treated as non-authoritative recalled context.
- The fake authoring agent cannot bypass draft review, Control Plane review,
  activation request policy, HumanTask approval, or RuntimeActivationGate.

## 15. Required Tests

Minimum tests:

- `AgentAuthoringContext_Composes_MetadataContextPack_MemoryPack_And_Request`
- `FakeAuthoringAgent_Consumes_AgentAuthoringContext_Only`
- `FakeAuthoringAgent_Output_Is_Deterministic`
- `FakeAuthoringAgent_DoesNotUse_RawMemoryStores`
- `AuthoringContext_Memory_Is_NonAuthoritative`
- `AuthoringContext_Metadata_Wins_When_Memory_Conflicts`
- `DraftSet_Creates_FinanceReview_HumanTask`
- `DraftSet_Updates_Workflow_With_FinanceReviewStep`
- `DraftSet_Review_Is_AllOrBlock_When_HumanTaskDraft_Invalid`
- `DraftSet_Review_Is_AllOrBlock_When_WorkflowDraft_Invalid`
- `DraftSet_SequentialMaterialization_Produces_FinalProposedInventory`
- `DraftSet_FinalDecision_Rechecks_CompleteInventory`
- `DraftSet_FinalTopology_Includes_Workflow_To_FinanceHumanTask`
- `InvalidDraft_Builds_ReviewReport_And_FixProposal`
- `ActivationRequest_Binds_FinalReview_And_PackageEvidenceHashes`
- `ReviewRequired_Creates_HumanTaskApproval`
- `Approval_Executes_Through_ActivationRequestService_And_RuntimeActivationGate`
- `ActivationGateSuccess_Alone_DoesNot_Count_As_RuntimeProof`
- `RuntimeProof_Builds_FreshHost_From_ApprovedFinalInventory`
- `RuntimeProof_DoesNotUse_StaticBaselineWorkflow`
- `RuntimeProof_Completes_InitialReview_Then_FinanceReview`
- `RuntimeProof_Records_WorkflowStepSequence`
- `RuntimeProof_Records_ObservedHumanTaskDescriptorIds`
- `RuntimeProof_Approves_After_SecondReview`
- `RuntimeProof_Captures_CompanyCertificationApproved`
- `FakeAuthoringAgent_Cannot_Bypass_ControlPlaneReview`
- `FakeAuthoringAgent_Cannot_Call_RuntimeActivationGate`
- `FakeAuthoringAgent_Cannot_Call_RuntimeHandlers`

## 16. Future Work

Possible later phases may add:

- real LLM provider adapters;
- prompt template governance;
- authoring tool surfaces;
- framework-level batch draft review and activation contracts;
- production descriptor activation state stores;
- runtime descriptor hot reload or controlled registry swap;
- persisted authoring sessions;
- richer remediation loops.

None of these are required for Phase 7f.
