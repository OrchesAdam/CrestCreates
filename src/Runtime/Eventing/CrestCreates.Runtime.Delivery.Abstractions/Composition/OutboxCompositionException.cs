namespace CrestCreates.Runtime.Delivery.Abstractions.Composition;

public sealed class OutboxCompositionException : InvalidOperationException
{
    public OutboxCompositionException(string message) : base(message) { }
    public OutboxCompositionException(string message, Exception innerException) : base(message, innerException) { }
}
