namespace CrestCreates.Runtime.Delivery.Abstractions.Messages;

public sealed class OutboxMessageConflictException : InvalidOperationException
{
    public OutboxMessageConflictException(string message) : base(message) { }
}
