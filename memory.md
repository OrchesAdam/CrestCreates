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

- Current design worktree: /home/orches/workspace/CrestCreates/.worktrees/durable-review-design-87
  Branch codex/phase-10c-durable-review-design-87, based on PR102 final head.
  No implementation changes here yet. Design work addresses durable human-review
  continuity, not another in-memory pending-request demonstration.
- PR102 draft: https://github.com/OrchesAdam/CrestCreates/pull/102
  Source worktree: .worktrees/live-proposal-retention-87.
  Final head5d635d8f56e3c9ae2f28ccf623c4e689dd640853.
  Full CI35804325739 was in progress; verify exact-head success before ready.
  Watch log /tmp/crest-pr102-ci-watch.log (session67463). Preserve published head.
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
