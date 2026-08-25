using CrestCreates.Runtime.Delivery.Abstractions.Handlers;

namespace CrestCreates.Runtime.Delivery.Abstractions.Registration;

public sealed record OutboxDeliveryHandlerRegistration(string ContractId, Func<IServiceProvider, IOutboxDeliveryHandler> Resolve);
