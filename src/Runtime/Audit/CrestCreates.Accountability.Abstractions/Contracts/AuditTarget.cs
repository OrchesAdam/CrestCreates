namespace CrestCreates.Accountability.Abstractions.Contracts;

public sealed record AuditTarget
{
    public required string Kind { get; init; }
    public required string Id { get; init; }
    public string? Version { get; init; }
}
