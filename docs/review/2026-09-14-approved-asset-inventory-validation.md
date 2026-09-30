# Approved inventory boundary validation

Local verification in the isolated asset-approved-inventory-87 worktree:

- ControlPlane acceptance filter AssetHumanApprovalActivationHandoffAcceptanceTests: 3 passed.
- Full CrestCreates.Agent.ControlPlane.Tests: 556 passed.
- Asset E2E filter CheckedInventory: 2 passed.
- Full Asset E2E: 12 passed, using isolated PostgreSQL 16 plus the sample SQLite store.
- git diff --check passed.

Builds used --disable-build-servers -m:1 -p:UseSharedCompilation=false and
-p:NuGetAudit=false for the documented audit-network limitation. Repository audit
defaults remain unchanged. The new worktree required building CrestCreates.BuildTasks
first. Initial compilation failures (missing namespaces and unrecognized inline
descriptor refs) were repaired before the reported passing runs.

The positive package check queries the real activation request after a real
HumanTask completion and hosted Outbox callback, then recomputes complete package
hashes from retained definitions. Its association is fixture-local and serial.
The changed-definition negative reaches hash verification and fails there.
The independent loading test executes a supplied Workflow whose definition hash
differs from the static candidate; fixed compiled contracts are still enforced.

These tests do not yet prove combined two-draft approval-to-host loading, durable
control-plane restart, deployment, arbitrary HumanTask contract installation, or
live model quality. Final-head CI is required before marking the PR ready.
