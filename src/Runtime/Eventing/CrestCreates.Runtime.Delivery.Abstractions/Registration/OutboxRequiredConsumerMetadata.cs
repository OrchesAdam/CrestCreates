using CrestCreates.Runtime.Delivery.Abstractions.Handlers;

namespace CrestCreates.Runtime.Delivery.Abstractions.Registration;

public sealed record OutboxRequiredConsumerMetadata(string ConsumerId);

public sealed record OutboxRequiredConsumerValidationRegistration(string ConsumerId, Action<IServiceProvider> ValidateResolution);

public interface IOutboxRequiredConsumerResolver<TPayload>
{
    IOutboxRequiredConsumer<TPayload> Resolve(IServiceProvider services, string consumerId);
}
