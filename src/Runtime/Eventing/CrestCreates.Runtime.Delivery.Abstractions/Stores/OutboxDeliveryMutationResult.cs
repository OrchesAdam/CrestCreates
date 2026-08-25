namespace CrestCreates.Runtime.Delivery.Abstractions.Stores;

public enum OutboxDeliveryMutationResult
{
    Applied,
    Duplicate,
    AlreadyApplied,
    StaleLease,
    StaleFence,
    NotFound,
    Conflict
}
