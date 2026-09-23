# Durable activation review: domain cutover for Issue #87

Status: design proposal, not implemented. The current retained live proposal and
PR #102 evidence do not depend on this proposal being implemented or approved.

## Why this is the next boundary

An enterprise human review may outlive the process that requested it. A durable
proposal plus an in-memory pending request is insufficient: a restart loses the
request, its decision replay markers and the evidence required for authorization.
The next deliverable must strengthen the platform authority, rather than add
another sample-only UnderReview demonstration. Production runtime installation is
a subsequent boundary and must remain separately named and verified.

Current facts:

- IDescriptorDraftStore has a formal PostgreSQL implementation and remains the
  single proposal authority. The actual HumanTask proposal from the retained run
  must be preserved without rewriting its content or status.
- DefaultDescriptorActivationRequestService owns a private ConcurrentDictionary
  of ActivationResourceSnapshot. The snapshot includes the owner draft and applied
  completion-event/decision markers, but has no durable revision or review-task link.
- Approval/rejection/cancellation/recheck/gate paths read a snapshot and later
  assign a replacement unconditionally. Concurrent callers can pass stale checks;
  sequential replay checks are not concurrent idempotency guarantees.
- DefaultAgentControlPlaneToolService holds review/package/evidence previews in
  process-local dictionaries. A durable request without recoverable bound evidence
  cannot pass a trustworthy post-restart recheck.
- The tool creates a request and subsequently creates a review HumanTask. Failure
  between these operations can leave an orphan request or an unlinked task.
- HumanTask completion already has revisioned persistence and transactional Outbox
  patterns. Reuse that mainline rather than inventing a second callback pipeline.
- Only an in-memory runtime activation gate is provided. Its receipt is not an
  installation, registry replacement or production deployment.

Source boundary: the phase9bplus durable control-plane/reference-data design
explicitly excludes activation-request ownership from the draft-store cutover.
This plan takes that separate domain boundary deliberately.

## Required authority and transition semantics

Introduce a provider-owned activation request persistence contract in the existing
Control Plane abstractions. The lifecycle service remains the only policy/transition
owner and consumes that contract; remove its private dictionary mainline when the
cutover lands. PostgreSQL persistence must reference abstractions, not Control Plane
implementation types. An explicit test/development provider is permitted; a durable
composition must never silently fall back to process memory.

A stored activation aggregate must retain the immutable request/binding and captured
policy/governance decision, immutable owner snapshot needed by existing authorization,
revision, task linkage, decision/completion-event identity and transition correlation.
A reference to a mutable current draft alone cannot replace its captured owner.
Reuse the formal draft payload representation; do not open a second draft protocol.
Do not move authorization decisions into a generic repository.

Every mutation is conditional on tenant, request identity, expected revision and
allowed prior status. Conflict returns a typed current-state/conflict result, not a
blind retry of a decision evaluated against stale evidence. Accepted decision and
completion-event identity commit together. Same event plus same decision is a replay;
different event/decision cannot overwrite a winner. Cancellation/rejection racing
approval must have a single durable winner. Local locks are not a multi-process proof.

Artifact ownership is part of this cutover, not optional follow-up documentation:
request creation must bind immutable tenant-scoped review/package/evidence artifacts
that can be loaded and rechecked after restart. Preserve the current full canonical
hash metadata, visibility boundaries and author identities. Locators or supplied
hashes are never authority on their own. Missing/corrupt/mismatched stored evidence
fails closed; do not silently regenerate different content under an old artifact ID.

## Staged implementation and exit criteria

1. **Contract and transition design.** Specify the exact aggregate, artifact ownership,
   CAS results and legal transitions, then map every current service writer and
   resource resolver onto it. Determine how submitted operation identity survives
   retries. Review public compatibility and generated JSON ownership before code.
   This stage is a design gate, not a durability claim.
2. **Durable pending-review cutover.** Implement formal PostgreSQL aggregate/artifact
   stores, service/resource-resolver routing and creation of the review task. Request
   plus task must be atomic in the existing shared transaction when runtime APIs
   permit it. If they do not, use a durable task-creation intent and reconciler with
   a proven idempotency key; do not return an unrecoverable orphan as success. A
   second process must find exactly one pending task and recover its exact bound
   request/evidence. The retained real proposal can enter this boundary without
   completing the task or approving itself.
3. **Decision delivery.** Preserve HumanTask completion + Outbox atomicity. Consume
   the completion through the existing required-consumer path; commit CAS transition
   and event replay identity before acknowledging delivery. Test conflicting decisions,
   cancellation, cross-tenant access and restart after commit/before acknowledgement.
4. **Activation dispatch and recovery.** Approval must durably hand off an activation
   intent to the sole gate owner. Define stable command identity, idempotent receipt
   and reconciliation before permitting retries. A crash after external mutation but
   before final status is an unknown outcome, not permission to repeat arbitrary
   mutation. This stage must not claim distributed exactly-once from sequential tests.
5. **Actual runtime owner.** Separately design and verify how an approved immutable
   package changes a real Asset host inventory and how its identity is attributable
   to business execution. Do not rename an in-memory gate as production activation.

