# Activation review completion binding — focused acceptance

Design/review owner: primary agent. Coding owner: GPT-5.6 Luna, high.
This precedes the Asset approved-host handoff in Issue #87. It is a bounded
investigation of the existing activation review consumer, not a new authority.

## Observed code and unverified consequence

`DescriptorActivationReviewHumanTaskEventHandler.HandleAsync` parses the typed
review result and routes it through the existing orchestrator. Its enrichment
method returns immediately when the result supplies TenantId and CorrelationId.
The current handler does not compare the result's request identity to the stored
review task input, or the decision/actor to the completion event's Outcome/ActorId.
The activation request service's self-approval check uses the parsed ActorId.

These are source observations. An unauthorized activation has not yet been
demonstrated. Record executable behavior before claiming that consequence or
changing the implementation.

## Independent oracle

- A real completed review task can decide only the activation request bound in
  its persisted input, under that task's tenant.
- A canonical rejection cannot become approval because typed result data says
  Approved. Conflicting facts must fail closed, without invoking the activation
  gate or mutating another request.
- Changing a result's ActorId must not evade the existing self-approval policy.
  Use the authoritative completed event's actor evidence. Do not invent a second
  authentication source or infer a human identity from an arbitrary result flag.
- Supplying nonempty tenant/correlation fields must not skip durable-task checks.
- An honest authorized approval/rejection must still use the existing request
  service, evidence rechecker and gate. A changed evidence set must remain blocked.
- Repeated delivery of the same valid completion retains the current duplicate
  semantics. A conflicting completion must not be treated as a successful replay.

## Implementation discipline

Start with focused failing cases using a real HumanTask completion and production
consumer/request-service path. Reuse existing composition fixtures. Derive events
from the actual runtime Outbox path where possible; clearly label any direct
consumer-boundary invocation. Do not fabricate an Activated flag, manually invoke
the gate as the approval step or use placeholder hashes as production evidence.

For this first consumer-boundary experiment, an existing fixture may establish
valid, unchanged evidence as a controlled premise and use a recording gate.
That does not attest to package production, evidence generation or deployment.
Keep real HumanTask completion and the production consumer/orchestrator/request
service; do not build a general authoring host merely to reproduce this boundary.

Before a production edit, report exact baseline commands, observed request/gate
state and which expectation failed. The review owner will confirm the narrow fix.
Any repair belongs to the current consumer/orchestrator/request owners. Preserve
canonical contracts, tenant boundaries, evidence ownership and NativeAOT gates;
do not add a generic completion protocol or testing-only production hook.
