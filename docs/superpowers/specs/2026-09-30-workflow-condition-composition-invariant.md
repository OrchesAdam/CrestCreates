# Workflow condition composition invariant

> Date: 2026-09-30
> Status: Accepted design constraint
> Supersedes: no prior spec; complements `2026-09-07-workflow-outcome-condition-prerequisite.md`
> Related: PR #91, Issues #87 / #88

## 1. Purpose

PR #91 introduced bounded HumanTask outcome conditions (`previous-human-task-approved`, `previous-human-task-rejected`) into the Workflow runtime. The implementation revealed two additional design constraints that were not part of the original prerequisite spec but are necessary for deterministic composition. This document records them as intentional, permanent invariants — not temporary limitations.

## 2. Invariants

### 2.1 Conditional step requires an unconditional predecessor

A step that declares a `Condition` must be immediately preceded by an unconditional step. If the preceding step itself carries a condition, the descriptor is rejected at validation time.

**Rule**: `WorkflowConditionPolicy.GetValidationError` returns an error when `descriptor.Steps[index - 1].Condition is not null`.

**Rationale**: A conditional predecessor may be skipped at runtime. A skipped step produces a `StepResults` entry with status `Skipped`, not `Completed`. If a downstream condition then attempts to evaluate the persisted `lastStepOutcome` from that step, the evidence is absent or stale. The resulting failure mode is non-deterministic — it depends on runtime evaluation order rather than descriptor structure.

**Strategy**: Fail-fast at binding time. The descriptor validator rejects the declaration before any workflow instance starts. This is preferable to a runtime exception because:

1. The error is deterministic and reproducible at design/compile/deploy time.
2. It prevents partially-executed workflows from reaching an ambiguous state.
3. It keeps the runtime evaluator's preconditions simple and provable.

### 2.2 Skipped is a Runner-internal status, not an executor return value

`StepExecutionStatus.Skipped` is produced exclusively by `WorkflowExecutionRunner` when a condition evaluates to false. No `IWorkflowStepExecutor` implementation may return this status.

**Rule**: The Runner's `switch` on `stepResult.Status` includes a `default` case that throws `InvalidOperationException` when an executor returns `Skipped`.

**Rationale**: The Runner owns the condition evaluation loop. Allowing executors to produce `Skipped` would create two sources of truth for step skipping — one inside the Runner (condition-based) and one inside executors (ad-hoc). This would break the invariant that `Skipped` always means "condition was false, target was never invoked," and would make accountability and persistence semantics ambiguous.

**Contract**: `IWorkflowStepExecutor.ExecuteAsync` must return `Completed`, `Suspended`, or `Failed`. Any other value is a contract violation.

### 2.3 Conditions reference only the immediately preceding HumanTask

A condition token evaluates against the persisted result of the single step at `index - 1`. It does not backtrack across multiple steps, does not search for the most recent HumanTask, and does not accept seeded or caller-supplied variables as substitute evidence.

**Rule**: `WorkflowConditionPolicy.Evaluate` reads `descriptor.Steps[index - 1]`, requires that step's target to be `HumanTaskTarget`, and requires a `Completed` result for that exact step. The `lastStepOutcome` variable must come from persisted runtime state, not from workflow input.

**Rationale**: Backtracking or searching for "the most recent HumanTask" would couple condition semantics to descriptor layout in non-obvious ways. A descriptor author who inserts an intermediate step between a HumanTask and its conditional successor would silently change the condition's meaning. Explicit adjacency keeps the contract provable and the descriptor self-documenting.

## 3. Expressiveness constraint

The current condition system does not support dual-branch (fork/join) workflows. Consider:

```text
H1 (unconditional)  → HumanTask: approve or reject
H2 if approved      → may execute or be Skipped
H3 if rejected      → rejected at validation (H2 is a conditional predecessor)
```

Step H3 is rejected because H2 carries a condition. If H2 is skipped, H3's condition would attempt to evaluate against a `Skipped` result — the exact non-determinism that invariant 2.1 prevents.

This is a deliberate design decision, not an implementation gap. Workflows that require divergent branches should use one of:

- Separate workflow definitions, each with a single linear path.
- An explicit fork/join primitive, if and when one is introduced into the Workflow runtime.

Do not relax invariant 2.1 to accommodate dual-branch patterns. If dual-branch support is needed, it must be introduced through a new first-class construct (e.g., a `ForkStep`, `BranchTarget`, or parallel region) with its own semantics, validation rules, and persistence model — not by weakening the adjacency and determinism guarantees that the current linear model provides.

## 4. Validation surface

These invariants are enforced by the following production code paths:

| Invariant | Enforcement point | Failure mode |
| --- | --- | --- |
| 2.1 | `WorkflowConditionPolicy.GetValidationError` | `WorkflowValidationException` before execution begins |
| 2.2 | `WorkflowExecutionRunner` default switch case | `InvalidOperationException` at runtime |
| 2.3 | `WorkflowConditionPolicy.Evaluate` | `WorkflowValidationException` when predecessor is missing, non-HumanTask, or not `Completed` |

Validation runs in three contexts, all sharing the same policy:

1. `WorkflowEngine` — before starting or resuming a workflow instance.
2. `WorkflowCompatibilityValidator` — during descriptor compatibility checks.
3. `WorkflowBindingStatusContributor` — during binding-status diagnostics.

## 5. Non-goals

- No expression evaluator, script engine, or dynamic condition composition.
- No cross-step backtracking or "most recent HumanTask" search.
- No fork/join, parallel branch, or merge primitive in the current model.
- No executor-side skipping or ad-hoc condition evaluation inside targets.
- No migration path for descriptors that violate these invariants — they were never valid under the PR #91 contract.
