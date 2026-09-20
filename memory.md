# CrestCreates current handoff

Last updated: 2026-09-20. This summary supersedes the dated status entries in
[the preserved historical memory](docs/review/2026-09-20-memory-history.md).
AGENTS.md remains the instruction entry point; history remains evidence, not a
second source of current task status.

## Goal and working rules

The product goal is to let AI safely create and evolve enterprise applications.
GitHub Issues are the roadmap. Issue #87 is active; #88 decisions remain provisional.
The primary agent designs, reviews and executes verification; GPT-5.6 Luna high
codes, and Luna medium handles bounded repository reconnaissance. Use GitHub PRs.
Do not automatically merge. Keep the generated/typed NativeAOT mainline and existing
authorization, tenant, approval and evidence authorities. Never patch parsed draft
envelopes inside a sample to disguise an authoring defect.

## Current workspace and published evidence

- Active worktree: /home/orches/workspace/CrestCreates/.worktrees/asset-approved-host-87
- Branch: codex/phase-10c-asset-approved-host-87, based on PR #96.
- PR #97 is draft: https://github.com/OrchesAdam/CrestCreates/pull/97.
  Implementation commit: 9217d79ebe43cc48d05f742b58c6ad6d75efd98d.
  Full CI was dispatched; verify the latest head before marking ready.
- PR #95 is ready, unmerged: 2d0dbfc8da6af5ed22b500dd2b0c7a76a2c9c374; CI 34794889411 passed.
- PR #96 is ready, unmerged: 9a6f9c0be08f356026f9377192595d2b33e2f7fa; CI 34836223554 passed.
- Earlier stacked PRs #91-#94 are recorded in historical memory. Preserve their
  verified heads. Do not change the PR #96 worktree while working on this successor.

PR #96 proves two separate boundaries: actual approved-package definitions are
checked against authoritative request hashes; a test Host loads supplied Workflow
objects and rejects invalid fixed compiled baselines. Local CP 556/556 and Asset
PostgreSQL E2E 12/12 passed, followed by full CI. This is not combined deployment proof.

## Successor changes and verified results

1. AssetControlPlaneApprovalHarness composes real parser, draft review, package
   preview/evidence, HumanTask completion and hosted Outbox with in-memory CP stores.
   It retains executable package definitions, rechecks full CanonicalHash structures,
   binds checked receipts to their harness/tenant, and uses current catalog topology.
2. Authoring Update could not express base v1 with proposed payload v2. An executed
   baseline had 1 pass/1 fail: BaseVersion expected 1, actual 2. The rejected fixture
   workaround (manually replacing BaseVersion) has been removed.
3. The existing 7g.v1 output item now has optional string baseVersion for Update.
   An explicit positive integer selects the existing version; absent input preserves
   existing same-version behavior. ProposedVersion still comes from the payload.
   Invalid values and fields on non-Update operations are rejected.
4. Verified after this repair: focused parser 8/8; full Authoring 64/64.
5. Two-draft acceptance now passes 2/2 on 2026-09-20. The Stale failure was
   caused by the harness binding DraftVersion=1 for a v2 proposal. Both bindings
   now use the validated actual ProposedVersion; production rechecker is unchanged.
6. Native fixture and wrapper contain AgentAuthoringUpdateBaseVersion:PASS;
   native publish/link/run passed 1/1 on 2026-09-20 with the new marker and all
   existing markers. ELF output was inspected. The earlier attempt had been rejected
   because weekly quota was exhausted; the successful run occurred after recovery.

## Next execution order

- Combined approved-package-to-Host test passed 2/2
  (/tmp/crest-approved-host-combined.log). Full Asset E2E passed 14/14
  (/tmp/crest-approved-host-e2e-full.log). PR #97 contains this reviewed change.
  The Host uses only the checked union of both approved inventories, retains v1
  from the first package and executes both business review stages.
- The separate two-draft acceptance, full ControlPlane and native wrapper are
  already verified; do not repeat them unless new changes justify it.
- Local regressions are complete. Mark PR #97 ready only after final-head CI succeeds.
- Then address restart, bounded live-model evidence and #88 decisions as demonstrated
  needs; do not declare #87 complete from a test-only Host handoff.

## Material limitations

Only InMemoryRuntimeActivationGate exists. It validates/records a development
activation receipt and does not install an inventory or mutate runtime registries.
Activated therefore does not mean production deployment. Production activation
ownership remains a concrete gap requiring a bounded business case and design.

DeepSeek model choice is deepseek-v4-flash; DEEPSEEK_API_KEY was confirmed present,
never printed. No live model evaluation has run. Reuse the existing OpenAI-compatible
authoring client and bounded ContextPack; do not add a general agent loop.
#50/#51/#76/#57 remain deferred; see the working gap assessment, not a final #88 closure.

## Reproducible environment and evidence

- Serialize dotnet builds: --disable-build-servers -m:1 -p:UseSharedCompilation=false.
- Local JIT commands used -p:NuGetAudit=false for prior audit-network limitations;
  repository defaults remain unchanged. Native wrapper inner publish uses normal audit.
- BuildTasks was bootstrapped in this worktree.
- PostgreSQL test container crest-asset-approved-inventory-87 was restarted on
  loopback port 55487 without replacing data. Private credentials were recovered
  to /tmp/crest-asset-pg-20260920.env; /tmp/crest-run-asset-e2e.py was recreated.
  Temporary files may disappear after restart; never print credentials.
- Historical logs (temporary files may be absent after restart):
  /tmp/crest-update-base-version-parser.log (8/8),
  /tmp/crest-update-base-version-authoring-full.log (64/64),
  /tmp/crest-approved-host-two-draft.log (2/2 after binding fix),
  /tmp/crest-approved-host-version-baseline.log (original version failure).
- Plans: docs/superpowers/plans/2026-09-14-approved-asset-host.md and
  docs/superpowers/plans/2026-09-15-authoring-update-base-version.md.
- docs/review/2026-09-15-authoring-update-version-baseline.log is committed evidence.

## Quota and wakeup

At the user's explicit request, the first action on 2026-09-20 was to create a new
one-time heartbeat, id crestcreates, for 16:46 Asia/Shanghai after actual 5-hour
reset 16:45:40. The wake was consumed and deleted successfully. Check actual limits
before future continuation; schedule a new wake if the 5-hour limit is reached. No reset credit has been redeemed
by this work; do not duplicate the user's earlier manual redemption.

Native evidence: artifacts/control-plane-json-aot-adeb8cb90c2c4963ad74685b846b1309
contains the ELF executable, publish.log and run.log. PostgreSQL container was
restarted without replacing its data; private credential file was safely recovered
to /tmp/crest-asset-pg-20260920.env, and /tmp/crest-run-asset-e2e.py recreated.

2026-09-20: fresh two-draft run passed 2/2; full ControlPlane regression passed 556/556
(/tmp/crest-approved-host-control-plane-full.log). Parent PR #96 was rechecked
OPEN/ready at the same verified head. The combined Host proof was reviewed, applied and passed 2/2. Its temporary
copy may lack subsequent namespace fixes; continue from the worktree source.

The 16:46 heartbeat fired, actual quota was available (0% consumed), and the
one-time automation crestcreates was deleted. Full Asset PostgreSQL E2E passed
14/14; all local planned checks for this PR slice are complete. Final-head CI is
still required. No reset credit used.
