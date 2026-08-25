namespace CrestCreates.Runtime.Delivery.Abstractions.Handlers;

public enum OutboxDeliveryOutcome
{
    Accepted,
    Duplicate,
    Retry,
    Conflict
}
