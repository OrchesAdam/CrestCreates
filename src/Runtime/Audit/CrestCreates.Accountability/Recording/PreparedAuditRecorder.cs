using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Hashing;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Accountability.Validation;

namespace CrestCreates.Accountability.Recording;

internal sealed class PreparedAuditRecorder
{
    private readonly AuditEnvelopeValidator _validator;
    private readonly IAuditIntegrityHasher _hasher;
    private readonly AuditSinkFanOut _fanOut;
    private readonly TimeProvider _time;

    public PreparedAuditRecorder(AuditEnvelopeValidator validator, IAuditIntegrityHasher hasher, AuditSinkFanOut fanOut, TimeProvider? time = null)
    {
        _validator = validator;
        _hasher = hasher;
        _fanOut = fanOut;
        _time = time ?? TimeProvider.System;
    }

    public async ValueTask<AuditRecordResult> RecordPreparedAsync(AuditEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (!_validator.ValidateSafeSnapshot(envelope).IsValid || envelope.Sanitization is null || envelope.Integrity is null)
            return new() { AuditId = envelope.AuditId, Status = AuditRecordStatus.Rejected, ProcessedAt = _time.GetUtcNow(), Issues = [new("AUDIT_PREPARED_ENVELOPE_INVALID")] };
        var expected = _hasher.Compute(envelope with { Integrity = null });
        if (expected != envelope.Integrity)
            return new() { AuditId = envelope.AuditId, Status = AuditRecordStatus.Rejected, ProcessedAt = _time.GetUtcNow(), Issues = [new("AUDIT_PREPARED_INTEGRITY_MISMATCH", "Integrity")] };
        return await _fanOut.WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
    }
}
