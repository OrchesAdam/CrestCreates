# Approved Asset inventory handoff

PR #95 exact head 2d0dbfc8da6af5ed22b500dd2b0c7a76a2c9c374 passed full CI
34794889411 and is ready/unmerged. Work continues on an isolated successor branch.

The existing complete authoring -> review/evidence -> HumanTask -> Outbox path is
verified through authoritative Activated/Rejected request states. The next proof
must associate the checked executable definitions with that approved request and
load those exact objects into a fresh Asset host.

Install a fixture-only decorator over the real package builder before preview.
Associate its actual input/output with the preview ID returned by that same tool
call and tenant context. Query authoritative request status and binding; do not
accept a caller-created success flag. Rebuild the manifest from retained executable
definitions and recompute all three canonical package hashes. Detect changed
definitions even when stored claimed hashes are unchanged. Reject wrong tenant,
preview/request substitutions and missing required approvals before host creation.

Extend the existing test factory with an explicit inventory constructor. Its
explicit path builds HumanTask/Workflow registries and descriptor lookup from
that supplied set, without retrieving a static candidate again. Retain compiled
handlers and the existing full-pin HumanTask contract resolver. The no-argument
factory remains a convenience for existing candidate-only tests; it is not an
approved-content oracle.

Workflow replacement packages and retained v1 runtime pins need separate evidence.
Do not append arbitrary historical descriptors outside verification. Establish the
host set as exactly the union of checked already-deployed baseline versions and
approved new versions, or use a stronger existing owning API if available. Verify
each component's full ref/contract/definition identity. Keep control-plane lifetime
continuous; runtime restart is distinct from durable control-plane restart.

Primary owns design/review and execution. GPT-5.6 Luna high owns coding; Luna medium
handles bounded repository factfinding. No new production API is authorized without
an executable unmet requirement. A fixture loading hook alone does not prove
approval, deployment, durable restart or live-model quality.

## Resume checkpoint — 2026-09-14

The successor remains uncommitted and has no PR. The explicit inventory tests
initially could not build because the fresh worktree lacked BuildTasks output;
BuildTasks was built successfully. The next compile exposed CC1001 on inline
HumanTask references; Luna is repairing those references using existing catalog
contracts. No new E2E pass is claimed. Logs: /tmp/crest-approved-inventory-e2e.log.

The package verifier was reviewed and corrected to use complete CanonicalHash
record equality, with UnderReview, wrong-request, wrong-tenant, wrong-preview and
changed-definition rejection. Targeted CP test log:
/tmp/crest-approved-package-binding.log. Check its completion before rerunning.

Next composition should live in the Asset E2E assembly and reference production
ControlPlane/Authoring/Draft projects, not another test assembly. Extract a reusable
test harness from the current CP acceptance, retain real Review/Preview/Evidence/
Submit/HumanTask Complete/Outbox behavior. Approve and verify Initial HumanTask
first; only then add it to the fixture catalog used to review Workflow v2. Approve
and verify Workflow separately. Construct exactly the checked baseline plus both
approved definitions before creating the explicit-inventory Host. Do not equate
current separate package-binding and loading tests with this combined proof.

Dedicated PostgreSQL container: crest-asset-approved-inventory-87 on loopback port
55487; credentials are in /tmp/crest-asset-pg-20260914.env (never print). Reuse it.
At 95% five-hour usage, automation crestcreates was successfully updated for one
wake on 2026-09-14 18:55 Asia/Shanghai, after reset 18:54:32. No card redeemed.

At the 18:55 wake the quota was available (6% consumed); the one-time
automation was deleted successfully. Namespace/reference compilation issues
were repaired. The package-binding focused suite passed 3/3 and full control
plane suite passed 556/556. Explicit inventory E2E validation is still pending.

Final local verification: explicit inventory focused 2/2 and full Asset PostgreSQL
E2E 12/12 passed. Control-plane full remains 556/556. All changes in this slice are
test composition or documentation; production runtime is unchanged. Combined
approval-to-host remains the next slice, not a completed claim.
