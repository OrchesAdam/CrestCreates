# Next slice: intentionally retained live proposal

Status: design outline only; implementation and execution have not started.
Prerequisite: verify PR #101 full CI at its final head. Preserve that tested head
and implement the successor in a separate worktree/branch.

## Problem and chosen boundary

The live authoring probe currently saves a sanitized result summary. That is useful
model evidence but cannot provide the exact proposal for later human governance.
PR #101 establishes the existing PostgreSQL IDescriptorDraftStore round trip for a
synthetic HumanTask proposal. Reuse that store and its formal codec; do not create
another JSON proposal import/export contract or expose internal serializers.

## Proposed bounded implementation

Add explicit opt-in retention configuration to the test-owned live evaluation.
A retained run must use the existing configured local PostgreSQL service and a
clearly named persistent schema. Retention is disabled by default; a requested but
invalid retention configuration fails before sending a model request. Do not use
the temporary schema fixture, because it cleans up the proposal at test completion.
Validate schema naming through the existing provider options/migration boundary.

Only retain the exact typed draft after the existing authoring, review and expected
business-contract checks all succeed. Do not patch its payload or envelope to make
it acceptable. The retention helper must not approve or activate anything. Keep
its service provider separate from the in-memory governance harness to avoid
unintended changes to other store registrations or hosted services.

After SaveAsync, dispose the provider, read from a fresh provider and compare the
complete draft and complete descriptor contract/definition hashes. Replay review
in a fresh harness against the current baseline. Emit a sanitized locator/evidence
summary only after this succeeds: run identity, tenant, draft identity, schema,
full hash metadata and values, and available prompt evidence hashes. Do not emit
connection strings, credentials, provider response bodies or free-form diagnostics.
A locator is not approval authority; future governance must load current content
and perform its own checks. If persistence or replay fails, keep the failure
explicit rather than reporting a successful retained proposal.

Provide a bounded offline test for the reusable retention path before any new live
request. It must demonstrate readback after helper/provider disposal and explicit
cleanup ownership, invalid configuration before model invocation, and absence of
approval/activation side effects. Avoid tests that simply mirror assignments.
Use existing tenant-scoped store contracts; do not invent a new storage authority.

## Execution and claims

After implementation review and appropriate local regression, execute at most one
separately enabled DeepSeek request using the existing selected model and credential
environment variable. Preserve failure evidence without automatic retries. A
successful retained HumanTask proposal still does not prove full Workflow evolution,
durable human approval or production deployment. Keep #87 open until those stated
boundaries are actually demonstrated. Use GitHub PR flow; no automatic merge.

Open implementation detail: choose a reusable test-owned retention entry point
without turning this opt-in fixture into a new application API. The actual schema
name, locator format and cleanup instructions must be documented with the run.

## Review decisions at implementation handoff

- Preflight includes provider registration/options validation and database migration
  or compatibility checks before invoking the model, not only parsing env values.
- Keep the exact draft status returned by the live path. Direct review-service
  execution is not equivalent to the tool service marking a persisted draft Reviewed.
- Live failure output uses fixed categories/stages, never assertion diffs containing
  provider rationale or full draft content. Offline synthetic tests may use normal
  assertion diagnostics.
- Root will run offline tests and review the retained-result success predicate
  before deciding to spend one live request. No live request has been made in this
  successor worktree.
