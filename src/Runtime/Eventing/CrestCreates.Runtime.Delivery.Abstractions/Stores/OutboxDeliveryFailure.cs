namespace CrestCreates.Runtime.Delivery.Abstractions.Stores;

public sealed record OutboxDeliveryFailure
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public bool Retryable { get; init; }
    public DateTimeOffset? ObservedAt { get; init; }
}
