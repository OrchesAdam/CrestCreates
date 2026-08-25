namespace CrestCreates.Runtime.Delivery.Abstractions.Stores;

public sealed record OutboxDeliveryLease
{
    public required string OwnerId { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required int Attempt { get; init; }
    public required long Fence { get; init; }
}
