namespace CrestCreates.Accountability.Abstractions.Contracts;

public sealed record AuditActor
{
    public required string Kind { get; init; }
    public required string Id { get; init; }
    public string? DisplayName { get; init; }
    public AuditActorReference? InitiatedBy { get; init; }
    public AuditActorReference? OnBehalfOf { get; init; }
    public string? DelegationId { get; init; }
    public string? ImpersonationId { get; init; }
}

public sealed record AuditActorReference(string Kind, string Id);
