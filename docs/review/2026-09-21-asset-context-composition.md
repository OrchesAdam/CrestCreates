# Asset context composition review

The registered production extractors already express the required Form→Schema
relationship. FormRelationshipExtractor lives in Framework/Modules/CrestCreates.Form,
and AddFormKernel registers it. The preliminary claim that it was missing was
incorrect and was corrected before any production patch was made.

An offline Asset acceptance now passes using the actual baseline and registered
topology. Existing RuntimeScenario traversal yields exactly four versioned refs:
wf_asset_maintenance_review, ht_asset_maintenance_review, form_asset_maintenance_review,
and asset-management.schema.maintenance-decision. It retains three relationships,
provides stable hashes, excludes unrelated baseline schemas/capabilities, and
reports no truncation. No synthetic edge or extra focus descriptor is injected.

The first attempted recipe selected Triggers/HumanTaskStep for the Workflow hop
and returned only the focus. Inspection identified the active semantics: the
Metadata WorkflowRelationshipExtractor emits Uses with no Role. Using that
existing relation plus TargetKind=HumanTask, followed by Uses/Interaction/Form and
Uses/Schema/Schema, passed 1/1 on 2026-09-21. This was a recipe mismatch, not an
absent Form capability. The initial test compilation issue (an expression-tree
is-pattern) was separately corrected before the business assertion executed.

A separate consistency concern remains: Runtime/Workflow also has a
WorkflowRelationshipExtractor whose target taxonomy uses Triggers and role labels;
DefaultDescriptorRelationshipProvider returns the first matching extractor. This
is code evidence of order-sensitive selection, not a completed business regression
or a justification to silently change taxonomy here. A later convergence design
must establish the intended owner, registration cases, compatibility and hash
implications. This slice adds no alternate production path.

For Issue #88, bounded context selection is currently B (composition/DX), not A
(new traversal capability). Reuse the existing recipe in the opt-in authoring
probe before considering new context projection. Passing context selection does
not demonstrate successful model authoring, approval or deployment.

## Live bounded proposal result

One separately enabled DeepSeek request using this four-descriptor context and
prompt-template-v2 passed on 2026-09-21. Requested model deepseek-v4-flash,
observed model deepseek-flash. HTTP200 / stop,1661 prompt tokens,3304 completion
tokens including2955 reasoning tokens. Output cap16384 and90-second timeout
remained bounded. No provider text or reasoning was retained.

The real parser returned Succeeded and exactly one expected Create HumanTask v1.
Real draft validation and materialization passed with no blocking diagnostics or
blocked governance decision. The existing deterministic task-contract checks
passed. The typed proposal was not persisted as a replay artifact, approved,
activated or deployed. The retained JSON is a run summary, not the proposal itself.

This is one successful bounded sample, not a reliability estimate or the full
Issue87 workflow-evolution outcome. Protocol disclosure and context selection
changed together relative to the old baseline; the result does not isolate which
change caused improvement. No descriptor body projection, semantic retrieval,
new Agent Runtime or production ContextPack change was necessary for this case.

Validation: full Asset PostgreSQL suite33 passed with1 live test default-skipped;
the subsequent opt-in live test passed1/1. Evidence is
2026-09-21-asset-bounded-context-live-result.json. The duplicate Workflow extractor
consistency concern remains separate and unresolved.
