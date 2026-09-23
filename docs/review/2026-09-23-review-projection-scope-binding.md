# Cached review and package visibility boundaries

The existing review path projects nested descriptor facts using the current
descriptor-kind policy, then caches that projection. Get/List/report only checked
the owner kind when reading it. Because policy is obtained on each invocation,
keeping the owner visible while denying a nested kind could disclose facts from a
previous broader projection. Package snapshots already captured a scope fingerprint,
but direct package retrieval and activation reference submission did not check it.

The selected fix binds cached data to the existing scope fingerprint. Direct reads
fail closed on mismatch, lists omit mismatched reviews with a trimming indication,
and report generation requires the latest review to match. Submission rejects
mismatched review/package references before activation-request creation. A fresh
review and package preview under the new policy provide the normal recovery path.
The rule also requires rebuilding after scope expansion; it deliberately avoids
adding another policy evaluator or changing the contents returned under an old ID.

Implementation is complete. Focused scope tests passed5/5; full Control Plane
regression passed561/561. The existing report tests now seed the captured scope
alongside the review-time owner. A real-service fixture passed its JIT preflight:
it creates and reviews an Event with a Schema reference, verifies the broad review
contains Schema facts, then denies cached review/package access and report building
after Schema visibility is removed. Restoring the original scope and reviewing
again recovers normal reads without changing the catalog. Native publish/link/run
verification passed1/1 in1m29s: linux-x64 Release executable emitted
CONTROL_PLANE_PROJECTION_SCOPE_NATIVEAOT_OK with reflection fallback disabled.

This change is a prerequisite for durable governance artifacts, not durable
persistence or production activation. The fixture uses explicit in-memory providers;
it neither touches the retained live proposal nor establishes provider durability.

## Remaining artifact design facts

Full DescriptorDraftReviewResult is not a supported persistence contract: its
inventories contain IDescriptor values, while AgentReviewResultDto loses details
needed by report generation. A finite report-input snapshot can preserve lazy
report construction and current template/time semantics without serializing arbitrary
runtime descriptor graphs. Eagerly storing the rendered report would change those
semantics and is not selected without an explicit contract change.

Report owner facts are TenantId, DraftId, DescriptorId/Kind, Operation, AuthorKind/Id,
Status, ProposedVersion and BaseVersion. Report rendering intentionally uses the
review-time owner, even though the formal draft is marked Reviewed after capture.
Authorization must not substitute an updated draft for this captured identity.
If full owner payload persistence is required elsewhere, reuse the formal PostgreSQL
draft codec and its six payload arms; the Authoring JSON context does not establish
an abstract-payload roundtrip contract. No second draft wire protocol is justified.

Original activation hash inputs (PR104), projected report hash inputs, retrieval
DTO and owner/scope identity must remain distinct. A locator or matching supplied
hash is not authority. None of these facts alone resolves request CAS, atomic
request/task creation, durable outbox acknowledgement or actual runtime installation.
