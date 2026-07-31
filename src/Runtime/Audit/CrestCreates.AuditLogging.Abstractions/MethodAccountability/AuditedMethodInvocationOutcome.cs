namespace CrestCreates.AuditLogging.Abstractions.MethodAccountability;

public enum AuditedMethodOutcomeKind
{
    Succeeded = 1,
    Failed = 2,
    Cancelled = 3
}

public sealed record AuditedMethodInvocationOutcome
{
    public required AuditedMethodOutcomeKind Kind { get; init; }
    public string? SafeCode { get; init; }
}
