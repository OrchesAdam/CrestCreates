# Asset live proposal retention

This slice extends the existing opt-in authoring evaluation with an explicit
PostgreSQL retention boundary. The previous successful provider run saved only a
sanitized result summary. The new path saves the actual typed draft through the
existing IDescriptorDraftStore and its official provider codec, after the existing
semantic and deterministic review checks succeed.

## Configuration and ownership

The probe remains skipped unless CREST_ASSET_LIVE_EVAL=1. Retention additionally
requires CREST_ASSET_LIVE_RETAIN_PROPOSAL=1, an explicit
CREST_ASSET_LIVE_RETAIN_SCHEMA, and the established
ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING. The connection string is private and
must never be copied into the result artifact or this document. Preflight validates
the formal provider options and applies migrations before calling the model.
Invalid enablement or missing/unsafe configuration fails with a fixed code.

A retained schema is deliberately not dropped by the live probe. Its owner must
keep the underlying database/container data available for later review. The offline
fixture owns a separate temporary itest schema and cleans only that scope. No
background cleanup, deployment owner or new public API is introduced.

## Validation boundary

Save and readback use separately constructed PostgreSQL providers. Readback checks
all draft envelope fields and metadata, payload type and full descriptor canonical
contract/definition hashes. A fresh governance harness reviews the reloaded draft
against the current supplied baseline. A successful locator contains run/tenant/
draft/schema identity, complete descriptor hashes and available prompt evidence
hashes. It contains no connection details, raw provider response or exception text.
The locator is an aid to retrieval; it cannot authorize approval or activation.

Offline validation includes post-helper readback in a third provider and unchanged
draft status. Invalid configuration tests assert rejection by preflight. They do
not exercise a model transport or claim a no-HTTP integration regression; the
preflight-before-authoring order was reviewed directly in the live method.

## Execution evidence

On 2026-09-23 the focused tests passed7/7 and the full Asset E2E suite
passed40 with1 opt-in live test skipped. Logs: `/tmp/crest-retention-focused-0923-r2.log`
and `/tmp/crest-retention-full-0923.log`. The first fresh compile found a missing
SeverityLevel namespace import; it was corrected before these passing runs.
The prior temporary log was lost during an environment restart and was not counted
as a pass. One explicit live request passed1/1 and returned HumanTaskDraftRetained.
The sanitized [run artifact](2026-09-23-asset-live-retained-result.json) contains
locator and full hash evidence. HTTP200/stop;1661 prompt tokens and2448 completion
tokens (1989 reasoning). Requested model deepseek-v4-flash; observed deepseek-flash.
No retry was made. The exact typed draft remains in schema
`asset_live_retained_20260923` of local container `crest-asset-approved-inventory-87`.
The test process has exited; a separate database query confirmed its row remains.
Preserve that container/data and schema for the next governance step; do not use
temporary-test cleanup against it. Local credentials remain outside the repository.
Run log: `/tmp/crest-live-retained-20260923.log`.

This single successful sample is evidence of this run, not a model reliability
estimate. It adds retained HumanTask proposal evidence, not complete Workflow evolution.

This remains a test-owned evaluation and persistence handoff. Full Workflow
evolution, subsequent human governance and production activation remain outstanding
for #87. No automatic approval, activation or merge is part of this slice.
