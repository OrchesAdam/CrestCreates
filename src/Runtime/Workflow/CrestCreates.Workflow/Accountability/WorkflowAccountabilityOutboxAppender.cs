using System.Text.Json;
using CrestCreates.Accountability.Abstractions.Json;
using CrestCreates.Accountability.Abstractions.Preparation;
using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow.Accountability;

/// <summary>
/// Builds the immutable prepared accountability fact before a workflow state
/// transaction and appends it through that same transaction kernel.
/// </summary>
internal sealed class WorkflowAccountabilityOutboxAppender
{
    private readonly IAuditEnvelopePreparer? _preparer;
    private readonly ITransactionalOutboxWriter? _writer;
    private readonly IOutboxMessageFactory? _factory;

    public WorkflowAccountabilityOutboxAppender(
        IAuditEnvelopePreparer? preparer,
        ITransactionalOutboxWriter? writer,
        IOutboxMessageFactory? factory)
    {
        _preparer = preparer;
        _writer = writer;
        _factory = factory;
    }

    public bool IsEnabled => _preparer is not null && _writer is not null && _factory is not null;

    public async ValueTask<OutboxMessage?> PrepareAsync(WorkflowLifecycleEvent lifecycleEvent, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
            return null;
        var prepared = await _preparer!.PrepareAsync(WorkflowAccountabilityEnvelopeFactory.Create(lifecycleEvent), cancellationToken).ConfigureAwait(false);
        if (!prepared.IsAccepted || prepared.Envelope is null)
            throw new InvalidOperationException("Workflow Accountability envelope preparation was rejected.");
        var payload = JsonSerializer.SerializeToUtf8Bytes(prepared.Envelope, AccountabilityJsonSerializerContext.Default.AuditEnvelope);
        return _factory!.Create(new OutboxMessageMetadata
        {
            MessageId = prepared.Envelope.AuditId,
            TenantId = prepared.Envelope.TenantId,
            ContractId = "crest.accountability.audit-envelope/v1",
            PayloadTypeId = "CrestCreates.Accountability.AuditEnvelope/v1",
            EventName = lifecycleEvent.EventType,
            EventVersion = 1,
            CorrelationId = prepared.Envelope.CorrelationId,
            CausationId = prepared.Envelope.CausationId,
            OccurredAt = prepared.Envelope.OccurredAt,
            RequiredConsumerIds = [],
            CreatedAt = prepared.Envelope.OccurredAt
        }, prepared.Envelope, AccountabilityJsonSerializerContext.Default.AuditEnvelope);
    }

    public async ValueTask AppendAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (_writer is null)
            throw new InvalidOperationException("Workflow Accountability Outbox writer is not configured.");
        var result = await _writer.AppendAsync(message, cancellationToken).ConfigureAwait(false);
        if (result is not (OutboxAppendResult.Appended or OutboxAppendResult.Duplicate))
            throw new InvalidOperationException("Workflow Accountability Outbox append was not accepted.");
    }
}
