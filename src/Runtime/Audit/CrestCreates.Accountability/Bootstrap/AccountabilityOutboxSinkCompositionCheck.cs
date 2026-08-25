using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Accountability.Delivery;
using CrestCreates.Runtime.Delivery.Abstractions.Composition;
using CrestCreates.Runtime.Delivery.Abstractions.Registration;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Accountability.Bootstrap;

internal sealed class AccountabilityOutboxSinkCompositionCheck(
    IEnumerable<IAuditSink> sinks,
    IEnumerable<OutboxDeliveryHandlerRegistration> handlers,
    IServiceProvider services) : IOutboxDurableCompositionCheck
{
    public string CheckId => "accountability-outbox-sink-composition";

    public ValueTask ValidateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var enabled = services.GetService<IOutboxDispatchStore>() is not null
            && handlers.Any(handler => string.Equals(handler.ContractId, AccountabilityDeliveryConstants.ContractId, StringComparison.Ordinal));
        if (enabled && !sinks.Any())
            throw new OutboxCompositionException("Accountability outbox delivery requires at least one configured sink.");
        return ValueTask.CompletedTask;
    }
}
