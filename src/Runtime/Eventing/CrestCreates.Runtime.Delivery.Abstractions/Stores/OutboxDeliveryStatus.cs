namespace CrestCreates.Runtime.Delivery.Abstractions.Stores;

public enum OutboxDeliveryStatus
{
    Pending,
    InFlight,
    RetryDue,
    Delivered,
    DeadLettered
}
