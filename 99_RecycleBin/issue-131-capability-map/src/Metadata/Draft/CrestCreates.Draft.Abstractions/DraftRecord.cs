using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Draft.Abstractions;

public sealed class DraftRecord
{
    public string DraftId { get; init; } = string.Empty;
    public string DraftType { get; init; } = string.Empty;
    public VersionedDescriptorRef<SchemaDescriptor> Schema { get; init; }
    public string TenantId { get; init; } = string.Empty;
    public string? OwnerId { get; init; }
    public string PayloadJson { get; init; } = "{}";
    public DraftStatus Status { get; init; } = DraftStatus.Active;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; init; }
}