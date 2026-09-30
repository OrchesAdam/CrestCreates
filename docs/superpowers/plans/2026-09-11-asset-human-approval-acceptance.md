# Asset human approval acceptance

Primary owns design and review; GPT-5.6 Luna high implements. Parent PR #94
at 95312106 remains under final CI 34569935387 in its own worktree.

First prove the newly introduced Asset initial HumanTask draft through actual
Create, Review, Preview/Evidence, Submit, HumanTask completion, Outbox consumption
and authoritative activation-request state. Use actual metadata/draft/control-plane
and runtime services. A fixture policy provider may require human review for all
requests and forbid self-approval; it must not manufacture governance decisions.
Blocked reviews remain blocked. Cover human approval and human rejection.

Observe package-builder inputs/output and created review task IDs through existing
interface decorators that delegate to real owners. Do not add production hooks,
manually store hashes, call direct approval APIs, or mock successful decisions.
The in-memory runtime gate records activation and does not deploy an application.
No v2 runtime host is loaded in this first slice.

Readonly findings establish a multi-draft authoring container and sequential
materialization through existing owners. The Company Certification runner is only
a composition reference: its manual Allowed decision/direct approval are not
usable proof. It does not establish durable aggregate approval authority.

Subsequent work must bind every changed draft and exact final executable inventory
to authoritative approvals before host construction. Adding initial HumanTask then
updating Workflow cannot silently treat an unapproved dependency as existing
approved content. Neither a single request's Activated status nor a final inventory
report proves that all changes were approved. Investigate this association with
executable evidence before proposing a new platform contract. Keep control-plane
restart, durable deployment and live-model quality outside this bounded proof.

## Authoring input boundary check (2026-09-13)

The generated HumanTask editing contract preserves Outcomes with CreateDefault;
the Asset candidate declares Approve and Reject. First verify the actual created
payload rather than accepting matching ID alone. Do not repair the payload after
tool creation or weaken the candidate semantics to make the test pass.

The existing JsonDescriptorAuthoringOutputParser already parses HumanTask outcomes
and LlmDescriptorAuthoringAgent invokes it. A Tool Create limitation is therefore
not by itself proof of a missing framework authoring capability. If confirmed,
use the existing authoring result -> draft store -> real review/package/approval
boundary as the next experiment, clearly distinguishing it from the constrained
editing DTO. Do not introduce a new parser, fallback or editable wire contract.

## Later checked-content host entry

Readonly review found that AssetCandidateWebApplicationFactory currently rebuilds
its registries from static candidate catalogs. A later acceptance fixture must
instead accept the externally checked inventory and build HumanTask/Workflow
registries and descriptor lookup from exactly those objects. Keep v1 pins in the
inventory. The current runtime Workflow service consumes its injected registry,
so no production execution API is needed for this bounded experiment.

AssetMaintenanceTaskContractResolver intentionally compares task pins against
compiled Asset task contracts. Preserve this check: the bounded initial task must
match the compiled contract exactly, including definition hash. Do not replace the
sealed resolver or relax pin checks to accept arbitrary approved task content.
The later proof is approved selection of this compiled business contract and a
governed Workflow change, not arbitrary HumanTask deployment. Static compiled
handlers remain the execution authority. Verify the handoff with actual retained
and rehashed package contents before introducing any fixture inventory injection.
