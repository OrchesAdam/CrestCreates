using CrestCreates.Accountability.Abstractions.Contracts;

namespace CrestCreates.Accountability.Abstractions.Recording;

public interface IAuditRecorder
{
    ValueTask<AuditRecordResult> RecordAsync(
        AuditEnvelope envelope,
        CancellationToken cancellationToken = default);
}
