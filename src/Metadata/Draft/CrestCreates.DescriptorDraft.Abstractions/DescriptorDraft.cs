using CrestCreates.Metadata.Abstractions;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.DescriptorDraft.Abstractions;

public sealed record DescriptorDraft : ISnapshotable<DescriptorDraft>
{
    public required string TenantId { get; init; }
    public required string DraftId { get; init; }
    public required DescriptorKind DescriptorKind { get; init; }
    public required string DescriptorId { get; init; }
    public required DescriptorDraftOperation Operation { get; init; }
    public required DescriptorDraftAuthorKind AuthorKind { get; init; }
    public required string AuthorId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DescriptorDraftPayload Payload { get; init; }

    public string? BaseVersion { get; init; }
    public string? ProposedVersion { get; init; }
    public string? Intent { get; init; }
    public string? Rationale { get; init; }
    public string? CorrelationId { get; init; }
    public string? Source { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    public DescriptorDraftStatus Status { get; init; } = DescriptorDraftStatus.Created;

    /// <summary>
    /// Creates a defensive copy for store snapshot-on-read/write semantics.
    /// Payload descriptors deep-copy their own mutable backing collections so
    /// the stored snapshot does not share list or dictionary state with callers.
    /// Metadata dictionary is also copied to prevent shared mutable references.
    /// </summary>
    public DescriptorDraft Snapshot() => this with
    {
        Payload = Payload.Snapshot(),
        Metadata = Metadata is null
            ? null
            : new Dictionary<string, string>(Metadata, StringComparer.Ordinal)
    };
}
