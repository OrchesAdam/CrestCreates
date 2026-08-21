using System.Collections.Immutable;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Accountability.Recording;

internal sealed class AuditSinkFanOut
{
    private readonly ImmutableArray<IAuditSink> _sinks;
    private readonly AccountabilityOptions _options;
    private readonly TimeProvider _time;

    public AuditSinkFanOut(IEnumerable<IAuditSink> sinks, AccountabilityOptions options, TimeProvider? time = null)
    {
        _sinks = sinks.OrderBy(x => x.Id, StringComparer.Ordinal).ToImmutableArray();
        _options = options;
        _time = time ?? TimeProvider.System;
    }

    public async ValueTask<AuditRecordResult> WriteAsync(AuditEnvelope envelope, CancellationToken cancellationToken)
    {
        var hash = envelope.Integrity!;
        if (_sinks.IsDefaultOrEmpty)
            return new() { AuditId = envelope.AuditId, Status = AuditRecordStatus.NoSinkConfigured, ProcessedAt = _time.GetUtcNow(), RecordHash = hash, Issues = _options.RequireAtLeastOneSink ? [new("AUDIT_NO_SINK_CONFIGURED")] : [] };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.WriteTimeout);
        var attempts = _sinks.Select(s => CaptureAsync(s, envelope, timeout.Token)).ToArray();
        try { await Task.WhenAll(attempts).WaitAsync(_options.WriteTimeout, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { timeout.Cancel(); }
        var results = ImmutableArray.CreateBuilder<AuditSinkWriteResult>();
        var failures = ImmutableArray.CreateBuilder<AuditSinkFailure>();
        for (var i = 0; i < attempts.Length; i++)
        {
            var attempt = attempts[i].IsCompletedSuccessfully ? await attempts[i].ConfigureAwait(false) : null;
            if (attempt is null) { failures.Add(new(_sinks[i].Id, "AUDIT_SINK_TIMEOUT")); continue; }
            if (!string.Equals(attempt.SinkId, _sinks[i].Id, StringComparison.Ordinal) || !string.Equals(attempt.AuditId, envelope.AuditId, StringComparison.Ordinal) || attempt.Integrity != hash)
            { failures.Add(new(_sinks[i].Id, "AUDIT_SINK_RESULT_IDENTITY_MISMATCH")); continue; }
            if (attempt.Status == AuditSinkWriteStatus.Conflict)
            {
                if (attempt.ExistingIntegrity is null || attempt.ExistingIntegrity == hash)
                    failures.Add(new(_sinks[i].Id, "AUDIT_SINK_RESULT_CONTRACT_MISMATCH"));
                else
                    failures.Add(new(_sinks[i].Id, "AUDIT_SINK_CONFLICT"));
                continue;
            }
            if (attempt.ExistingIntegrity is null)
            { failures.Add(new(_sinks[i].Id, "AUDIT_SINK_RESULT_CONTRACT_MISMATCH")); continue; }
            results.Add(attempt);
        }
        return new() { AuditId = envelope.AuditId, Status = failures.Count == 0 ? AuditRecordStatus.Recorded : results.Count > 0 ? AuditRecordStatus.PartiallyRecorded : AuditRecordStatus.Failed, ProcessedAt = _time.GetUtcNow(), RecordHash = hash, SinkResults = results.ToImmutable(), SinkFailures = failures.ToImmutable() };
    }

    private static async Task<AuditSinkWriteResult?> CaptureAsync(IAuditSink sink, AuditEnvelope envelope, CancellationToken cancellationToken)
    { try { return await sink.WriteAsync(envelope, cancellationToken).ConfigureAwait(false); } catch { return null; } }
}
