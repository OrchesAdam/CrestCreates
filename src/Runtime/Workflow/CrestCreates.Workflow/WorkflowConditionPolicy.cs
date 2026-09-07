using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

internal enum WorkflowConditionKind
{
    PreviousHumanTaskApproved,
    PreviousHumanTaskRejected
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

        if (!TryParse(step.Condition, out _))
            return $"Workflow step '{step.Id}' declares unsupported condition '{step.Condition}'. " +
                $"Supported conditions are '{WorkflowConditionTokens.PreviousHumanTaskApproved}' and " +
                $"'{WorkflowConditionTokens.PreviousHumanTaskRejected}'.";

        if (index == 0 || descriptor.Steps[index - 1].Target is not HumanTaskTarget)
            return $"Workflow step '{step.Id}' condition requires the immediately preceding step to target a HumanTask.";

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

    private static WorkflowConditionKind Parse(string condition)
    {
        if (string.Equals(condition, WorkflowConditionTokens.PreviousHumanTaskApproved, StringComparison.Ordinal))
            return WorkflowConditionKind.PreviousHumanTaskApproved;
        if (string.Equals(condition, WorkflowConditionTokens.PreviousHumanTaskRejected, StringComparison.Ordinal))
            return WorkflowConditionKind.PreviousHumanTaskRejected;

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

        kind = default;
        return false;
    }
}
