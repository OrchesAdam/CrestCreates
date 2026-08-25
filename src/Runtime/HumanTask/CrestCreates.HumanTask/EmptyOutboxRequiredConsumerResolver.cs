using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using CrestCreates.Runtime.Delivery.Abstractions.Registration;
using CrestCreates.HumanTask.Abstractions;

namespace CrestCreates.HumanTask;

internal sealed class EmptyOutboxRequiredConsumerResolver : IOutboxRequiredConsumerResolver<HumanTaskCompletedEvent>
{
    private readonly IReadOnlyDictionary<string, Func<IServiceProvider, IOutboxRequiredConsumer<HumanTaskCompletedEvent>>> _resolvers;

    public EmptyOutboxRequiredConsumerResolver(
        IEnumerable<OutboxRequiredConsumerRegistration<HumanTaskCompletedEvent>> registrations)
    {
        _resolvers = registrations
            .GroupBy(item => item.ConsumerId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Single().Resolve, StringComparer.Ordinal);
    }

    public IOutboxRequiredConsumer<HumanTaskCompletedEvent> Resolve(IServiceProvider services, string consumerId)
        => _resolvers.TryGetValue(consumerId, out var resolver)
            ? resolver(services)
            : throw new InvalidOperationException($"No required HumanTask completion consumer '{consumerId}' is registered.");
}
