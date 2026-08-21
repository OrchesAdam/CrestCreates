namespace CrestCreates.Runtime.Delivery.Abstractions.Composition;

public interface IOutboxCompositionProbe
{
    ValueTask ValidateAsync(ActiveOutboxRequirements requirements, CancellationToken cancellationToken = default);
}
