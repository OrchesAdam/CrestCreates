namespace CrestCreates.Runtime.Delivery.Abstractions.Stores;

public sealed record OutboxClaimRequest
{
    public required string OwnerId { get; init; }
    public int BatchSize { get; init; } = 32;
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(30);
    public DateTimeOffset? Now { get; init; }

    /// <summary>
    /// The immutable composition snapshot used by a provider to close the
    /// readiness-to-claim race. Null is retained only for direct legacy store
    /// callers; the hosted dispatcher always supplies both sets.
    /// </summary>
    public IReadOnlySet<string>? SupportedContractIds { get; init; }
    public IReadOnlySet<string>? SupportedRequiredConsumerIds { get; init; }
}
