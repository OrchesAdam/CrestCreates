# Asset proposal retention and review replay

Issue #87 requires an attributable proposal across the governance chain. PR #100's
live result retained a sanitized summary, not the generated typed proposal. This
slice tests the existing formal PostgreSQL draft-store boundary with a separate,
deterministic offline fixture. It does not reinterpret the live summary as a
replayable proposal.

The test parses the existing 7g.v1 HumanTask candidate through the real parser and
reviews it through the real Control Plane service. It saves the reviewed typed
draft through IDescriptorDraftStore using both PostgreSQL Runtime and Control Plane
reference-data registrations. The original persistence provider is disposed before
another provider reads the same tenant/draft from the same migrated isolated schema.

Assertions compare the whole draft, its concrete HumanTask payload and complete
contract/definition CanonicalHash structures. They also check identity, author,
operation, versions and Reviewed status, and ensure another tenant cannot read it.
The original review harness is disposed; a fresh harness reviews only the reloaded
draft and checks validation, materialization, governance and stable hashes again.

## Evidence scope

On 2026-09-22 the focused test passed 1/1 and the full Asset E2E suite
passed 34 tests with the single opt-in live test skipped. Logs:
`/tmp/crest-proposal-replay-focused-r3.log` and `/tmp/crest-proposal-replay-full.log`.
An initial compile failed on missing namespace imports, which were corrected.
A subsequent test-host launch was blocked by sandbox socket permissions before
tests ran; the authorized local-socket run then passed.

The fixture uses the established
ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING and a temporary itest schema. It does
not introduce another persistence format, reflection fallback or production API.

This is an in-process provider reconstruction test, not a process crash test or a
production deployment claim. The schema is cleaned up after testing. No live model
is called, no proposal remains available for later human approval, and no approval,
activation receipt or runtime registry is restored or changed. Existing PostgreSQL
provider durability tests remain separate evidence. NativeAOT capability is not
newly claimed by this test-only change.

The next design decision remains how an intentionally retained live proposal is
handed to subsequent human governance with explicit storage lifetime and ownership.
Workflow evolution and production activation ownership still prevent closing #87.
