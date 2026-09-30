# Review record persistence

The Control Plane now uses IAgentReviewArtifactStore for review creation, get/list,
latest-report generation and activation submission's review reference checks. Its
binding resolver derives original review hashes from the same retained inputs;
the independent review hash dictionaries and scalar writer are removed.

The versioned record retains the projected DTO, captured report facts, original
canonical hash inputs, timestamp and scope. Report owner facts remain those captured
at review time. Original and projected inputs are distinct: visibility projection may
change readiness, diagnostics and governance. Full typed hash metadata is recomputed
through the existing canonical service, not trusted from supplied hash strings.

The PostgreSQL provider is selected explicitly with
AddCrestCreatesPostgreSqlReviewArtifactStore after the base provider registration.
Development stubs use the same contract with JSON detachment. Formal provider failures
do not fall back to memory. V014 is additive; key, draft, timestamp and version columns
are checked against decoded content. Insert collisions cannot overwrite records.
Latest selection precedes scope checks and never silently chooses an older review.

AgentReviewArtifactFactory centralizes visibility projection and capture. ToolService
uses its already-authorized scope; the Company Certification producer passes its
configured policy and exact sequential-review inventory. The factory grants no
authorization and performs no storage. This avoids re-reviewing against a changed
catalog or retaining a scalar-hash backdoor. External callers must adapt to the
removed resolver write method and new required ToolService/resolver dependencies.

This slice makes review records durable. Package/evidence previews, activation
requests, human-review task linkage and runtime installation are not made durable by
it. Restart may still leave activation submission without its package/evidence refs.
The only supplied activation gate remains in-memory; no production deployment occurs.

## Validation status

Prior real PostgreSQL focused coverage passed12/12, including second-provider recovery,
full report rebuild, original hash retention, tenant isolation, deterministic order,
collision, corrupted columns/payload, provider outage and DI order. Full PostgreSQL
regression passed448/448 in5m24s. Prior CP564/568 exposed four old report-builder mock
overloads; corrected tests preserve owner/status/version and scope assertions, and
full Control Plane regression now passes568/568. Architecture boundary checks pass
153 plus3 database-backed tests after supplying the external test database. The
separate CI evidence-ledger gate is not included in those156. Company Certification
sample tests pass34/34. Actual NativeAOT publish/link/run passes1/1 in1m37s.
Final full CI, including JSON ownership and evidence-ledger gates, remains pending.

Native fixture evidence covers the full envelope and actual ToolService
recreation over the memory provider. It does not establish NativeAOT PostgreSQL access.
No new model invocation, retained proposal mutation, approval, activation or merge.
