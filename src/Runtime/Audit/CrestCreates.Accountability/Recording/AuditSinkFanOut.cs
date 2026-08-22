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
            var capture = attempts[i].IsCompletedSuccessfully ? await attempts[i].ConfigureAwait(false) : null;
            if (capture is null)
            {
                failures.Add(new(_sinks[i].Id, "AUDIT_SINK_TIMEOUT"));
                continue;
            }
            if (capture.Result is null)
            {
                failures.Add(new(_sinks[i].Id, capture.FailureCode ?? "AUDIT_SINK_FAILURE"));
                continue;
            }
            var attempt = capture.Result;
            if (!string.Equals(attempt.SinkId, _sinks[i].Id, StringComparison.Ordinal) || !string.Equals(attempt.AuditId, envelope.AuditId, StringComparison.Ordinal) || attempt.Integrity != hash)
            { failures.Add(new(_sinks[i].Id, "AUDIT_SINK_RESULT_IDENTITY_MISMATCH")); continue; }
            if (attempt.Status == AuditSinkWriteStatus.Conflict)
            {
                if (attempt.ExistingIntegrity is null || attempt.ExistingIntegrity == hash)
                    failures.Add(new(_sinks[i].Id, "AUDIT_SINK_RESULT_CONTRACT_MISMATCH"));
                else
                    results.Add(attempt);
                continue;
            }
            if (attempt.Status is not (AuditSinkWriteStatus.Accepted or AuditSinkWriteStatus.Duplicate))
            { failures.Add(new(_sinks[i].Id, "AUDIT_SINK_RESULT_CONTRACT_MISMATCH")); continue; }
            results.Add(attempt);
        }
        var completedResults = results.ToImmutable();
        var hasAcceptedSink = completedResults.Any(x => x.Status is AuditSinkWriteStatus.Accepted or AuditSinkWriteStatus.Duplicate);
        var hasNonAcceptedOutcome = completedResults.Any(x => x.Status == AuditSinkWriteStatus.Conflict) || failures.Count > 0;
        var status = hasAcceptedSink
            ? hasNonAcceptedOutcome ? AuditRecordStatus.PartiallyRecorded : AuditRecordStatus.Recorded
            : AuditRecordStatus.Failed;
        return new() { AuditId = envelope.AuditId, Status = status, ProcessedAt = _time.GetUtcNow(), RecordHash = hash, SinkResults = completedResults, SinkFailures = failures.ToImmutable() };
    }

    private static async Task<SinkCapture> CaptureAsync(IAuditSink sink, AuditEnvelope envelope, CancellationToken cancellationToken)
    {
        try
        {
            return new(await sink.WriteAsync(envelope, cancellationToken).ConfigureAwait(false), null);
        }
        catch (OperationCanceledException)
        {
            return new(null, "AUDIT_SINK_TIMEOUT");
        }
        catch
        {
            return new(null, "AUDIT_SINK_FAILURE");
        }
    }

    private sealed record SinkCapture(AuditSinkWriteResult? Result, string? FailureCode);
}
