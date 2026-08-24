namespace CrestCreates.Runtime.Delivery.Abstractions.Stores;

public enum OutboxDeliveryStatus
{
    Pending,
    InFlight,
    Delivered,
    DeadLettered
}
