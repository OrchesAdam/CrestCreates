# Generated draft identity binding baseline

The earlier retained-content hash test at ec2aea6d passed, but used a fixture
payload with empty identity and did not prove that request intent survived real
creation. Do not interpret that green test as correct authoring or handoff.

Commit 8b287175 supplies valid editable payload Version=1 and envelope
ProposedVersion=1. The primary independently ran the focused suite against
unmodified production code: 2 tests, 2 failures.

| Actual main path | Required identity | Observed identity |
| --- | --- | --- |
| Real CreateDescriptorDraftAsync -> PreviewDescriptorPackageAsync -> materializer/package builder | candidate.event, version 1 | empty ID, version 1 |
| Generated Merge changing only Name on a valid existing event | existing.event | empty ID |

Full output: `docs/review/2026-09-11-draft-identity-baseline.log`.

The immutable Id is intentionally absent from editable DTOs. The generated Create
has no identity input, and both Create/Merge omit Id in their new descriptor
initializers. Version is already editable and the draft validator checks its
agreement with ProposedVersion; version omission in the initial fixture was an
input error, not a missing projection feature.

The fix belongs in the existing generator projection boundary. See
`docs/superpowers/plans/2026-09-11-generated-draft-identity-binding.md`.
No test-side identity repair, runtime reflection fallback, editable Id field or
second materializer is authorized by this evidence. The baseline uses real
control-plane creation/preview, materialization, packaging and canonical hashing
with controlled store/catalog/review collaborators. It does not prove full review,
authorization, runtime handoff or durable deployment.

Production repair, relevant full suites, native execution and final-head CI remain
pending. PR #93 is ready and unmerged with final CI 34442962164 successful.
