using CrestCreates.Runtime.Delivery.Abstractions.Messages;

namespace CrestCreates.Runtime.Delivery.Abstractions.Stores;

public sealed class OutboxDeliveryClaim
{
    public required OutboxMessage Message { get; init; }
    public required OutboxDeliveryStatus Status { get; init; }
    public required OutboxDeliveryLease Lease { get; init; }
    public DateTimeOffset? NextAttemptAt { get; init; }
    public string? LastFailureCode { get; init; }
}
