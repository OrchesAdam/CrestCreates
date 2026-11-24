using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

internal enum WorkflowConditionKind
{
    PreviousHumanTaskApproved,
    PreviousHumanTaskRejected,
    AllPriorHumanTasksApproved
}

/// <summary>
/// The single parser, descriptor validator, and runtime evaluator for the
/// bounded Workflow condition contract.
/// </summary>
internal static class WorkflowConditionPolicy
{
    public static IReadOnlyList<string> ValidateDescriptor(WorkflowDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var errors = new List<string>();
        for (var index = 0; index < descriptor.Steps.Count; index++)
        {
            var error = GetValidationError(descriptor, index);
            if (error is not null)
                errors.Add(error);
        }

        return errors;
    }

    public static string? GetValidationError(WorkflowDescriptor descriptor, int index)
    {
        var step = descriptor.Steps[index];
        if (step.Condition is null)
            return null;

        if (!TryParse(step.Condition, out var kind))
            return $"Workflow step '{step.Id}' declares unsupported condition '{step.Condition}'. " +
                $"Supported conditions are '{WorkflowConditionTokens.PreviousHumanTaskApproved}', " +
                $"'{WorkflowConditionTokens.PreviousHumanTaskRejected}' and " +
                $"'{WorkflowConditionTokens.AllPriorHumanTasksApproved}'.";

        if (kind == WorkflowConditionKind.AllPriorHumanTasksApproved)
            return GetAllPriorValidationError(descriptor, index);

        if (index == 0 || descriptor.Steps[index - 1].Target is not HumanTaskTarget)
            return $"Workflow step '{step.Id}' condition requires the immediately preceding step to target a HumanTask.";

        if (descriptor.Steps[index - 1].Condition is not null)
            return $"Workflow step '{step.Id}' condition requires the immediately preceding step to be unconditional; " +
                $"a conditional predecessor may be skipped, producing non-deterministic composition.";

        return null;
    }

    public static bool Evaluate(
        WorkflowDescriptor descriptor,
        WorkflowInstance instance,
        int index,
        IRuntimeStateContractRegistry stateRegistry)
    {
        var step = descriptor.Steps[index];
        if (step.Condition is null)
            return true;
        if (GetValidationError(descriptor, index) is { } validationError)
            throw new WorkflowValidationException(validationError);

        var condition = Parse(step.Condition);
        if (condition == WorkflowConditionKind.AllPriorHumanTasksApproved)
            return EvaluateAllPrior(descriptor, instance, index);

        var precedingStep = descriptor.Steps[index - 1];
        var precedingResult = instance.StepResults.LastOrDefault(result =>
            string.Equals(result.StepId, precedingStep.Id, StringComparison.Ordinal));
        if (precedingResult is null || precedingResult.Status != StepExecutionStatus.Completed)
            throw new WorkflowValidationException(
                $"Workflow step '{step.Id}' condition requires a completed result for immediately preceding HumanTask step '{precedingStep.Id}'.");

        if (!instance.Variables.TryGetValue("lastStepOutcome", out var encodedOutcome))
            throw new WorkflowValidationException(
                $"Workflow step '{step.Id}' condition requires persisted lastStepOutcome evidence from '{precedingStep.Id}'.");

        string? outcome;
        try
        {
            outcome = stateRegistry.Restore<string>(encodedOutcome);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new WorkflowValidationException(
                $"Workflow step '{step.Id}' condition has malformed persisted lastStepOutcome evidence.", exception);
        }

        var matchesApproved = string.Equals(outcome, CompletionCondition.Approve.ToString(), StringComparison.Ordinal);
        var matchesRejected = string.Equals(outcome, CompletionCondition.Reject.ToString(), StringComparison.Ordinal);
        if (!matchesApproved && !matchesRejected)
            throw new WorkflowValidationException(
                $"Workflow step '{step.Id}' condition has unsupported persisted lastStepOutcome '{outcome ?? "<null>"}'.");

        return condition == WorkflowConditionKind.PreviousHumanTaskApproved
            ? matchesApproved
            : matchesRejected;
    }

