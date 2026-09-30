# Review artifact authority cutover

Status: implementation design, not yet implemented. Builds on PR106. This slice
persists the review domain only. Package/evidence previews and activation requests
remain separate in-memory domains; after restart submission may still fail because
those references are absent. This is not durable pending approval or production
activation. The larger durable activation design remains in force.

## Single authority

Add an asynchronous review artifact store in ControlPlane.Abstractions and a real
PostgreSQL implementation. Remove ToolService's review dictionary. Creation, get,
list, latest report and submission review-reference checks all use this store.
The binding resolver derives original review hashes from that same artifact;
remove its review hash dictionaries and synchronous StoreReviewHashes writer.
Package/evidence hash ownership is unchanged in this bounded slice. No database
failure may fall back to memory. An explicit memory provider serves development
through the same contract, not a parallel mirror. Follow existing draft-store
registration-order conventions when selecting PostgreSQL.

## Exact retained inputs

Store one versioned immutable envelope: tenant/review ID, CreatedAt, opaque scope
fingerprint, projected AgentReviewResultDto, PR106 report input snapshot and ORIGINAL
DescriptorDraftReviewHashInput from PR104. Reuse report input's finite owner instead
of duplicating owner fields or serializing full DescriptorDraft. Submission uses
typed captured projected governance MaxDecision, preserving current semantics.

The resolver recomputes SourceReview and ReviewManifest hashes using the existing
hash service and the ORIGINAL captured input. Do not persist only caller-supplied
hash scalars, substitute projected report inputs, or regenerate a review against a
new baseline. Original and projected diagnostics may legitimately differ.

Use an explicit-root source-generated JSON envelope with explicit null handling.
Validate versions, required structures and tenant/draft identity across original
input, report input/owner and projected DTO. Check returned payload identity and
timestamp against database keys/columns. Decode/mismatch fails closed. This is
storage integrity, not authenticity against a privileged database editor. No runtime
descriptor interfaces, topology indexes or abstract draft payloads are retained.

## Operations and cutover

Insert immutable tenant/review records; collision must not overwrite. Support exact
get, tenant list with optional draft filter and latest tenant/draft query. Sort latest
deterministically by capture time plus ID. Select latest overall BEFORE scope check;
never fall back to an older matching-scope review. Memory implementation must isolate
stored mutable collections from callers through the same codec or defensive copies.

Preserve current get/list/report authorization, captured owner kind and exact scope.
Lists retain their current fail-closed current-owner resolution. Reports build from
captured input with lazy template/clock. Submit uses the stored review identity,
scope and typed governance. The write-only _reports dictionary can be removed after
verifying references; archive its unused snapshot under 99_RecycleBin.

Persist the complete artifact before success. Draft SaveAsync remains its own
top-level transaction boundary; do not wrap it in ambient provider transactions.
Failure saving draft status can leave immutable review evidence but must not return
tool success or imply atomic cross-domain request creation.

Provider: follow PostgreSqlControlPlaneReferenceDataPersistenceServiceCollectionExtensions
for kernel validation/provider selection. Add migration after V013; do not edit old
migrations. Use existing Npgsql support, table-prefix and transaction coordinator.
Do not reuse the private full-draft codec for this finite artifact.

## Verification

Full Control Plane regression plus real PostgreSQL two-instance/restart tests:
get/list/latest, full fixed-clock report recovery, original hashes, tenant isolation,
scope denial and latest-wrong-scope behavior, immutable collision, corrupted identity
and decode failure, and no memory fallback when provider fails. Fresh test schemas
only; preserve retained live proposal container/schema and avoid all model calls.

Extend actual NativeAOT publish/link/run to full artifact JSON and real service/store
routing. State PostgreSQL-native evidence separately: memory-native or JSON-only
checks do not establish native database persistence. No approval, activation or merge.
Root designs/reviews; GPT-6 Luna high codes. Exact-final-head CI required before ready.

## Authoring producer migration

The Company Certification scenario already computes sequential reviews against an
evolving proposal inventory. Re-reviewing against a runtime catalog would change
that evidence, and writing scalar hashes would retain the removed second authority.
Use one platform AgentReviewArtifactFactory in both ToolService and this trusted
authoring producer. It projects the already-computed original review with its actual
inventory and authorization policy snapshot, captures report/DTO/original hash inputs,
and returns a validated envelope. It does not authorize or persist. ToolService uses
its existing frozen scope/universe internally; the producer uses its configured
policy and exact staged inventory. No runtime registry mutation or fake fingerprint.
Artifact IDs must remain unique for distinct runs; registry references must bind the
actual captured IDs. Validate affected golden scenario behavior, not only compilation.
