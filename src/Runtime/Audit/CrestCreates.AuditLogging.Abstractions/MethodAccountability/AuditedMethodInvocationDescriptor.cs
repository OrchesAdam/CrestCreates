namespace CrestCreates.AuditLogging.Abstractions.MethodAccountability;

public sealed record AuditedMethodInvocationDescriptor
{
    public required string MethodId { get; init; }
    public required string ActionName { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
}
