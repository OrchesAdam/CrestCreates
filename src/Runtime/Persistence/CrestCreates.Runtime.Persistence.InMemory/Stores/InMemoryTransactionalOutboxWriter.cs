using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using CrestCreates.Runtime.Persistence.InMemory.Transactions;
using CrestCreates.Runtime.Persistence.InMemory.Kernel;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;

namespace CrestCreates.Runtime.Persistence.InMemory.Stores;

internal sealed class InMemoryTransactionalOutboxWriter : ITransactionalOutboxWriter
{
    private readonly InMemoryRuntimeTransactionCoordinator _coordinator;
    public InMemoryTransactionalOutboxWriter(InMemoryRuntimeTransactionCoordinator coordinator) => _coordinator = coordinator;

    public ValueTask<OutboxAppendResult> AppendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OutboxMessageIntegrity.Matches(message))
            throw new RuntimePersistenceContractException(RuntimePersistenceContractErrorCode.PersistedInvariantViolation, "Outbox message integrity does not match its immutable payload.");
        var state = _coordinator.RequireAmbientState();
        if (state.Outbox.TryGetValue(message.Metadata.MessageId, out var existing))
        {
            if (!OutboxMessageIntegrity.Matches(existing.Message) || !existing.Message.Payload.AsSpan().SequenceEqual(message.Payload)
                || !string.Equals(existing.Message.Metadata.ContractId, message.Metadata.ContractId, StringComparison.Ordinal))
                throw new OutboxMessageConflictException($"Outbox message '{message.Metadata.MessageId}' conflicts with an existing message.");
            return ValueTask.FromResult(OutboxAppendResult.Duplicate);
        }
        state.Outbox.Add(message.Metadata.MessageId, new InMemoryOutboxRecord { Message = message.Snapshot() });
        return ValueTask.FromResult(OutboxAppendResult.Appended);
    }
}
