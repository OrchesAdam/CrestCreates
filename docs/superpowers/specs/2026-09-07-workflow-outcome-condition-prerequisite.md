# Workflow outcome condition prerequisite

Issue #87 business case B04; framework decision owner #88; draft PR #91.
Design owner: primary reviewer. Implementation owner: GPT-5.6 Luna (high).
Status: implemented in draft PR #91; reproduction, Workflow regression and local
native publish/link/run verified. Final-head CI acceptance remains pending.

## Problem and decision

A two-stage maintenance approval must not create the final approval task after
initial rejection. Inspection shows that `WorkflowStep.Condition` is represented
in draft snapshots and canonical hashes, but `WorkflowExecutionRunner` executes
every step without consuming it. Compatibility and binding checks also omit it.
This is a candidate framework-owned correctness/capability gap, not a reason to
poll and abort a task from an Asset consumer after it has already been created.

First run an independent public-mainline test expecting no final task following
rejection, with a non-null condition on that step. Record the actual failure.
Once reproduced, implement the following bounded responsibility in Workflow.

## Contract

Retain the existing nullable `WorkflowStep.Condition` wire field. Introduce a
named public declaration surface for exactly two canonical tokens:

- `previous-human-task-approved`
- `previous-human-task-rejected`

Application declarations use named constants or a typed factory owned by Workflow;
handlers never compose these strings. The runtime uses one internal typed parsing
and evaluation policy. This reuses an existing hash-covered contract slot rather
than adding a field that would change every pre-existing descriptor pin.

Null means unconditional, preserving the existing mainline. Every other token,
including empty/whitespace, arbitrary expressions and case variants, is invalid.
No expression evaluator, reflection, script engine, new provider or plugin.

Compatibility is deliberately narrower than preserving all previous behavior:
unconditional descriptors retain their behavior and hash shape, but previously
ignored non-null conditions now execute only if supported, or fail validation.
An unchanged hash is identity evidence, not a promise to retain the bug that
ignored that field. No migration of already-running conditional workflows is
claimed by this prerequisite.

Both supported conditions refer only to the immediately preceding descriptor step.
That step must target a HumanTask. First-step conditions and conditions after a
Capability or SubWorkflow are invalid. Do not reinterpret stale outcomes from an
earlier HumanTask separated by other steps.

## Execution and failure

Validate all declared conditions before beginning execution or invoking a target.
Share the policy with compatibility validation and binding-status diagnostics;
startup checks alone are insufficient because callers can construct registries.
Unknown/invalid conditions must produce an actionable failure before any target
side effects, including when the invalid declaration is a later step.

Evaluate a valid condition only from the persisted runtime continuation evidence:
the immediately preceding HumanTask step has a completed result and the canonical
`lastStepOutcome` has a supported, correctly typed value. Caller-supplied workflow
input alone is not proof of a completed HumanTask. Missing/malformed evidence fails
closed; it is not a false condition or implicit approval.

Matching outcome executes the step normally. A valid nonmatching outcome records
an explicit skipped step, advances the index without resolving/executing its
target, and persists through the existing Workflow state/accountability path.
Append a Skipped result enum value without changing any existing numeric values.
Do not report the skipped target as completed or publish a target side effect.
False conditions are normal control flow, not aborted or failed workflows.

Resume, duplicate completion, exact descriptor pins and tenant correlation retain
their current owners. Add no independent completion or retry protocol. Unconditional
v1 descriptors and their existing canonical hashes must remain unchanged.

## Required executable cases

1. Initial Reject skips final approval; no final HumanTask exists.
2. Initial Approve creates exactly one final HumanTask.
3. The rejected condition executes only following Reject.
4. Unknown/blank/case-variant conditions fail before any target executes, even when
   declared after an unconditional side-effecting step.
5. First-step and non-HumanTask-predecessor conditions fail validation and binding.
6. Seeded `lastStepOutcome` cannot substitute for a real completed predecessor.
7. Missing or invalid outcome evidence fails closed.
8. Unconditional behavior and descriptor hashes remain unchanged; different
   supported condition tokens change the existing contract/definition hashes.
9. Draft snapshot and source-generated JSON retain the condition tokens.
10. Persisted continuation/restart and duplicate-delivery behavior remain correct.
11. A real native publish/link/run exercises approve and reject routing.

Use focused tests on the production mainline and existing fixtures. No text-presence
test may stand in for these behaviors. Record red/green commands and actual output.

## Scope and roadmap

This prerequisite does not complete #87. Asset versioned composition, independent
business acceptance, governed approval/package handoff and DeepSeek model-quality
evaluation remain separate evidence. Do not change the default Asset v1 selection.

The historical Phase10b no-runtime-change test currently compares whichever PR is
running against its base. A real runtime fix must not be blocked forever by a closed
review phase. If exercised here, scope that check to the actual frozen #86 baseline
and final revisions, retaining real diff verification and an explicit history-fetch
requirement. Do not weaken it into an unconditional pass or a Markdown assertion.
