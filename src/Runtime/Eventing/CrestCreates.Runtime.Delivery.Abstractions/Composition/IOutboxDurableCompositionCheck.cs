namespace CrestCreates.Runtime.Delivery.Abstractions.Composition;

internal interface IOutboxDurableCompositionCheck
{
    string CheckId { get; }
    ValueTask ValidateAsync(CancellationToken cancellationToken);
}
