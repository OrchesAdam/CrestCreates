# Phase 10c — Asset business evolution

Date: 2026-09-07
Issue: #87; decision owner for framework gaps: #88.
Status: design accepted for implementation; executable and live-model evidence pending.

## Goal and boundary

Prove that an AI-authored, human-approved descriptor change alters a real Asset
business operation through the existing production runtime composition. Preserve
the frozen Phase 9 owners. This is bounded evolution of compiled capabilities,
not evidence that descriptors can invent new domain algorithms or persistence.

Use maintenance review instead of the Issue's illustrative amount threshold.
Asset currently has no assignment-value approval contract. The bounded intent is:

> Require a second maintenance approval after the initial review. Initial approval
> must leave the asset pending; only final approval completes maintenance. Rejection
> at either stage rejects maintenance and must not produce a later approval task.

Existing v1 requests retain their original one-review behavior. New requests after
approved activation use v2. Tenant and organization boundaries remain unchanged.
Both stages use the existing asset-manager authority; separation of reviewer roles
or a four-eyes policy is not claimed by this scenario.

## Canonical owners

- Authoring: existing `IDescriptorAuthoringAgent` and bounded `AgentAuthoringContext`.
- Validation/materialization/review: existing DescriptorDraft and Control Plane.
- Approval: existing activation request service and HumanTask review orchestrator.
- Activation eligibility/evidence recheck: existing Runtime Activation Gate chain.
- Runtime execution: existing Capability, Workflow, HumanTask and durable Outbox.
- Business state: Asset domain/application and SQLite store.
- Runtime state and exact descriptor pins: existing PostgreSQL provider.

Application composition may support both maintenance versions and route intermediate
versus final decisions, but may not duplicate framework approval, hash, authorization,
transaction or registry mutation protocols. Do not copy the Company Certification
runner's success flags as proof. Observe stores, HTTP responses and pinned descriptors.

## Activation and delivery

Registry snapshots remain one-shot builds. Use a fresh host; no hot reload is required.
The final inventory must derive from reviewed drafts and be bound to the exact approved
package/evidence. A fresh host is not authorized merely because an inventory object,
boolean or caller-supplied hash says it is approved. Carry and revalidate authoritative
activation evidence before selecting an inventory for deployment.

Multi-draft changes are all-or-block at deployment. Never run a partially approved set.
Retain v1 descriptors needed by persisted workflows and their referenced HumanTasks.
Never silently substitute latest descriptors for an unavailable or mismatching pin.

Separate evidence for: (1) governance/activation, (2) fresh-host business behavior,
(3) process restart using the same approved version and durable state. If the current
platform cannot express the necessary handoff, record a failing case for #88 before
introducing new framework infrastructure. Do not present in-memory orchestration as
durable activation or claim multi-instance rollout, automatic migration or rollback.

## Independent business case oracle

These expected outcomes are fixed by the design/review owner before implementation.
Tests must execute behavior; parsing these paragraphs is not acceptance evidence.

| ID | Input or state | Expected business observation |
| --- | --- | --- |
| B01 | Baseline v1 request; approve review | Maintenance terminates with one task, preserving prior available/assigned state. |
| B02 | Activated v2 request; approve initial review | Asset stays pending; exactly one final approval task appears. |
| B03 | B02; approve final review | Maintenance approved once; asset returns to prior available/assigned state. |
| B04 | v2 initial review rejected | Maintenance rejected; no final approval task. |
| B05 | v2 final review rejected | Maintenance rejected; no approved decision fact. |
| B06 | Proposal awaiting approval or rejected | No v2 deployment; new requests retain baseline behavior. |
| B07 | Structurally valid proposal omits final review or applies decision at first approval | Framework structural review may pass; independent business oracle fails. Never label this model success. |
| B08 | Unknown reference, partial draft-set approval, changed evidence or agent self-approval | Deployment denied; no altered business state. |
| B09 | Tenant B reads/completes tenant A task | Denied without mutation of either tenant. |
| B10 | v1 suspended request exists during v2 deployment | v1 exact pins retained; completion uses v1 semantics, new requests use v2. |
| B11 | Restart after v2 initial review | Same durable pending workflow resumes using its exact approved descriptor version; no duplicate terminal transition. |
| B12 | Intent needs a new domain algorithm or lacks necessary policy information | Explicit unsupported/needs-clarification result; no fabricated capability or deployment. |

Also exercise existing NativeAOT and source-generated JSON composition. Wrong-tenant,
unauthorized, rejection, retry and negative business cases remain executable failures
even if governance reports contain all expected fields.

## Implementation order

1. Add failing business acceptance cases and the narrow versioned host composition seam.
2. Support the two-review Asset behavior using existing runtime contracts, retaining v1.
3. Compose bounded authoring/review/HumanTask approval and evidence-bound fresh-host handoff.
4. Add rejection, tampering, retained-pin and restart evidence; publish/link/run native host.
5. Evaluate the existing real model adapter independently from deterministic fixtures.
6. Update friction/evidence records and #88 decisions from actual observations.

Each intermediate commit/PR must state which steps are implemented and which remain.
No whole-Issue completion claim from an acceptance skeleton alone.

## Model evaluation and evidence

Deterministic fake/recorded responses prove the adapter and runtime mechanism. They
do not prove a real model understood an intent. Real model evaluation uses a small
separate case set with paraphrases, incorrect-but-valid drafts, insufficient context
and unsupported requests; an independent reviewer scores against the business oracle.
Report provider/model/profile, attempt count, corrections, context size, latency/cost
when available, and outcome. Keep secrets and unsanitized production data out of logs.
Do not make live paid requests an unconditional CI gate. Missing configured credentials
mean live evaluation is pending, not passing. Do not build a generic evaluation platform.

Evidence must identify commit, command, environment, result and limitation. Distinguish
executed tests, recorded experiments and review judgments. Documentation-presence tests
cannot attest to business correctness. #88 C88-01 needs a reproducible negative factory
activation case before implementing the proposed framework improvement.

## Non-goals

No new Agent Runtime, semantic/vector retrieval, Harness layer, arbitrary code execution,
new data-provider authority, source scanning fallback, global mutable registry, automatic
production deployment, or general approval/activation framework. Do not change Phase 9
semantics without a demonstrated downstream failing acceptance case.
