using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using CrestCreates.Runtime.Delivery.Abstractions.Registration;

namespace CrestCreates.Runtime.Delivery.Registration;

internal sealed class OutboxRequiredConsumerResolver<TPayload> : IOutboxRequiredConsumerResolver<TPayload>
{
    private readonly IReadOnlyDictionary<string, Func<IServiceProvider, IOutboxRequiredConsumer<TPayload>>> _resolvers;

    public OutboxRequiredConsumerResolver(IEnumerable<OutboxRequiredConsumerRegistration<TPayload>> registrations)
    {
        _resolvers = registrations
            .GroupBy(item => item.ConsumerId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Single().Resolve, StringComparer.Ordinal);
    }

    public IOutboxRequiredConsumer<TPayload> Resolve(IServiceProvider services, string consumerId)
        => _resolvers.TryGetValue(consumerId, out var resolver)
            ? resolver(services)
            : throw new InvalidOperationException($"No required Outbox consumer '{consumerId}' is registered for payload '{typeof(TPayload).Name}'.");
}
