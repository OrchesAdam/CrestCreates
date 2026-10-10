# Archived active handoff — 2026-09-30（Phase 10c / PR107–PR108 收尾）

> 归档说明（2026-10-10 · Issue #122）：本文件保存 2026-09-30 memory.md 活跃 handoff 的完整内容（原样，未修改），已被重写的 `memory.md` 取代。归档目的：保留历史现场（PR107/#108 未合并时期的状态），其中旧任务指令、worktree/配额/唤醒信息不再有效，不作为当前操作依据。更早的历史见 `2026-09-20/23/24/29-memory-history.md` 与 `2026-09-30-package-evidence-handoff-history.md`。

---

# CrestCreates active handoff — 2026-09-30

## Current objective and rules

Issue87: AI safely creates and evolves enterprise applications;88 provisional.
Root designs/reviews/verifies. GPT-6 Luna high codes; medium investigates.
GitHub PR workflow, never auto-merge. Stop at actual5h limit; one-time wake after
actual reset; no reset cards. No new model requests. Never output credentials.
AGENTS.md: single typed/generated authority; actual native publish/link/run evidence;
archive replaced files, never delete. Preserve root user untracked
`docs/review/h2-mainline-closure-review.md`.

## Active worktree and state

Path `/home/orches/workspace/CrestCreates/.worktrees/package-evidence-store-87`.
Branch `codex/phase-10c-package-evidence-store-87`.
Base PR107 final `d2e68d65fe9c42ac19e41a3783afd5f4e79aacec`.
PR107 READY; fullCI36532381060 success at that head, unmerged. Do not alter parent.
PR108 https://github.com/OrchesAdam/CrestCreates/pull/108 DRAFT stacked on107;
implementation45ea74f3 committed/pushed; attached. Exact five replaced source files
archived under99_RecycleBin/2026-09-29-package-evidence-store and committed.
Plan `docs/superpowers/plans/2026-09-29-package-evidence-artifact-cutover.md`.
Evidence `docs/review/2026-09-30-package-evidence-artifacts.md`.
Complete prior handoff preserved in `docs/review/2026-09-30-package-evidence-handoff-history.md`;
older histories Sep29/Sep24/Sep23 remain.

## Implementation and review constraints

One async IAgentPackageArtifactStore replaces ToolService package/evidence dictionaries,
latest pointer and independent resolver scalar hash writers. Explicit memory and PG
providers. Exact evidence-parent ID, tenant/owner/scope/version checks before submission.
Atomic new-pair insertion, existing-parent evidence insertion, detached snapshots,
deterministic latest query on scope/version/catalog. No failure fallback.
PG additiveV015, FK, strict schema manifest and structured-column/payload comparison.
Official package serializer and original3hash profiles unchanged. Separate canonical
content profile covers exact PackageJson, entire projection and immutable metadata;
old3hashes omit snapshot relationships/diagnostics. This is integrity, not approval or
privileged-writer authenticity. Resolver derives hashes from validated same-store content.

Latest addition: shared factory CreateProjectedPair with actual authorization options,
exact catalog, owner, package, IDs/time. Requires topology builder, reuses platform
projectors, rejects hidden owner/package kinds, creates digest-owning envelopes.
CompanyCertification sample uses it and atomic store insertion. New2 narrow-scope
factory tests; constructor callers in CP and PG tests updated. This addition and its consumers are now verified locally. Root requested semantic review of proposed
references absent from pre-review catalog; preserve catalog fingerprint semantics.
Do not fix test fixtures by weakening production scope/integrity.

## Verified results and next commands

Local final checks all passed:
- PG463/463 in5m36 `/tmp/crest-package-pg-final-r2.log`, includes large catalog index case.
- CP575/575 `/tmp/crest-package-cp-final.log`, includes7new tests.
- CompanyCertification34/34 `/tmp/crest-package-sample-r2.log`.
- Boundary168/168 `/tmp/crest-package-boundary-r2.log`, excludes CI ledger aggregation.
- Actual NativeAOT1/1 in1m29 `/tmp/crest-package-native-final.log`; output
  artifacts/control-plane-json-aot-8bb8b0d9d3f54b239b6edafac6fb9606/run.log.
  Memory service/native evidence only, NOT PostgreSQL-native support.
- All5 replaced source files verified archived identically. No old scalar writer/cache
  type references in live src/tests/samples. git diff --check clean.

Root caught unbounded scope/catalog strings in PG reuse btree; fixed to bounded
lookup keys tenant/draft/time/id, exact scope/version/catalog remain SQL predicates.
Earlier compile/test failures preserved in logs/history; do not repeat resolved work.
No local test process running. Next: handoff/final commit, dispatch fullCI and
verify exact head; ready only
on complete success, never merge. FullCI includes JSON ownership/aggregate ledger.

## Agents

/root/package_evidence_store_runtime completed runtime/contracts/tests/sample.
/root/package_evidence_pg and /root/package_evidence_native completed and idle.
Do not duplicate assignments. Root serializes local builds/tests.

## Test environment

Use --disable-build-servers -m:1 -p:UseSharedCompilation=false -p:NuGetAudit=false.
Last flag local environment workaround; never change package defaults.
Independent container crest-review-store-tests-87,127.0.0.1:55488,crest_runtime_tests.
Private `/tmp/crest-review-store-pg-private.json` contains connection; inject internally
as CREST_RUNTIME_PG_CONNECTION, never print. Tests create isolated itest schemas.
No Docker socket; existing Docker wrapper works. Do not use retained live container.

## Preserve real proposal

Container crest-asset-approved-inventory-87 (stopped after reboot, preserved),port55487;
schema asset_live_retained_20260923; tenant asset-live-eval-tenant;
draft ht_asset_maintenance_initial_review-a9a00004ca5344f6832df9c25dc5e764,Created.
Locator docs/review/2026-09-23-asset-live-retained-result.json. One successful DeepSeek
retention call already independently verified; no more calls, replacement, cleanup
or schema changes. No approval/activation. Older failures remain failures.

## Remaining platform scope

Only review/package artifacts are addressed. Durable activation aggregate/CAS/replay,
submission operation identity, request+HumanTask transaction, activation intent/receipt
and actual runtime installation are separate work. Gate is in memory, no production
deployment or complete durable approval claim. Issue87 cannot close.
PR106 and earlier ready PRs remain unmerged; PR103 design-only draft.

## Quota and automation

Sep30 10:43 wake restored5h0%,weekly79%, actual reset1790754200.
Consumed crestcreates wake deleted; no active wake currently. No reset cards used.
Schedule future wake only from actual quota/reset and latest handoff.
