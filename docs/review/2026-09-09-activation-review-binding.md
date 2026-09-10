# Activation review binding review

The original baseline is preserved below. Test commit `74454c0a` adds four boundary regressions
without changing production code. The primary reviewer independently observed
one passing consistent approval and three incorrectly accepted conflicting
decisions. The subsequent logged run records request state and gate invocation:

| Completion/result | Actual dispatch | Actual request state | Gate calls |
| --- | --- | --- | --- |
| Canonical Reject, typed Approved | Accepted | Activated | 1 |
| Task A, result targeting request B | Accepted | A UnderReview; B Activated | 1 |
| Creator completes, result names another actor | Accepted | Activated | 1 |

The negative oracle requires Conflict, unchanged UnderReview requests and no gate
invocation. Honest approval passes. Command:

```sh
dotnet test tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests --no-build --no-restore --filter FullyQualifiedName~ActivationReviewCompletionBindingRegressionTests --logger 'console;verbosity=normal'
```

Baseline: 1 passed, 3 failed. Full recorded output is
`docs/review/2026-09-09-activation-binding-baseline.log`.

The fixture invokes real `DefaultHumanTaskRuntime.CompleteAsync`, captures its
generated event, and invokes the production callback/orchestrator/request service.
State/store/transactions/message factory, unchanged evidence and the recording
gate are controlled test substitutes. Thus Activated is a request-service state,
not proof of a runtime registry deployment. This does not test HTTP authentication,
real Outbox transport, PostgreSQL durability or package/evidence production.

The original consumer trusted conflicting typed-result fields without binding them
to the completed task input and canonical event. Repair belongs to this existing
owner. Follow `docs/superpowers/plans/2026-09-09-activation-review-fact-binding.md`:
bind task/request/tenant/actor/outcome, retain consistent approval/rejection and
duplicate behavior, verify cross-tenant/missing-task failures and run the changed
callback in NativeAOT. Do not start approved-host handoff before this gate closes.

PR #92 remains ready and unmerged with its final full CI green. #87/#88 remain
open. PR #93 remains draft until native execution and final-head CI complete.

## Repair verification (2026-09-10)

Commit `fa74e027` binds review dispatch to the stored Completed task, exact task
key/pin/completion event ID and persisted result, then checks request and tenant
against its typed input and decision/actor against the canonical completion event.
Invalid facts return the existing ReviewPayloadInvalid conflict before dispatch.
Empty tenant/correlation enrichment remains; nonempty mismatches cannot bypass
binding. No wire contract or separate approval authority was added.

The primary reviewer independently reran the command above: **12/12 passed**.
The three original conflicts now leave requests UnderReview with zero gate calls.
Coverage also includes honest approval/rejection, exact duplicate, missing and
noncompleted task, wrong event ID/pin, tenant mismatch, independently reconstructed
RuntimeStateValue and existing empty-field enrichment. The controlled fixture
limits described above still apply. The canonical ActorId is supplied by the
completion adapter; this repair does not authenticate that adapter's caller or
establish authoritative ActorKind.

Native callback execution and final-head CI are pending. Live model evaluation
and approved artifact-to-runtime handoff remain separate unfinished acceptance.
