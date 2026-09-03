namespace CrestCreates.Runtime.Delivery.Abstractions.Messages;

/// <summary>
/// Applies the provider-neutral timestamp precision promised by the Runtime
/// outbox contract before metadata is hashed or persisted. The integrity
/// verifier remains an exact v1 canonical writer and has no provider rules.
/// </summary>
public static class OutboxMessageMetadataCanonicalizer
{
    public static OutboxMessageMetadata Normalize(OutboxMessageMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var createdAt = NormalizeTimestamp(metadata.CreatedAt == default ? DateTimeOffset.UtcNow : metadata.CreatedAt);
        var occurredAt = NormalizeTimestamp(metadata.OccurredAt == default ? createdAt : metadata.OccurredAt);
        return metadata with { CreatedAt = createdAt, OccurredAt = occurredAt };
    }

    /// <summary>
    /// Returns whether metadata already satisfies the provider-neutral
    /// timestamp contract. Writers validate this at their public boundary so
    /// manually-created messages cannot hash successfully and then change
    /// meaning when a provider stores timestamptz at microsecond precision.
    /// </summary>
    public static bool IsCanonical(OutboxMessageMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return metadata.CreatedAt != default
            && metadata.OccurredAt != default
            && IsCanonicalTimestamp(metadata.CreatedAt)
            && IsCanonicalTimestamp(metadata.OccurredAt);
    }

    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMicrosecond));
    }

    private static bool IsCanonicalTimestamp(DateTimeOffset value)
        => value.Offset == TimeSpan.Zero && value == NormalizeTimestamp(value);
}
