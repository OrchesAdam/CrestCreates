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

Commit efd301fe implements the generated repair. Create now requires descriptorId
and rejects blank IDs; Merge retains the existing ID and rejects invalid existing
identity. The production caller passes request.DescriptorId. There is no old
identityless overload and no new editable Id DTO field. This is a C# projection
signature change; callers must supply identity explicitly. Version behavior is
unchanged.

DraftContracts passed 36/36 and the related ControlPlane focused set passed 36/36.
The primary independently reran the original two regressions: 2/2 passed.
Validation commit 46c43cfd covers Create and name-changing Merge for Capability,
Workflow, HumanTask, Form, Event and Schema. The retained-content regression now
executes real Create -> Update -> Preview without fixture identity repair; the
primary independently reran both tests successfully after restart.

Full ControlPlane passed 553/553, CodeGenerator 283/283, and the focused draft
generator suite 15/15. The existing NativeAOT wrapper passed 1/1 after publishing,
linking and executing the fixture, including AgentDraftIdentityBinding:PASS.
The primary also independently inspected and executed the ELF artifact.

The complete local dependency-boundary run passed 165/170. Two failures require
the preceding executed-evidence suites (581 tuples were absent), and three could
not connect to Docker for PostgreSQL schema checks. These are unresolved local
validation prerequisites, not waived gates. CI already runs the evidence-producing
suites before the ledger gate and provides Docker; final-head CI remains required.
PR #94 stays draft until that result. PR #93 is ready and unmerged with final CI
34442962164 successful. HumanTask-approved Asset handoff remains subsequent work.
