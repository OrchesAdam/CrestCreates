using CrestCreates.HumanTask.Abstractions;

namespace CrestCreates.HumanTask;

internal static class CompletionOutcomeMatcher
{
    public static bool Matches(CompletionOutcome outcome, string requestOutcome)
        => outcome.Condition.ToString().Equals(requestOutcome, StringComparison.OrdinalIgnoreCase);

    public static CompletionOutcome Resolve(HumanTaskDescriptor descriptor, string outcome)
    {
        var matches = descriptor.Outcomes
            .Where(o => Matches(o, outcome))
            .ToList();

        if (matches.Count == 0)
            throw new InvalidOperationException(
                $"Outcome '{outcome}' not found in HumanTask '{descriptor.Id}' v{descriptor.Version}.");

        if (matches.Count > 1)
            throw new InvalidOperationException(
                $"Multiple outcomes match '{outcome}' in HumanTask '{descriptor.Id}'. " +
                "Identifier-based matching not yet supported.");

        var matched = matches[0];
        if (matched.Condition == CompletionCondition.CustomExpression)
            throw new NotSupportedException(
                "CustomExpression outcome evaluation is not supported in Phase 5.");

        return matched;
    }
}
