using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;

namespace CrestCreates.Runtime.Delivery.Abstractions.Handlers;

public sealed record OutboxDeliveryContext
{
    public required OutboxMessage Message { get; init; }
    public required OutboxDeliveryLease Lease { get; init; }
    public required DateTimeOffset AttemptDeadline { get; init; }
    public required IServiceProvider Services { get; init; }
}
