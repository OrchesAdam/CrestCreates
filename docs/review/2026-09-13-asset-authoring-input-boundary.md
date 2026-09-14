# Asset authoring input boundary

The primary executed the real CreateDescriptorDraftAsync baseline with valid
HumanTask identity, version 1, CandidateGroup assignment and the existing Asset
form reference. Identity/version/reference assertions passed; the required
Approve/Reject outcome assertion failed (one test, one failure).

Evidence: 2026-09-13-asset-outcomes-create-baseline.log. The test used the real tool
service and generated projection, with a captured draft-store Save. It did not
execute approval or deployment. This local run used NuGetAudit=false after a
network audit failure; it is not evidence of a successful vulnerability audit.

HumanTaskContractSpec intentionally defaults preserved Outcomes during Create.
The constrained editing DTO therefore does not represent this complete new Asset
task. Do not repair payloads after creation or remove outcomes from the target.

This does not yet establish a missing platform capability. The existing
JsonDescriptorAuthoringOutputParser explicitly parses outcomes and is called by
LlmDescriptorAuthoringAgent. Verify that existing authoring boundary next, then
send its complete draft through the same real review/package/approval owners.
No production change or new authoring fallback is justified by this baseline.

The existing authoring parser test now passes with explicit Approve/Reject JSON:
AssetInitialHumanTaskJson_PreservesCompleteCandidateContract checks identity,
version, form reference, CandidateGroup assignment, Active state and both outcomes.
The primary independently reran it successfully (1/1). Active is the descriptor's
existing default; this does not prove arbitrary JSON state editing. The parser
test proves candidate representation, not live-model quality or approval.

The primary subsequently compiled and ran the actual authoring -> draft store ->
Review -> Preview -> Evidence -> Submit acceptance: 1/1 passed. The authoritative
request is UnderReview under an explicit require-human-review policy, and the
real HumanTask runtime creates the request-bound activation review task. Runtime
state uses the existing persistence contract registry; review, package, evidence,
request service and orchestrator are real owners. The fixture retains in-memory
audit/artifact/gate implementations and a controlled HumanTask store, and stops
before completion/Outbox delivery. No deployment or identity-authentication proof
is implied. Full ControlPlane 554/554 and Authoring 53/53 passed independently.

The test registers the existing Asset form/schema in its baseline. CC1001 is
suppressed only at the external form reference in the fixture's review-task
definition; runtime baseline consistency is asserted and the registry uses its
real DI validation engine. No global diagnostic switch is changed.

## Complete optional-reference equivalence (2026-09-14)

Comparing the entire parsed descriptor to the compiled Asset initial task exposed
a further defect: absent nullable InputSchema/OutputSchema became present default
references (null ID, version zero). The prior partial parser test did not assert
these fields. Full candidate comparisons failed twice; dedicated parser regressions
then failed 3/4, with the valid-reference control passing. Logs are retained in
2026-09-13-asset-optional-reference-baseline.log and
2026-09-14-optional-reference-parser-baseline.log.

The existing parser now uses a private typed nullable helper for just these two
optional fields. Missing/explicit null remain null; other values delegate to the
existing reference parser. Interaction and existing malformed-value behavior are
unchanged. No second authoring protocol or post-parse payload repair is added.
The primary reran parser regressions (4/4) and the complete Asset handoff/approval
tests (2/2). The latter now uses actual in-memory runtime persistence and hosted
Outbox delivery: real HumanTask completion leads to an Activated request and one
activation-gate call. This remains an in-memory activation gate, not deployment.
The final local suite now also verifies real HumanTask rejection through Outbox:
the authoritative request is Rejected with zero activation-gate calls. Full
ControlPlane passed 556/556 and Authoring passed 56/56. The existing NativeAOT
publish-link-run wrapper passed 1/1 with AgentAuthoringOptionalReferences:PASS for
missing/null/valid references; the primary inspected the ELF output. See
2026-09-14-authoring-optional-reference-native.log. Final-head CI remains pending.
