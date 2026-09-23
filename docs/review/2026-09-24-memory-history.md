# CrestCreates current handoff

Updated 2026-09-23. This summary supersedes status entries in
[the preserved history](docs/review/2026-09-23-memory-history.md).
AGENTS.md remains the instruction entry point.

## Goal and working rules

Let AI safely create and evolve enterprise applications. GitHub Issues are the
roadmap; #87 remains active and #88 decisions provisional. Root designs/reviews
and runs verification; user now authorizes GPT-6 Luna (high for coding, medium for
bounded investigation). Use GitHub PR workflow, never automatically merge.
Do not use reset cards. On actual quota exhaustion, stop and arrange one wake after
the limiting reset. Preserve typed/generated/NativeAOT mainline and existing
identity/tenant/approval authorities. No sample production shortcut or new agent loop.

## Current workspaces and PRs

- Current implementation worktree: /home/orches/workspace/CrestCreates/.worktrees/review-hash-inputs-87
  Branch codex/phase-10c-review-hash-inputs-87, based on PR103 head
  0d39cd2f3ad1846f2a7d64ad3bcdb93e6b8c9429. First production slice captures original
  canonical review hash inputs. It is not yet the complete durable review artifact.
- PR102 ready/unmerged: https://github.com/OrchesAdam/CrestCreates/pull/102
  Source worktree: .worktrees/live-proposal-retention-87.
  Final head5d635d8f56e3c9ae2f28ccf623c4e689dd640853.
  Full CI35804325739 passed at that exact head; readiness verified by prior heartbeat.
  Preserve published head.
- PR101 ready/unmerged: https://github.com/OrchesAdam/CrestCreates/pull/101
  Head6d83015137b79c88781eb592de650b04aab23a78; CI35673825720 success.
- PR100 ready/unmerged: head02459ad1b6768cee8f6ad967188e42e105cedf0f,
  CI35613490902 success. PR99/98/97 and earlier stacked PRs remain unmerged;
  exact heads/evidence are preserved in history. Do not rewrite them incidentally.

## Verified evidence

PR101: deterministic real parser/review -> formal PostgreSQL draft store -> provider
reconstruction -> exact draft and full canonical hashes -> fresh review. Tenant
isolation checked. Local focused1/1, full Asset34 passed/1 live skipped; full CI passed.
This temporary-schema test did not retain a live proposal for later approval.

PR102: explicit live retention preflight validates official provider/migrations
before model call. Save/readback use fresh PostgreSQL providers; fresh review
checks the reloaded draft. Original draft status is preserved. Locator exposes only
identity/full canonical hashes and prompt hashes, no private connection or raw output.
Local focused7/7; full Asset E2E40 passed/1 opt-in live skipped. One actual DeepSeek
retention call then passed1/1, no retry: HumanTaskDraftRetained. Requested model
 deepseek-v4-flash; observed deepseek-flash, HTTP200/stop,1661 prompt tokens,
2448 completion tokens (1989 reasoning). This is one sample, not reliability proof.

Actual retained proposal:
- Container crest-asset-approved-inventory-87, port127.0.0.1:55487.
- Persistent schema asset_live_retained_20260923. DO NOT drop it or replace container
  data; it holds the exact typed proposal for subsequent governance.
- Tenant asset-live-eval-tenant.
- Draft ht_asset_maintenance_initial_review-a9a00004ca5344f6832df9c25dc5e764.
- Locator/full hashes: docs/review/2026-09-23-asset-live-retained-result.json.
- Separate query after test process exit confirmed row remains, status0 (Created).
- No human approval, activation or deployment occurred.

Prior failed provider baselines remain intact (budget exhaustion, parser rejection),
as does the earlier successful summary-only sample. Never rewrite them as successes
of this retained run. No additional model request is needed for retention itself.

## Active design finding

DefaultDescriptorActivationRequestService owns requests in a private
ConcurrentDictionary, including approval/completion-event markers. Its changes are
blind dictionary assignments after status checks. Durable draft persistence does
not make requests, review/package/evidence artifacts or audit durable. Existing
phase9bplus reference-data spec explicitly reserves this as a separate domain cutover.
Only InMemoryRuntimeActivationGate exists; Activated is not production installation.

Root decision: asynchronous human review in an enterprise app needs continuity
across process restart. Design a single request authority with conditional transitions,
explicit evidence ownership, HumanTask/outbox recovery and gate idempotency boundaries.
Do not call an ephemeral UnderReview harness request a durable handoff. No production
activation owner has been implemented. #87 cannot be closed yet.
GPT6 Luna medium task luna6_next_governance_seam is gathering concrete state/transaction/
outbox/AOT seams; root writes/reviews design before delegating bounded implementation.

## Environment and next execution order

1. Verify PR102 final-head full CI; update PR body and mark ready only on success.
2. Finish durable-review design from code facts, existing crash/replay semantics and
   source-generated serialization gates. Preserve boundaries and staged exit criteria.
3. Implement reviewed smallest platform cutover through Luna high, with real race/
   restart/tenant/authority tests and NativeAOT publish/link/run where execution changes.

