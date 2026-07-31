using CrestCreates.Accountability.Abstractions.Contracts;

namespace CrestCreates.Accountability.Abstractions.Context;

public sealed record AuditOperationContext
{
    public required string CorrelationId { get; init; }
    public required string OperationId { get; init; }
    public string? EnclosingAuditId { get; init; }
    public required AuditActor Actor { get; init; }
    public string? TenantId { get; init; }
    public required string InvocationSource { get; init; }

    /// <summary>Root producer operation retained across nested method/runtime scopes.</summary>
    public string? InitiatingOperationId { get; init; }

    /// <summary>Root producer fact retained across nested method/runtime scopes.</summary>
    public string? InitiatingAuditId { get; init; }
}

public interface IAuditOperationContextAccessor
{
    AuditOperationContext? Current { get; }
    IDisposable Push(AuditOperationContext context);
}

public sealed record AuditOrigin
{
    public required string CorrelationId { get; init; }
    public string? UpstreamOperationId { get; init; }
    public string? UpstreamAuditId { get; init; }
    public required AuditActor InitiatingActor { get; init; }
    public required string InvocationSource { get; init; }
}
