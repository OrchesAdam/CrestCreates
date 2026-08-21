using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Json;
using CrestCreates.Accountability.Recording;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;

namespace CrestCreates.Accountability.Delivery;

internal sealed class AccountabilityOutboxHandler : IOutboxDeliveryHandler
{
    private delegate AuditEnvelope? SourceGeneratedReader(string json, JsonTypeInfo<AuditEnvelope> typeInfo);
    private static readonly SourceGeneratedReader ReadEnvelope = JsonSerializer.Deserialize;

    private readonly PreparedAuditRecorder _recorder;
    public AccountabilityOutboxHandler(PreparedAuditRecorder recorder) => _recorder = recorder;
    public string ContractId => AccountabilityDeliveryConstants.ContractId;

    public async ValueTask<OutboxDeliveryOutcome> HandleAsync(OutboxDeliveryContext context, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(context.Message.Metadata.ContractId, ContractId, StringComparison.Ordinal))
            return OutboxDeliveryOutcome.Conflict;
        AuditEnvelope? envelope;
        try { envelope = ReadEnvelope(context.Message.Payload, AccountabilityJsonSerializerContext.Default.AuditEnvelope); }
        catch (JsonException) { return OutboxDeliveryOutcome.Conflict; }
        if (envelope is null || !string.Equals(envelope.AuditId, context.Message.Metadata.MessageId, StringComparison.Ordinal) ||
            !string.Equals(context.Message.Metadata.PayloadTypeId, AccountabilityDeliveryConstants.PayloadTypeId, StringComparison.Ordinal))
            return OutboxDeliveryOutcome.Conflict;
        var result = await _recorder.RecordPreparedAsync(envelope, cancellationToken).ConfigureAwait(false);
        return result.Status switch
        {
            AuditRecordStatus.Recorded => OutboxDeliveryOutcome.Accepted,
            AuditRecordStatus.PartiallyRecorded or AuditRecordStatus.Failed =>
                result.SinkFailures.Any(x => x.Code == "AUDIT_SINK_CONFLICT") ? OutboxDeliveryOutcome.Conflict : OutboxDeliveryOutcome.Retry,
            AuditRecordStatus.NoSinkConfigured => OutboxDeliveryOutcome.Retry,
            _ => OutboxDeliveryOutcome.Conflict
        };
    }
}
