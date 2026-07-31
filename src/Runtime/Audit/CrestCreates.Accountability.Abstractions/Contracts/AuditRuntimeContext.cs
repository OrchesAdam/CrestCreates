using System.Collections.Immutable;

namespace CrestCreates.Accountability.Abstractions.Contracts;

public sealed record AuditRuntimeContext
{
    public string? InvocationSource { get; init; }
    public string? ExecutionId { get; init; }
    public string? RequestId { get; init; }
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
    public TimeSpan? Duration { get; init; }
    public ImmutableArray<AuditRuntimeReference> References { get; init; } = [];

    public static AuditRuntimeContext Empty { get; } = new();
}

public sealed record AuditRuntimeReference(string Kind, string Id);
