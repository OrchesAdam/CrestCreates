using CrestCreates.Runtime.Delivery.Abstractions.Messages;

namespace CrestCreates.Runtime.Delivery.Abstractions.Stores;

public interface ITransactionalOutboxWriter
{
    ValueTask<OutboxAppendResult> AppendAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
