# CrestCreates current handoff

Last updated: 2026-09-22. This summary supersedes the dated status entries in
[the preserved historical memory](docs/review/2026-09-20-memory-history.md).
AGENTS.md remains the instruction entry point; history remains evidence, not a
second source of current task status.

## Goal and working rules

The product goal is to let AI safely create and evolve enterprise applications.
GitHub Issues are the roadmap. Issue #87 is active; #88 decisions remain provisional.
The primary agent designs, reviews and executes verification; GPT-5.6 Terra high
codes (user authorized replacement on 2026-09-22 because Luna is unavailable).
The earlier Luna medium repository-reconnaissance preference has not been changed. Use GitHub PRs.
Do not automatically merge. Keep the generated/typed NativeAOT mainline and existing
authorization, tenant, approval and evidence authorities. Never patch parsed draft
envelopes inside a sample to disguise an authoring defect.

## Current workspace and published evidence

- Active worktree: /home/orches/workspace/CrestCreates/.worktrees/asset-proposal-replay-87
- Branch: codex/phase-10c-asset-proposal-replay-87, based on PR #100. Offline
  proposal retention/review-replay acceptance is being implemented; no PR yet.
- Bounded context acceptance is in PR #100: https://github.com/OrchesAdam/CrestCreates/pull/100.
  Final head02459ad1b6768cee8f6ad967188e42e105cedf0f; CI35613490902 passed (verified 2026-09-22).
  Marked ready on 2026-09-22 after exact-head success; preserve PR100 source worktree.
  Check GitHub for current status rather than treating this pre-CI snapshot as final.
- PR #99 is ready, unmerged: https://github.com/OrchesAdam/CrestCreates/pull/99.
  Exact head3004b888a15b70e64161ffa9d83bc22630bd4ce3 passed CI35564431378.
  Local Authoring66, ControlPlane556 and native1 passed. Preserve its head.
- PR #98 is ready, unmerged: https://github.com/OrchesAdam/CrestCreates/pull/98.
  Head 1efbe693907cd41d62aa53b08ed79e4e65345a4b; CI35547798046 passed.
  Its worktree asset-live-eval-87 is clean; preserve the tested head.
- PR #97 is ready, unmerged: https://github.com/OrchesAdam/CrestCreates/pull/97.
  Final head 08fe989a1adefcc50dc0bb9a505dea6401a9204d passed full CI
  35500739729, verified against the PR head before marking ready.
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
- PR #97 is complete and ready. Preserve its verified head.
- The successor adds a default-skipped, opt-in live authoring probe. First real
  DeepSeek request failed with ProviderUnavailable and zero drafts. See
  docs/review/2026-09-20-asset-live-authoring-baseline.md and its JSON evidence.
  Safe HTTP response metadata observation is being tested to distinguish empty
  provider content from parser/review failure. No automatic retry or activation.
- Then address restart, bounded live-model evidence and #88 decisions as demonstrated
  needs; do not declare #87 complete from a test-only Host handoff.

## Material limitations

Only InMemoryRuntimeActivationGate exists. It validates/records a development
activation receipt and does not install an inventory or mutate runtime registries.
Activated therefore does not mean production deployment. Production activation
ownership remains a concrete gap requiring a bounded business case and design.

DeepSeek model choice is deepseek-v4-flash; DEEPSEEK_API_KEY was confirmed present,
never printed. The first three live model requests failed before producing drafts; the fourth
bounded-context run succeeded as recorded below.
The requested name remains deepseek-v4-flash; provider returned deepseek-flash.
Official docs now map this legacy request name to V4.1 Flash. Reuse the existing OpenAI-compatible
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
14/14; PR #97 final-head CI subsequently passed and it was marked ready.
No reset credit used.

## Live evaluation successor — 2026-09-21

Source: AssetDeepSeekLiveAuthoringEvaluationTests.cs, explicit opt-in only.
Local full Asset suite passed31/skipped1 after response-observer and budget checks.
Default does not call DeepSeek. Requests use only public synthetic descriptor
metadata, fixed test intent, new empty memory store. Credentials never logged.

Three actual provider requests have run: initial empty-content baseline; one
instrumented4096-budget run (HTTP200, length, all4096 completion tokens reasoning,
no content); one16384-budget run (HTTP200, stop,3650 content chars,1687 completion
including797 reasoning tokens), rejected by real parser as InvalidProviderOutput.
Zero drafts, no approval/activation. A separate observer composition failure sent
no HTTP and is recorded separately. Its typed HttpClient registration was repaired
and tested offline. No further baseline live call is needed.

Evidence and limitations: docs/review/2026-09-20-asset-live-authoring-baseline.md
and linked JSON. The exact parser envelope rejection branch was not retained;
do not claim a specific missing field. Default prompt names7g.v1 without complete
wire schema; context has only workflow/task refs, no Form or descriptor bodies.
Next design should improve bounded authoring protocol/context disclosure before
proposing new agent orchestration. PR #98 contains the live baseline; full CI passed at its exact head.

