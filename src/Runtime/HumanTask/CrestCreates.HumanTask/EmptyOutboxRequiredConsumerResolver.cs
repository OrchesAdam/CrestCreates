using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using CrestCreates.Runtime.Delivery.Abstractions.Registration;
using CrestCreates.HumanTask.Abstractions;

namespace CrestCreates.HumanTask;

internal sealed class EmptyOutboxRequiredConsumerResolver : IOutboxRequiredConsumerResolver<HumanTaskCompletedEvent>
{
    public IOutboxRequiredConsumer<HumanTaskCompletedEvent> Resolve(IServiceProvider services, string consumerId)
        => throw new InvalidOperationException($"No required HumanTask completion consumer '{consumerId}' is registered.");
}
