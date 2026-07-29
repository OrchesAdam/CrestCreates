using System.Text.Json;
using System.Collections.Immutable;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Capability.Abstractions;

public sealed class CapabilityExecutionContext
{
    public string CapabilityId { get; init; } = string.Empty;
    public string CapabilityName { get; init; } = string.Empty;
    public int CapabilityVersion { get; init; }
    public string CapabilityContractHash { get; init; } = string.Empty;
    public CanonicalHash? AccountabilityContract { get; set; }
    public InvocationSource InvocationSource { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? CausationId { get; set; }
    public string? ParentAuditId { get; set; }
    public AuditActor? AccountabilityActor { get; set; }
    public ImmutableArray<AuditRuntimeReference> AccountabilityRuntimeReferences { get; set; } = [];
    public string? AuditRecordId { get; internal set; }
    public string? ExecutionId { get; internal set; }
    public string? TenantId { get; set; }
    public string? UserId { get; set; }
    public string IdempotencyKey { get; set; } = Guid.NewGuid().ToString("N");
    public object? Input { get; set; }
    public JsonElement? InputJson { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public IDictionary<string, object?> Items { get; init; } = new Dictionary<string, object?>();
    public IReadOnlyList<string> RequiredPermissions { get; set; } = Array.Empty<string>();
    public CancellationToken CancellationToken { get; init; }
    public required IServiceProvider ServiceProvider { get; init; }
}