The 2026-09-21 13:19 Asia/Shanghai heartbeat fired after the actual reset.
Fresh quota was5h0%, weekly47%; the one-time automation crestcreates was deleted.
No active wake remains and no reset credit was used. Schedule the next wake from
actual limits if the5h window is exhausted again.

## Bounded context successor — current work

Plan: docs/superpowers/plans/2026-09-21-asset-bounded-context.md.
The real context acceptance passed1/1 after matching the active Metadata Workflow
extractor's Uses/noRole taxonomy. FormRelationshipExtractor already exists under
Framework/Modules and is registered by AddFormKernel; the initial missing-extractor
claim was false and was corrected without production changes. Runtime's separate
Workflow extractor uses Triggers/role labels; provider first-match ordering remains
a separate consistency concern documented in the review, not silently patched.

A test-owned shared RuntimeScenario recipe now selects exactly Workflow, HumanTask,
Form and maintenance-decision Schema, with3 real edges, bounded depth/count,
stable hashes and no unrelated descriptors. The live probe uses this recipe and
safe fixed diagnostic categories. Full Asset PostgreSQL suite passed33/skipped1
(/tmp/crest-bounded-context-full.log).

One live call with promptv2 and this4-item context passed1/1:
/tmp/crest-live-context-v2.log, /tmp/crest-asset-live-eval-context-v2.json.
Real parser Succeeded, exactly one expected HumanTask Create v1, actual deterministic
review/materialization clean, task contract checks passed. HTTP200/stop;1661 prompt
and3304 completion tokens (2955 reasoning). Requested model deepseek-v4-flash,
observed deepseek-flash. Evidence committed with this slice is a sanitized run
summary, NOT the typed proposal, approval, activation or deployment. One sample
is not a reliability guarantee. Both context and protocol changed from baseline.

PR #100 final-head CI is verified successful and the PR is ready. No further model request needed for this slice. Later #87 work still needs
retained proposal-to-approval/replay evidence, Workflow evolution and a production
activation owner. Do not inflate this result to full #87 completion.

Most recent fresh quota check:5h1%,weekly63%. No reset card used. No active wake.

## Offline proposal replay successor

Plan: docs/superpowers/plans/2026-09-21-asset-proposal-replay.md.
Terra high task terra_proposal_replay is implementing one offline Asset test using
existing PostgreSQL draft store and explicit one-of persistence codec. Real parser
-> review -> save exact typed draft -> dispose provider -> fresh provider same
isolated schema -> read/compare complete hashes and identities -> fresh review;
wrong tenant cannot read. No new serializer format, production API, model call,
approval or activation. Temporary schema cleanup means this is within-test restart
evidence, not a retained live proposal available for future human approval.
BuildTasks bootstrap passed (/tmp/crest-proposal-replay-bootstrap.log).
Primary must review implementation, run appropriate PostgreSQL tests, document
results and use successor PR. Keep live-run summary and offline fixture evidence
distinct. DescriptorAuthoringJsonSerializerContext alone cannot round-trip concrete
abstract DraftPayload; do not use it to claim lossless proposal retention. Existing
PostgreSQL codec/store is the chosen formal boundary; do not expose internal codec.

## Resume 2026-09-22

Fresh quota allowed work: 5h4%, weekly79%; no reset credit used.
Previous Luna coding attempt left no implementation files. Current tool rejects
gpt-5.6-luna as unavailable; user explicitly selected GPT-5.6 Terra, retaining high
reasoning. terra_proposal_replay is implementing the existing bounded offline plan.
PR100 exact-head full CI succeeded and it is now ready, unmerged.

The old /tmp runner and private credential file did not survive restart. Existing
container crest-asset-approved-inventory-87 was restarted without replacing data.
Private configuration recovered securely to /tmp/crest-asset-pg-20260922.env; runner
/tmp/crest-run-asset-e2e.py recreated. Do not print the private configuration.
BuildTasks bootstrap passed with 0 warnings/errors on 2026-09-22.
One-time heartbeat crestcreates is scheduled for 2026-09-22 13:40 Asia/Shanghai,
after the actual 5h reset13:39:25; consume/delete after it fires.

## Offline replay local verification — 2026-09-22

Terra high implementation is complete and reviewed. Focused PostgreSQL replay
passed1/1 (/tmp/crest-proposal-replay-focused-r3.log); full Asset E2E passed34
with1 opt-in live test skipped (/tmp/crest-proposal-replay-full.log).
The initial compilation lacked namespace imports; these were corrected. A sandbox
socket restriction blocked the first test-host launch; authorized execution passed.
Both persistence and review providers are rebuilt before read/review replay. Full
draft/concrete payload and complete contract/definition hashes match; wrong tenant
cannot read. Only offline synthetic proposal data was used, temporary schema cleaned.
No live model call, approval, activation or deployment occurred.
Evidence: docs/review/2026-09-22-asset-proposal-replay.md. Successor PR/CI publication
is next; do not mark ready until its exact final head passes the full workflow.
