# Phase 10c resume checkpoint

## Current checkpoint (supersedes the initial checkpoint below)

- PR #91 now scopes only the bounded Workflow prerequisite. It remains draft;
  #87 Asset evolution and #88 final decisions are not complete.
- `66e56d33` scopes the #86 runtime diff gate to its true frozen history.
- `318e3f83` implements shared condition validation, the two outcome tokens,
  explicit skipped steps, and normal failure handling for invalid outcome state.
- After fixing the test's missing local event bus registration/namespace, Luna
  observed the actual red assertion: final approval tasks were nonempty after
  initial rejection. The earlier DI error was not that evidence.
- The focused policy/reproduction suite passed 11/11. The primary reviewer
  independently ran the full Workflow suite and confirmed 90/90:

```sh
dotnet test tests/Runtime/Workflow/CrestCreates.Workflow.Tests --no-build --no-restore --logger 'console;verbosity=minimal' -m:1
```

- Native publish/link succeeded according to the coding agent. Native execution
  is still pending: Testcontainers could not discover the local Docker socket,
  but a running PostgreSQL test container was independently found on port 42587.
  The fixture accepts `CREST_RUNTIME_PG_CONNECTION` to use this existing database.
  Recheck availability on resume; do not record the discovery failure as a
  permanent blocker or claim publish/link alone proves NativeAOT execution.
- Uncommitted AOT tests add real HumanTask completion, PostgreSQL outbox dispatch,
  and provider recreation before continuation; review their final form and test
  results before committing. Provider recreation is not process-kill recovery.
- Required before marking PR ready: public early-validation and failed-step
  accountability cases, both outcome tokens, metadata/draft compatibility checks,
  native publish-and-run, historical gate verification, and final-head CI.
- Default Asset v1 is unchanged; no live model evaluation has run.

## Initial checkpoint (historical)

PR #91 remains draft. Branch: `codex/phase-10c-asset-business-evolution-87`.
Persistent worktree: `/home/orches/workspace/CrestCreates/.worktrees/phase87`.
The old `/tmp` worktree was lost when the environment was recreated. Do not rely
on temporary directories to preserve uncommitted implementation work.

## Saved work

- `f4c4abe1`: independent Asset business oracle and overall #87 design.
- `afe219c9`: bounded Workflow outcome condition prerequisite design for #88.
- `d764f8e5`: public Workflow/HumanTask/Outbox reproduction test and references.

Production Runtime and default Asset v1 behavior are unchanged. The unsupported
Condition execution gap is supported by code inspection, but the reproduction
has NOT yet reached its business assertion at this checkpoint.

## Actual verification

SDK: 10.0.111. The reproduction compiled successfully. Running it failed during
hosted-service startup with `Unable to resolve service for type
'CrestCreates.EventBus.Abstractions.ILocalEventBus' while attempting to activate
'CrestCreates.HumanTask.DefaultHumanTaskRuntime'`. This is a test composition
failure, not a verified B04 failure. A Luna coding subtask is correcting the fixture.

Use an escalated command because sandboxed MSBuild cannot create named pipes:

```sh
dotnet test tests/Runtime/Workflow/CrestCreates.Workflow.Tests --filter FullyQualifiedName~WorkflowOutcomeConditionReproductionTests --disable-build-servers -m:1 -p:UseSharedCompilation=false --logger 'console;verbosity=normal'
```

Do not introduce production outcome routing until the correctly configured test
fails because final approval was created after rejection. Then implement/review
the prerequisite specification, its unknown-condition and seeded-outcome cases,
existing regressions, historical #86 diff-gate scoping, and native publish/link/run.
Continue with governed Asset v2 composition only afterward. No activation boolean,
hardcoded Allowed decision, or polling-to-abort workaround is acceptable.

User selected DeepSeek V4 Flash (`deepseek-v4-flash`) with credential environment
variable `DEEPSEEK_API_KEY`; presence was confirmed, value never printed. No live
model evaluation has run. No native verification has run for this change.

Primary agent owns design/review; all coding remains delegated to GPT-5.6 Luna
with high reasoning. Stop on the 5-hour usage limit; do not redeem reset credits,
schedule a continuation, or automatically resume. The user will resume manually.