Use serialized dotnet --disable-build-servers -m:1 -p:UseSharedCompilation=false.
Local -p:NuGetAudit=false is an environment workaround, not a changed repo default.
Private DB config: /tmp/crest-asset-pg-private.json, never print. Runner:
/tmp/crest-run-asset-e2e.py LOG [dotnet-test extra args]. /tmp can disappear on reboot;
recover credentials internally from container configuration without printing them.
Credential env DEEPSEEK_API_KEY remains user-authorized; never print it.
Local latest logs: /tmp/crest-retention-focused-0923-r2.log,
/tmp/crest-retention-full-0923.log, /tmp/crest-live-retained-20260923.log.
No active automation currently. Last fresh quota5h40%,weekly6%,ordinary usage allowed;
query actual limits before making future scheduling decisions. No card used by root.

## Published design checkpoint

Draft PR103: https://github.com/OrchesAdam/CrestCreates/pull/103. Documentation only;
no durable activation implementation exists yet. Plan:
docs/superpowers/plans/2026-09-23-durable-activation-review-cutover.md.
Resolved: same PG coordinator can atomically create request/task; stable task ID
supports verification after unknown commit but CreateAsync itself is insert-only.
Do not wrap draft SaveAsync into this ambient transaction (top-level boundary).
Official package serializer preserves manifest/snapshot/evidence, NOT executable
descriptor definitions. Next design task is exact immutable review artifact contract
and async persistence ownership without losing current visibility/hash semantics.
Luna medium investigation completed; no active coding agents or duplicate work needed.
PR102 CI35804325739 was running Asset Management Golden Sample at last actual query;
query live status before ready. PR103 needs contract design completion, not a claim
of implementation. One-time heartbeat crestcreates scheduled2026-09-23 13:53 CST
following actual5h reset13:52:15; consume/delete after firing. No reset card used.

## Heartbeat continuation — 2026-09-23 13:53 CST

Fresh quota allowed work: 5h1%, weekly15%; no reset card used. PR102 final head
5d635d8f56e3c9ae2f28ccf623c4e689dd640853 passed full CI35804325739. Its body was
updated with this exact-head evidence and PR102 is now ready, unmerged. The one-time
crestcreates heartbeat was consumed and deleted; no active wake remains. PR103 remains
a design-only draft. Its next unresolved contract is the immutable review artifact
and async persistence authority; production implementation has not started.

## Active implementation — review hash inputs

Plan: docs/superpowers/plans/2026-09-23-review-hash-input-contract.md.
Root verified that canonical activation hashes derive from unprojected review;
the cached review used for Get/report is visibility projected. Rehashing the display
DTO after restore is not sufficient. GPT-6 Luna high task luna6_review_hash_inputs
implements a versioned typed captured input using the existing source-binding
projection and derives integrity from it. Existing v2 hash writers/values stay intact;
source-generated JSON and real native fixture coverage are required. This is a hash
input contract, not request persistence, approval authority or a complete review DTO.
Prior descriptor payload serializer caveat remains: Authoring JSON context alone
cannot round-trip the abstract typed draft payload; do not use it as another protocol.
Fresh quota at resume: 5h4%,weekly16%,ordinary allowed. No active automation or card use.

## Verification checkpoint — 2026-09-23 afternoon

Luna high implementation and root review complete. Focused hashes18/18, full
Draft132/132, fullControlPlane556/556, native fixture1/1 passed. Native Release
linux-x64 publish/link/run1m36s checked complete hashes before/after sourcegen JSON,
false review flags, diagnostics, unsupported input version and disabled reflection
fallback. Existing v2 pinned digests unchanged. Logs /tmp/crest-review-hash-focused.log,
/tmp/crest-review-hash-draft.log, /tmp/crest-review-hash-control-plane.log,
/tmp/crest-review-hash-native.log; native logs under artifacts/control-plane-json-aot-*.
Public hash-service interface expands: custom external implementations must update.
No model request, DB mutation, approval or activation. Full CI still pending.
One-time heartbeat crestcreates created for2026-09-23 18:55 Asia/Shanghai after
actual5h reset18:53:54. Latest quota92% used, weekly30%, ordinary allowed. No card used.
PR104 draft: https://github.com/OrchesAdam/CrestCreates/pull/104, stacked on PR103.
Implementation commit cfdc5200. Continue from this worktree and verify final-head full
CI before ready. Durable full artifact/request state remains future work; #87 open.

## Resume — 2026-09-23 18:55 CST

Actual quota restored (5h0%, weekly31%, allowed); no reset card used. Previous
handoff commit/push/CI dispatch was not executed because automatic approval review
hit the usage limit. Only memory.md remained uncommitted; PR104 implementation
cfdc5200 was already published. The consumed one-time heartbeat was deleted.
Resume by committing this handoff and dispatching full CI on the final PR104 head.

## Current follow-up — cached projection scope

