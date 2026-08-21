namespace CrestCreates.Runtime.Delivery.Abstractions.Messages;

public sealed class OutboxMessage
{
    public required OutboxMessageMetadata Metadata { get; init; }
    public required byte[] Payload { get; init; }
    public required byte[] Integrity { get; init; }

    public OutboxMessage Snapshot() => new()
    {
        Metadata = Metadata with { RequiredConsumerIds = Metadata.RequiredConsumerIds.ToArray() },
        Payload = Payload.ToArray(),
        Integrity = Integrity.ToArray()
    };
}
