# Asset live authoring evaluation

Issue #87 gets a bounded, opt-in DeepSeek authoring probe in the existing Asset
E2E project. The test runs only when `CREST_ASSET_LIVE_EVAL=1`; otherwise xUnit
marks it skipped. It uses the existing OpenAI-compatible client, the requested
`deepseek-v4-flash` model name, credential reference `DEEPSEEK_API_KEY`, the
default prompt/parser, and one 90-second request budget without retries.

The test builds the deployed Asset baseline and a `DirectDependencies` context
focused on `wf_asset_maintenance_review` v1. It asks for the first bounded
authoring increment: exactly one `Create` HumanTask draft for
`ht_asset_maintenance_initial_review`, matching the existing compiled task
contract: maintenance form interaction, active state, CandidateGroup, v1, null
optional task schemas, and Approve/Reject outcomes. It does not ask for a
workflow update. Each returned draft is passed through the real
`IDescriptorDraftReviewService`; validation errors, blocker/error diagnostics,
or a blocked governance decision fail the review and do not advance the running
inventory. The test does not submit activation, complete a human task, or
mutate runtime registries.

The JSON evidence path is `CREST_ASSET_LIVE_EVAL_ARTIFACT`, with a temporary
fallback. It contains only run/model identifiers, bounded context refs/counts/
hashes, status and diagnostic codes, draft operation facts, review statuses, and
semantic booleans. Provider free text, model output, prompts, headers, and
credentials are excluded. A test-owned response observer additionally records
HTTP status, choices count, an allow-listed finish reason, content/reasoning
character counts, and numeric usage counters while leaving the response body
available to the existing client. A local fake-handler test proves body
preservation and the absence of response text in the metadata projection.

The first opt-in run on 2026-09-20 took about 18 seconds and returned
`ProviderUnavailable` with diagnostic code `AUTHORING_PROVIDER_UNAVAILABLE`.
The provider response exposed observed model `deepseek-flash` while the
requested model remained `deepseek-v4-flash`; this is recorded as an aliasing
observation and does not assert a fixed model family. The default context pack
contained two descriptors for the focused workflow's direct-dependency scope.
A separate instrumented request on 2026-09-21 returned HTTP 200 with finish reason `length`, zero content
characters, 16,990 reasoning characters, 568 prompt tokens, 4,096 completion
tokens, and 4,096 reasoning tokens. The live evidence classifies this bounded
fact as `OutputBudgetExhausted` while retaining the production authoring status
and diagnostic unchanged. `CREST_ASSET_LIVE_EVAL_MAX_OUTPUT_TOKENS` accepts an
Invariant-culture integer from 1 through 16,384, defaults to 4,096, and is
recorded in the artifact. The subsequent single 16,384-token experiment returned
HTTP 200 / stop with content, but the real parser rejected it before creating
drafts. See docs/review/2026-09-20-asset-live-authoring-baseline.md for the
separate evidence records. The baseline slice requires no further live calls.
