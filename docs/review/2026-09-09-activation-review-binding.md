# Activation review binding baseline

Implementation is pending. Test commit `74454c0a` adds four boundary regressions
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

The current consumer trusts conflicting typed-result fields without binding them
to the completed task input and canonical event. Repair belongs to this existing
owner. Follow `docs/superpowers/plans/2026-09-09-activation-review-fact-binding.md`:
bind task/request/tenant/actor/outcome, retain consistent approval/rejection and
duplicate behavior, verify cross-tenant/missing-task failures and run the changed
callback in NativeAOT. Do not start approved-host handoff before this gate closes.

PR #92 remains ready and unmerged with its final full CI green. #87/#88 remain
open. This successor is intentionally a draft with failing regression evidence;
no production fix or readiness claim is made yet.
