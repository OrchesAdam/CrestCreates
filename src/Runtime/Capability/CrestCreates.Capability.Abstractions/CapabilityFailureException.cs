namespace CrestCreates.Capability.Abstractions;

/// <summary>
/// Allows a handler to return a deterministic capability failure without
/// encoding an error as a successful output contract.
/// </summary>
public sealed class CapabilityFailureException : Exception
{
    public CapabilityFailureException(
        string errorCode,
        string safeMessage,
        IReadOnlyList<CapabilityExecutionIssue>? issues = null)
        : base(safeMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(safeMessage);
        ErrorCode = errorCode;
        Issues = issues ?? Array.Empty<CapabilityExecutionIssue>();
    }

    public string ErrorCode { get; }

    public IReadOnlyList<CapabilityExecutionIssue> Issues { get; }
}
