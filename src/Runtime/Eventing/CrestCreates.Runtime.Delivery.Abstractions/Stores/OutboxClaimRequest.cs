namespace CrestCreates.Runtime.Delivery.Abstractions.Stores;

public sealed record OutboxClaimRequest
{
    public required string OwnerId { get; init; }
    public int BatchSize { get; init; } = 32;
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(30);
    public DateTimeOffset? Now { get; init; }
}
