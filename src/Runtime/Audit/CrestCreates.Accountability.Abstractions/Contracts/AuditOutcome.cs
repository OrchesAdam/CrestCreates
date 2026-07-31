namespace CrestCreates.Accountability.Abstractions.Contracts;

public sealed record AuditOutcome
{
    public required string Status { get; init; }
    public string? Code { get; init; }
    public string? SafeSummary { get; init; }
}
