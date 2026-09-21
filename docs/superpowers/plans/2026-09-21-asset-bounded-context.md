# Asset bounded authoring context

The live baseline's DirectDependencies request yields only Workflow and HumanTask.
The initial read-only report incorrectly claimed a missing Form extractor. Primary
review found the existing typed FormRelationshipExtractor and AddFormKernel
registration under Framework/Modules. Do not add a duplicate extractor or synthetic
relationship, and do not classify this as a missing platform capability.

First execute an offline Asset acceptance using the real registered extractors,
real baseline inventory and existing RuntimeScenario recipe: the registered
Metadata Workflow extractor uses Uses / no Role, then HumanTask Uses / Interaction
-> Form Uses / Schema -> Schema. Restrict each step by its actual target kind.
The distinct Runtime Workflow extractor uses Triggers / HumanTaskStep; do not
assume that taxonomy is active in the current harness.
Each step has max depth1; request depth3 and count8 are explicit. The expected
context is exactly four versioned refs, three real edges, available stable hashes,
closed edge endpoints, no unrelated schemas/capabilities, no truncation.

If it passes, reuse this test-owned bounded recipe for the existing opt-in live
probe. Preserve the original two-descriptor evidence as a separate baseline.
Do not alter the production ContextPack builder, invent dependency edges, copy
candidate target definitions, or expand to all metadata. Reuse promptv2 protocol
disclosure. A later live evaluation can determine whether refs/names plus the wire
reference suffice; descriptor body projection requires separate evidence/design.

The live probe remains proposal -> real parser -> deterministic review only.
It may not submit activation or simulate human approval. Keep existing timeout,
output budget bounds, metadata-only observation and diagnostic evidence; no raw
provider reasoning, prompts, headers or credentials are retained. No new live call
is required before the offline acceptance is reviewed and passing.

Executed: real recipe acceptance1/1, full Asset suite33 passed/1 live skipped,
then one opt-in live proposal test1/1. See the review evidence for exact scope.
No production changes were needed; no further model request is required in this
slice. The accepted typed proposal was not retained for approval/replay.
