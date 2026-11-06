namespace CrestCreates.Agent.Authoring.Abstractions.Authoring;

/// <summary>
/// Structured clarification request returned when the intent cannot be authored from the
/// information supplied. Carries no drafts; answering it stays application-side.
/// </summary>
public sealed record DescriptorAuthoringClarificationOutcome
{
    public required IReadOnlyList<DescriptorAuthoringClarificationQuestion> Questions { get; init; }

    public DescriptorAuthoringClarificationOutcome Snapshot() => this with
    {
        Questions = Questions.Select(q => q.Snapshot()).ToArray()
    };
}

public sealed record DescriptorAuthoringClarificationQuestion
{
    public required string Id { get; init; }

    /// <summary>Closed <c>needsClarification.questions[].reasonCode</c> vocabulary.</summary>
    public required string ReasonCode { get; init; }

    public required string Question { get; init; }

    public IReadOnlyList<string> MissingInputs { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> CandidateValues { get; init; } = Array.Empty<string>();

    public DescriptorAuthoringClarificationQuestion Snapshot() => this with
    {
        MissingInputs = MissingInputs.ToArray(),
        CandidateValues = CandidateValues.ToArray()
    };
}

/// <summary>The closed code table for <see cref="DescriptorAuthoringClarificationQuestion.ReasonCode"/>.</summary>
public static class DescriptorAuthoringClarificationReasonCodes
{
    public const string MissingPolicyInput = "MISSING_POLICY_INPUT";
    public const string AmbiguousTarget = "AMBIGUOUS_TARGET";
    public const string ConflictingRequirements = "CONFLICTING_REQUIREMENTS";
    public const string MissingReferenceSelection = "MISSING_REFERENCE_SELECTION";

    public static IReadOnlyList<string> All { get; } =
    [
        MissingPolicyInput,
        AmbiguousTarget,
        ConflictingRequirements,
        MissingReferenceSelection
    ];

    public static bool IsKnown(string? reasonCode) => reasonCode is not null && All.Contains(reasonCode, StringComparer.Ordinal);
}
