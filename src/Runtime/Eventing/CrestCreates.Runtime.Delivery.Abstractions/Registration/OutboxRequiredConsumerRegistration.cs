using CrestCreates.Runtime.Delivery.Abstractions.Handlers;

namespace CrestCreates.Runtime.Delivery.Abstractions.Registration;

public sealed record OutboxRequiredConsumerRegistration<TPayload>(string ConsumerId, Func<IServiceProvider, IOutboxRequiredConsumer<TPayload>> Resolve);
