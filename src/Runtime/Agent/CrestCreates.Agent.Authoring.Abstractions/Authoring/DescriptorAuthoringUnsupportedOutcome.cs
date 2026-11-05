namespace CrestCreates.Agent.Authoring.Abstractions.Authoring;

/// <summary>
/// Structured refusal returned when the intent needs a capability, operation, or governance
/// decision that the supported authoring surface does not provide. Carries no drafts.
/// </summary>
public sealed record DescriptorAuthoringUnsupportedOutcome
{
    public required IReadOnlyList<DescriptorAuthoringUnsupportedReason> Reasons { get; init; }

    public DescriptorAuthoringUnsupportedOutcome Snapshot() => this with
    {
        Reasons = Reasons.Select(r => r.Snapshot()).ToArray()
    };
}

public sealed record DescriptorAuthoringUnsupportedReason
{
    /// <summary>Closed <c>unsupported.reasons[].code</c> vocabulary; see <see cref="DescriptorAuthoringUnsupportedReasonCodes"/>.</summary>
    public required string Code { get; init; }

    public required string Message { get; init; }

    public IReadOnlyList<string> EvidenceRefs { get; init; } = Array.Empty<string>();

    public DescriptorAuthoringUnsupportedReason Snapshot() => this with
    {
        EvidenceRefs = EvidenceRefs.ToArray()
    };
}

/// <summary>The closed code table for <see cref="DescriptorAuthoringUnsupportedReason.Code"/>.</summary>
public static class DescriptorAuthoringUnsupportedReasonCodes
{
    public const string CapabilityUnsupported = "CAPABILITY_UNSUPPORTED";
    public const string OperationUnsupported = "OPERATION_UNSUPPORTED";
    public const string GovernanceBoundary = "GOVERNANCE_BOUNDARY";

    public static IReadOnlyList<string> All { get; } =
    [
        CapabilityUnsupported,
        OperationUnsupported,
        GovernanceBoundary
    ];

    public static bool IsKnown(string? code) => code is not null && All.Contains(code, StringComparer.Ordinal);
}
