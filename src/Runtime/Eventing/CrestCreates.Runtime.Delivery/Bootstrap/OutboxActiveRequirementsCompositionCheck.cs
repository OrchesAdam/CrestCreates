using CrestCreates.Runtime.Delivery.Abstractions.Composition;
using CrestCreates.Runtime.Delivery.Abstractions.Registration;

namespace CrestCreates.Runtime.Delivery.Bootstrap;

internal sealed class OutboxActiveRequirementsCompositionCheck : IOutboxDurableCompositionCheck
{
    private readonly IOutboxCompositionProbe _probe;
    private readonly IReadOnlyList<string> _contractIds;
    private readonly IReadOnlyList<string> _consumerIds;

    public OutboxActiveRequirementsCompositionCheck(
        IOutboxCompositionProbe probe,
        IEnumerable<OutboxDeliveryHandlerRegistration> handlers,
        IEnumerable<OutboxRequiredConsumerMetadata> consumers)
    {
        _probe = probe;
        _contractIds = handlers.Select(handler => handler.ContractId).ToArray();
        _consumerIds = consumers.Select(consumer => consumer.ConsumerId).ToArray();
    }

    public string CheckId => "runtime-delivery-active-requirements";

    public ValueTask ValidateAsync(CancellationToken cancellationToken)
        => _probe.ValidateAsync(
            new ActiveOutboxRequirements(
                _contractIds.ToHashSet(StringComparer.Ordinal),
                _consumerIds.ToHashSet(StringComparer.Ordinal)),
            cancellationToken);
}
