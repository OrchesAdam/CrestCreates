using CrestCreates.Runtime.Delivery.Abstractions.Composition;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using CrestCreates.Runtime.Delivery.Abstractions.Registration;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Runtime.Delivery.Registration;

internal sealed class OutboxCompositionValidator
{
    private readonly IServiceProvider _services;
    private readonly IReadOnlyList<OutboxDeliveryHandlerRegistration> _handlers;
    private readonly IReadOnlyList<OutboxRequiredConsumerMetadata> _consumers;
    private readonly IReadOnlyList<OutboxRequiredConsumerValidationRegistration> _validations;

    public OutboxCompositionValidator(
        IServiceProvider services,
        IEnumerable<OutboxDeliveryHandlerRegistration> handlers,
        IEnumerable<OutboxRequiredConsumerMetadata> consumers,
        IEnumerable<OutboxRequiredConsumerValidationRegistration> validations)
    {
        _services = services;
        _handlers = handlers.ToArray();
        _consumers = consumers.ToArray();
        _validations = validations.ToArray();
    }

    public void Validate()
    {
        EnsureUnique(_handlers.Select(h => h.ContractId), "delivery contract");
        EnsureUnique(_consumers.Select(c => c.ConsumerId), "required consumer");
        EnsureUnique(_validations.Select(c => c.ConsumerId), "required consumer validation");
        var consumerIds = _consumers.Select(c => c.ConsumerId).ToHashSet(StringComparer.Ordinal);
        if (!consumerIds.SetEquals(_validations.Select(c => c.ConsumerId)))
            throw new OutboxCompositionException("Required consumer metadata and validation registrations must match exactly.");

        using var scope = _services.CreateScope();
        foreach (var handler in _handlers)
        {
            var instance = handler.Resolve(scope.ServiceProvider) ?? throw new OutboxCompositionException($"Handler '{handler.ContractId}' resolved null.");
            if (!string.Equals(instance.ContractId, handler.ContractId, StringComparison.Ordinal))
                throw new OutboxCompositionException($"Handler registration '{handler.ContractId}' resolved a different contract.");
        }
        foreach (var validation in _validations)
            validation.ValidateResolution(scope.ServiceProvider);
    }

    private static void EnsureUnique(IEnumerable<string> values, string kind)
    {
        var all = values.ToArray();
        if (all.Any(string.IsNullOrWhiteSpace) || all.Any(value => value.Length > 256) || all.Distinct(StringComparer.Ordinal).Count() != all.Length)
            throw new OutboxCompositionException($"Duplicate or invalid {kind} registration.");
    }
}
