# Asset live authoring baseline — 2026-09-20

One opt-in live request used the existing LlmDescriptorAuthoringAgent,
OpenAI-compatible client, default prompt/parser, and DEEPSEEK_API_KEY credential
reference. No credential, raw prompt, response text or headers are retained.
The public repository synthetic Asset baseline supplied all domain context;
the in-memory memory store was newly constructed and empty.

Requested model: deepseek-v4-flash. Observed model: deepseek-flash.
The official provider documentation now says the legacy request name is served
by DeepSeek-V4.1-Flash: https://api-docs.deepseek.com/quick_start/pricing/.
This run is not evidence of fixed original V4 model weights.

The probe failed after approximately 18 seconds: ProviderUnavailable,
AUTHORING_PROVIDER_UNAVAILABLE, zero drafts. The client obtained a provider
model name but no usable response content. No deterministic draft review,
human approval, activation or runtime deployment occurred. The available
metadata does not establish whether token exhaustion, provider behavior or
another response-shape issue caused the empty content. Do not attribute this
failure to model quality or context without further evidence.

The actual DirectDependencies context contained two descriptors: the maintenance
Workflow v1 and its directly referenced HumanTask v1. It did not include the Form.
The default prompt projects names, refs and hashes without descriptor bodies.
These are separate observed context limitations; they do not explain the empty
response by themselves. A later probe must retain safe transport metadata to
separate provider completion limits from proposal and review failures.

Evidence: 2026-09-20-asset-live-authoring-baseline.json. This is an actual failed
live evaluation, distinct from the passing deterministic fixtures in PR #97.

## 2026-09-21 diagnostic experiment

After a separate offline-corrected observer composition failure (no HTTP sent),
one instrumented request returned HTTP 200, one choice, finish_reason=length,
content length 0, reasoning length 16990 characters. Usage was 568 prompt tokens
and 4096 completion tokens, all 4096 reported as reasoning tokens. This run
demonstrates output budget exhaustion before final content, not network failure.
The existing authoring layer maps empty content to ProviderUnavailable; that
production classification is unchanged by this test-only probe.

Evidence: 2026-09-21-asset-live-output-budget.json. The provider reasoning itself
was neither logged nor stored. A single bounded larger-budget experiment is
planned to reach parser/review signals; this is not automatic retry.

## Larger-budget experiment

One separately enabled request raised only the output cap to 16384 (same default
prompt and two-descriptor context, 90-second timeout). HTTP 200 returned
finish_reason=stop, 3650 content characters and 3152 reasoning characters.
Usage: 568 prompt tokens, 1687 completion tokens, including 797 reasoning tokens.
The real parser returned InvalidProviderOutput / AUTHORING_INVALID_PROVIDER_OUTPUT
with zero drafts. No review or activation followed.

Evidence: 2026-09-21-asset-live-parser-rejection.json. The retained diagnostic code
does not distinguish the parser's several envelope rejection branches; no exact
missing or malformed field is claimed. Inspection independently shows the
default prompt names contract 7g.v1 without providing its complete wire schema.
It also omits descriptor bodies and the focus's transitive Form dependency.
These are concrete prompt/context gaps to design next, not proof that an expanded
agent runtime or semantic retrieval is needed. No more live calls are planned
for this baseline slice.

## Local validation

Full Asset PostgreSQL suite: 31 passed, 1 live probe explicitly skipped.
Includes offline observer body-preservation, non-object response, typed-client
composition, output-budget boundaries and completion classification checks.
The live probe's expected business outcome remains unmet; default CI never
contacts DeepSeek. This change is test/evidence-only and claims no new production
NativeAOT capability. PR #97 remains ready at its verified head.