A stage that migrates persistence must cover all existing lifecycle writes; a
parallel durable side table beside the authoritative dictionary is forbidden.
If the gate-recovery stage cannot ship with the pending-review stage, durable
composition must explicitly block activation until its executor contract is ready.
No test-only bypass may enable it.

## Mandatory failure evidence

- Two processes race approve/reject/cancel: one transition wins and the other
  receives conflict/current status; no overwritten decision or duplicate gate call.
- Restart before/after request-task linkage: one recoverable request/task pair.
- Restart after task completion but before delivery: existing Outbox completes once
  per durable event identity; a conflicting identity is rejected.
- Draft/evidence changes, hash metadata corruption or artifact absence: fail closed.
- Tenant B cannot read or transition tenant A's request, task or bound artifacts.
- Self-approval restrictions and captured-policy semantics survive rehydration.
- Provider failure never activates against an in-memory fallback.
- Gate unknown outcome is reconciled according to an explicitly idempotent contract.

Reuse the ControlPlane.JsonContracts native publish/link/run fixture for new durable
contracts with reflection fallback disabled. Add real PostgreSQL race/restart tests
and extend provider NativeAOT evidence only for the provider boundary actually
exercised. A JSON-only test does not establish native persistence or deployment.

## Concrete work before coding stage 2

Resolve these implementation choices from existing APIs and a reviewed contract:
(1) immutable artifact envelope and generated serializer ownership; (2) whether
HumanTask creation can enlist and reuse stable identity in the shared transaction;
(3) request-store CAS and decision-replay result shapes; (4) durable audit/activation
intent ownership. This design does not yet invent those API signatures. No user
business approval or external deployment is implied by implementing the platform.

## Request-store contract direction

The storage aggregate is internal governance state, not a new caller-supplied
approval DTO. Keep ActivationRequest's existing public lifecycle meaning. The
store boundary needs these operations/typed outcomes; exact names remain reviewable:

- Create-if-absent under tenant + explicit submission operation identity. A replay
  of the same operation and bound semantic input returns the original request;
  reusing it for different content/policy/actor returns conflict. CorrelationId is
  trace grouping, not a documented idempotency key, and cannot substitute for this.
  Current SubmitActivationRequestRequest has no operation key; define compatibility
  for existing calls explicitly rather than claiming they are retry-safe.
- Read by tenant/request ID returns immutable request/owner/binding state plus
  storage revision, task linkage and applied decision/event metadata.
- Conditional transition returns Applied, Duplicate, Conflict or NotFound with
  an appropriate current snapshot. Store performs comparison/write atomically;
  lifecycle service supplies the authorized transition. A stored decision replay
  marker is committed with its winning transition, never in a later best-effort write.
- Task linkage is immutable once established. Failure to attach the task must roll
  back the transaction or leave a recoverable intent; it cannot be ignored.

Artifact persistence cannot be achieved by only swapping the existing
IActivationBindingArtifactResolver implementation: its StoreReviewHashes,
StorePackageHashes and StoreEvidenceHashes are synchronous void methods, while
its purpose is current hash resolution, not storing executable package content.
Replace caller writes with an asynchronous artifact persistence boundary and have
the existing resolver read the same authority. No sync-over-async database adapter
or hash-only surrogate may masquerade as retained review/package/evidence content.
A migration must update tool-service preview dictionaries/resource resolution too;
otherwise restart still fails before request creation. The exact source-generated
artifact envelope is a remaining contract task, not silently settled by this plan.

## Resolved task-creation boundary

The selected PostgreSQL path uses one shared transaction, not a new task reconciler:
PostgreSqlRuntimeTransactionCoordinator.ExecuteAsync joins its ambient accessor;
AddCrestCreatesPostgreSqlRuntimePersistence registers coordinator/accessor as shared
singletons. DefaultHumanTaskRuntime.CreateAsync prepares then calls AddAsync, and
PostgreSqlHumanTaskInstanceStore enlists through that coordinator. The new request
store must use the same provider/coordinator and support joining this boundary.
Move the current sequential request-then-task orchestration under one explicit
transaction; bind the deterministic task identity in the initial request aggregate.

HumanTaskCreationRequest.InstanceId supports caller-supplied stable identity. Derive
it from the stable request identity within tenant scope. CreateAsync remains
insert-only: after unknown commit, read and verify the existing request/task instead
of blindly inserting again. Verify task descriptor/version, tenant, typed request
input, full bound hashes and required completion-consumer obligations before
accepting it as the same task. RuntimeTransactionCommitUnknownException must trigger
read-after-unknown resolution using the persisted submission operation identity.

Do not wrap the entire preceding authoring/review pipeline in this transaction.
PostgreSqlDescriptorDraftStore.SaveAsync uses ExecuteTopLevelAsync, which deliberately
rejects ambient transactions for its separate ownership semantics. The proposal and
immutable governance artifacts must already be prepared under their own boundaries;
this transaction commits the activation request, its task/link and durable audit or
outbox intent. It must not change the draft-store contract to make nesting convenient.
