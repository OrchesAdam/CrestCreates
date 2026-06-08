namespace CrestCreates.Capability.Abstractions;

public sealed class CapabilityExecutionContext
{
    public string CapabilityName { get; init; } = string.Empty;
    public int CapabilityVersion { get; init; }
    public string CapabilityContractHash { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
    public string? CausationId { get; set; }
    public string? TenantId { get; set; }
    public string? UserId { get; set; }
    public string IdempotencyKey { get; set; } = Guid.NewGuid().ToString("N");
    public object? Input { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public IDictionary<string, object?> Items { get; init; } = new Dictionary<string, object?>();
    public CancellationToken CancellationToken { get; init; }
}
