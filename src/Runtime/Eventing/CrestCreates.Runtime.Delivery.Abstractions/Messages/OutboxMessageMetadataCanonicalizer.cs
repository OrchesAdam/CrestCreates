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

    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
