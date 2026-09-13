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