    private static string? GetAllPriorValidationError(WorkflowDescriptor descriptor, int index)
    {
        var step = descriptor.Steps[index];
        if (index == 0)
            return $"Workflow step '{step.Id}' condition requires at least one preceding HumanTask step.";

        for (var priorIndex = 0; priorIndex < index; priorIndex++)
        {
            var prior = descriptor.Steps[priorIndex];
            if (prior.Target is not HumanTaskTarget)
                return $"Workflow step '{step.Id}' condition requires every preceding step to target a HumanTask; step '{prior.Id}' does not.";
            if (prior.Condition is not null
                && prior.Condition != WorkflowConditionTokens.PreviousHumanTaskApproved
                && prior.Condition != WorkflowConditionTokens.AllPriorHumanTasksApproved)
                return $"Workflow step '{step.Id}' condition permits only approval-gated conditional predecessors; step '{prior.Id}' declares '{prior.Condition}'.";
        }

        return null;
    }

    private static bool EvaluateAllPrior(WorkflowDescriptor descriptor, WorkflowInstance instance, int index)
    {
        var step = descriptor.Steps[index];
        // Ordered proof inspection makes an early rejection decisive even when
        // later approval-gated tasks were skipped and have no outcome evidence.
        for (var priorIndex = 0; priorIndex < index; priorIndex++)
        {
            var prior = descriptor.Steps[priorIndex];
            var result = instance.StepResults.LastOrDefault(value =>
                string.Equals(value.StepId, prior.Id, StringComparison.Ordinal));
            if (result is null || result.Status != StepExecutionStatus.Completed)
                throw new WorkflowValidationException(
                    $"Workflow step '{step.Id}' condition requires a completed result for prior HumanTask step '{prior.Id}'.");
            if (result.HumanTaskOutcome is not { } outcome)
                throw new WorkflowValidationException(
                    $"Workflow step '{step.Id}' condition requires persisted HumanTask outcome evidence from '{prior.Id}'.");
            if (!Enum.IsDefined(outcome))
                throw new WorkflowValidationException(
                    $"Workflow step '{step.Id}' condition has malformed persisted HumanTask outcome '{outcome}' from '{prior.Id}'.");
            if (outcome == CompletionCondition.Reject)
                return false;
            if (outcome != CompletionCondition.Approve)
                throw new WorkflowValidationException(
                    $"Workflow step '{step.Id}' condition has unsupported persisted HumanTask outcome '{outcome}' from '{prior.Id}'.");
        }

        return true;
    }

    private static WorkflowConditionKind Parse(string condition)
    {
        if (string.Equals(condition, WorkflowConditionTokens.PreviousHumanTaskApproved, StringComparison.Ordinal))
            return WorkflowConditionKind.PreviousHumanTaskApproved;
        if (string.Equals(condition, WorkflowConditionTokens.PreviousHumanTaskRejected, StringComparison.Ordinal))
            return WorkflowConditionKind.PreviousHumanTaskRejected;
        if (string.Equals(condition, WorkflowConditionTokens.AllPriorHumanTasksApproved, StringComparison.Ordinal))
            return WorkflowConditionKind.AllPriorHumanTasksApproved;

        throw new WorkflowValidationException($"Unsupported Workflow condition '{condition}'.");
    }

    private static bool TryParse(string condition, out WorkflowConditionKind kind)
    {
        if (string.Equals(condition, WorkflowConditionTokens.PreviousHumanTaskApproved, StringComparison.Ordinal))
        {
            kind = WorkflowConditionKind.PreviousHumanTaskApproved;
            return true;
        }
        if (string.Equals(condition, WorkflowConditionTokens.PreviousHumanTaskRejected, StringComparison.Ordinal))
        {
            kind = WorkflowConditionKind.PreviousHumanTaskRejected;
            return true;
        }
        if (string.Equals(condition, WorkflowConditionTokens.AllPriorHumanTasksApproved, StringComparison.Ordinal))
        {
            kind = WorkflowConditionKind.AllPriorHumanTasksApproved;
            return true;
        }

        kind = default;
        return false;
    }
}