PR104 final head1d8ac231af0152d6f179380bd2ef139708c58d50 is running full
CI35851610571. Watch /tmp/crest-pr104-ci-watch.log; verify before marking ready.
Current isolated WT /home/orches/workspace/CrestCreates/.worktrees/review-artifact-owner-87,
branch codex/phase-10c-review-artifact-owner-87, based on that final head.
Root/Luna review found cached review Get/List/report and direct package retrieval
can return an earlier broader projection after policy narrows while owner kind
stays visible. Fix plan docs/superpowers/plans/2026-09-23-review-projection-scope-binding.md.
Luna high luna6_review_hash_inputs implements exact stored scope binding and tests;
do not duplicate task. Root owns review/docs/testing. Submit must reject mismatched
review/package scopes before creating requests. Native coverage required.
Durable owner/report investigation recorded in docs/review/2026-09-23-review-projection-scope-binding.md;
no full durable artifact/store has been implemented. Retained live DB/model data
unchanged. Latest one-time heartbeat consumed/deleted; no active wake or card use.

## Latest handoff — 2026-09-23 evening

Current scope-binding changes remain uncommitted in review-artifact-owner-87.
Luna high production/unit work complete; root reviewed it. BuildTasks bootstrap
passed142 prior warnings/0 errors; production ControlPlane build passed0 warnings.
Focused ReviewProjectionScopeBindingTests5/5 passed after fixing test Json namespace
import (log /tmp/crest-review-scope-focused-r2.log). Full CP launched session48907,
log /tmp/crest-review-scope-full.log; inspect final result. No new PR yet.
Native helper added by luna6_scope_native_fixture exercises real DI services,
immutable schema catalog, Event draft, broad->narrow Get/List/report/package denial,
then broad re-review recovery. Initial JIT compile failed topology-builder namespace
(fixed). JIT r2 compiled but failed broad review Schema topology precondition:
/tmp/crest-review-scope-fixture-jit-r2.log. Agent diagnosing actual draft validation,
including lowercase DescriptorRef kind vs canonical Schema; inspect latest edits and
agent status. Do not weaken assertions or claim native success. Next: rerun JIT,
then actual native wrapper publish/link/run; only then publish reviewed stacked PR.
PR104 CI35851610571 still in PostgreSQL direct provider stage at last query,
head1d8ac231af0152d6f179380bd2ef139708c58d50. Watch session7317 /tmp/crest-pr104-ci-watch.log.
Do not mark ready unless exact-head full CI succeeds. No merge.
Quota97% used, weekly46%, reset2026-09-23 23:55:32 CST. One-time heartbeat crestcreates
created for23:57 CST, pointing at this worktree memory. No reset card used. Preserve
live container/schema/proposal unchanged. Other implementation work is complete;
do not duplicate dispatch. Root owns memory/docs; coding remains GPT-6 Luna high.

Final observed results: full CP failed2/passed559 (561 total); both failures in
Phase7dServiceIntegrationTests.PopulateReviewResult line37, whose reflection-created
internal ReviewResourceSnapshot still uses the old constructor. Delegate fixture
repair via formal review path or updated captured scope, then rerun full CP.
Native agent finished: Event DTO lacked Version=1 while ProposedVersion=1; now fixed
and failure output includes review/validation diagnostics. Lowercase DescriptorRef
"schema" is correct per existing fixtures, unchanged. JIT/native rerun NOT done.
All coding agents idle; no duplicate work in progress. Latest changes uncommitted.

## Resume — 2026-09-23 23:57 CST

Quota restored5h0%,weekly47%; no card used. Consumed heartbeat deleted. PR104 final
head1d8ac231af0152d6f179380bd2ef139708c58d50 passed full CI35851610571;
PR body updated and ready confirmed, unmerged. Parent head must remain unchanged.
Luna repaired Phase7d test seeding with the captured DevelopmentDefaults fingerprint;
full CP rerun log /tmp/crest-review-scope-full-r2.log. Native JIT r3 hit a diagnostic
helper type mismatch (AgentToolDiagnostic vs DescriptorDraftDiagnostic); Luna native
agent correcting it. Scope production guard logic remains reviewed and unchanged.

## Verified scope fix — 2026-09-24

All local validation now passed: focused5/5, fullCP561/561 (full-r2 log), real-service
JIT preflight (fixture-jit-r4 log), NativeAOT1/1 in1m29s (scope-native log), with
CONTROL_PLANE_PROJECTION_SCOPE_NATIVEAOT_OK and reflection fallback disabled.
Logs share /tmp/crest-review-scope- prefix. Native logs in artifacts/control-plane-json-aot-*.
Root reviewed stable implementation and fixture; no active coding agents. Next
publish stacked draft PR on PR104 and dispatch exact-final-head CI before ready.
Only cached projection scope boundaries changed; no durable store/activation claimed.

Published draft PR105: https://github.com/OrchesAdam/CrestCreates/pull/105,
implementation9d121ed9, stacked on ready/unmerged PR104. Final handoff commit follows;
dispatch full CI on that final head, verify before ready. Next independent design
investigation (Luna medium luna6_review_artifact_contract) maps exact finite report
input fields for lazy rendering; no next implementation has been dispatched yet.
