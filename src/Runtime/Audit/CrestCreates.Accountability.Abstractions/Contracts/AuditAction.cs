namespace CrestCreates.Accountability.Abstractions.Contracts;

public sealed record AuditAction
{
    public required string Kind { get; init; }
    public required string Name { get; init; }
}
