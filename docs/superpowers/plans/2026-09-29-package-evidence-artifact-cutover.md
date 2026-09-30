# Package and evidence artifact authority

Status: implemented; final regression and PR validation in progress. Follows PR107. Review
records are now durable; this slice makes package/evidence previews recoverable from
the same configured provider. Activation requests, review task linkage, delivery
replay and installation remain separate work. Do not claim durable approval yet.

## Problems and selected boundary

ToolService owns package/evidence dictionaries and a latest-package pointer; the
binding resolver separately owns hashes. Restart loses both content and identity.
Evidence snapshots do not retain their actual package preview ID or scope. Submission
can therefore mix a package with evidence from a different build of the same draft
if independently supplied hashes match the two slots. Persisting hashes alone does
not fix either problem.

Introduce one asynchronous IAgentPackageArtifactStore in ControlPlane.Abstractions
with explicit development-memory and formal PostgreSQL providers. Remove package/
evidence dictionaries and latest-pointer authority from ToolService and all scalar
StorePackageHashes/StoreEvidenceHashes methods and resolver hash dictionaries. The
resolver becomes the default store-backed resolver; core composition can register it,
while memory stores remain exclusively explicit development stubs. Archive replaced
files. No fallback or parallel mirror.

## Immutable records and serialization

Package artifact: version, tenant/preview ID, captured timestamp, finite owner
(tenant/draft/descriptor ID+kind/operation/author/status/base+proposed versions), scope
fingerprint, captured draft version, visible-catalog fingerprint, projected preview,
and PackageJson from the EXISTING IDescriptorPackageSerializer. Do not serialize full
DescriptorDraft or define a second package wire format. Owner is finite authorization
and binding data, not a report or executable payload.

Evidence artifact: version, tenant/evidence preview ID, captured timestamp, exact
PackagePreviewId, captured owner/scope/version and projected evidence DTO. Its parent
package is immutable and contains the full evidence and canonical envelope. Do not
duplicate a second independently mutable package or hash authority in the evidence
row. Reuse path retains the selected existing package preview ID; new-build path
persists package and linked evidence atomically. No link inferred from audit order,
same draft identity, latest selection or equality of hash strings.

Both records have explicit source-generated envelopes and required-null handling.
Keep official package serialization as the inner retained string. Validate key and
tenant/draft/owner/scope/version relationship on every provider read. Do not restore
against the current draft/catalog or regenerate content under the old artifact ID.

## Integrity and canonical identities

Recompute the unchanged official three package hashes using
IDescriptorPackageCanonicalHashComputer.ComputeHashSet(manifest,evidence,envelopeMetadata).
Verify full CanonicalHash metadata/value, payload Hashes, EvidenceEnvelope's repeated
metadata/hashes, and owner/package identity/version/author/source timestamps where
their existing builder semantics require equality. Do not use obsolete pipe hashes.

These three hashes do NOT cover SnapshotData or Diagnostics. Add a separate versioned
artifact-content integrity profile through the existing canonical hash infrastructure
(not an ad-hoc string hash or changed package v2 profile). Its canonical input includes
the exact official PackageJson string and immutable envelope identity/owner/scope/
version/fingerprint metadata, excluding its own digest. Store its full CanonicalHash.
Use explicit typed projection/writer, InternalFull/Integrity semantics and a dedicated
shape version. It is a storage-content check, not approval or authenticity against a
privileged writer capable of replacing both content and digest. Evidence projections
need equivalent integrity coverage or must be deterministically verified against
captured parent content and scope; choose one shared envelope integrity service.

Additionally validate SnapshotData identity/version/entries against manifest and
builder snapshot-ID convention. Relationships and diagnostic content are covered by
the content profile even though absent from the legacy package hash set. Preserve
canonical package hashes unchanged; artifact hash is not interchangeable with them.
Deserialize through the official serializer and validate before returning content.
Use the same platform capture/validation service for memory, PG and trusted sample
producers; do not move domain validation into SQL or copy the algorithm per provider.

## Store operations and service routing

Store operations must support immutable package insert, atomic new-package+evidence
insert, evidence insert referencing an existing package, exact tenant-keyed gets and
latest reusable package query. Collision never overwrites. Parent existence and
tenant/draft/scope/version association are enforced transactionally. Use deterministic
capture-time plus ordinal ID ordering for reuse; freeze the selected ID in evidence.
The PostgreSQL reuse index includes tenant/draft/time/ID only; scope/version/catalog
remain exact query predicates. Catalog fingerprints can be larger than a PostgreSQL
btree key, so they must not be included in that index.
All memory-provider reads detach mutable graphs, and atomic operations match formal
provider semantics. PostgreSQL uses additiveV015 and existing transaction/kernel/
strict-schema conventions. No migration edits to V001-V014.

ToolService package create/reuse/get and submission checks use this authority. Add
submission diagnostics for evidence scope and package-association mismatches; fail
before request creation, task creation or gate activity. Exact tenant isolation and
owner kind checks continue. Resolver recomputes hashes from validated parent content
and requires the submitted package/evidence pair match; missing/corrupt data fails
closed. Do not let caller-provided hashes establish relationships.

Evidence responses should expose exact package/evidence preview IDs in their own
ControlPlane DTO, rather than requiring consumers to infer a pair from audit arrays.
Preserve existing diagnostic/audit fields and visibility projection; do not push
ControlPlane IDs into generic Metadata preview contracts. Update golden authoring
sample producers to persist actual package content via shared platform factory,
using their existing policy and exact review inventory. No made-up scope strings,
no new runtime catalog mutation, no scalar-hash backdoor.

## Required evidence

Meaningful service tests: both new-build and reuse paths retain exact parent link;
same-draft cross-build pair and wrong-scope evidence are rejected before activation;
cross-tenant IDs fail; provider outages never fall back to cached/hash-only data.
Real PostgreSQL two-instance recovery must rebuild exact preview/evidence and original
hashes, test atomic pair rollback/collision, missing/mismatched parents, structured
column corruption, and tampering only snapshot relationships/diagnostics. Include
full-hash metadata tampering and immutable/detached memory behavior.

Extend actual NativeAOT publish/link/run for envelopes, official package serializer,
integrity profile and real service read/submit boundary, clearly distinguishing memory
service/native from PostgreSQL-native evidence. Run full CP/PG/boundary regressions and
affected samples. No model call or retained live proposal mutation. PR workflow and
final-head CI, never merge. Root designs/reviews; GPT-6 Luna high implements.
