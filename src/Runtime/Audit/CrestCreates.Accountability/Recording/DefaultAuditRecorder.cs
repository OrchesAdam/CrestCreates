using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Hashing;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Accountability.Abstractions.Preparation;
using CrestCreates.Accountability.Preparation;
using CrestCreates.Accountability.Abstractions.Sanitization;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Accountability.CanonicalHashing;
using CrestCreates.Accountability.Sanitization;
using CrestCreates.Accountability.Validation;
using CrestCreates.Accountability.Abstractions.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CrestCreates.Accountability.Recording;

public sealed class DefaultAuditRecorder : IAuditRecorder
{
    private readonly IAuditEnvelopePreparer _preparer;
    private readonly ImmutableArray<IAuditSink> _sinks;
    private readonly AccountabilityOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly AuditSinkFanOut _fanOut;
    private readonly ILogger<DefaultAuditRecorder> _logger;

    public DefaultAuditRecorder(
        AuditEnvelopeValidator validator,
        IAuditSanitizer sanitizer,
        IAuditIntegrityHasher hasher,
        AccountabilityCanonicalProjectionWriter projectionWriter,
        IEnumerable<IAuditSink> sinks,
        AccountabilityOptions options,
        TimeProvider? timeProvider = null,
        ILogger<DefaultAuditRecorder>? logger = null,
        IAuditEnvelopePreparer? preparer = null)
    {
        _preparer = preparer ?? new DefaultAuditEnvelopePreparer(validator, sanitizer, hasher, projectionWriter);
        _sinks = sinks.OrderBy(x => x.Id, StringComparer.Ordinal).ToImmutableArray();
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _fanOut = new AuditSinkFanOut(_sinks, _options, _timeProvider);
        _logger = logger ?? NullLogger<DefaultAuditRecorder>.Instance;
    }

    public async ValueTask<AuditRecordResult> RecordAsync(AuditEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var prepared = await _preparer.PrepareAsync(envelope, cancellationToken).ConfigureAwait(false);
            if (!prepared.IsAccepted || prepared.Envelope is null)
                return Rejected(envelope.AuditId, prepared.Issues);
            return await FanOutAsync(prepared.Envelope, prepared.Envelope.Integrity!, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AuditSanitizationException ex)
        {
            return Rejected(envelope.AuditId, [new(ex.Code, ex.Path)]);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Accountability recorder failed internally for {AuditId}", envelope.AuditId);
            return new AuditRecordResult
            {
                AuditId = envelope.AuditId,
                Status = AuditRecordStatus.Failed,
                ProcessedAt = _timeProvider.GetUtcNow(),
                Issues = [new("AUDIT_RECORDER_INTERNAL_FAILURE")]
            };
        }
    }

    private ValueTask<AuditRecordResult> FanOutAsync(
        AuditEnvelope envelope,
        CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHash hash,
        CancellationToken callerCancellation)
        => _fanOut.WriteAsync(envelope, callerCancellation);

    private AuditRecordResult Rejected(string? auditId, ImmutableArray<AuditRecordIssue> issues)
        => new()
        {
            AuditId = auditId ?? string.Empty,
            Status = AuditRecordStatus.Rejected,
            ProcessedAt = _timeProvider.GetUtcNow(),
            Issues = issues
        };

    private static AuditEnvelope Snapshot(AuditEnvelope value)
    {
        var payload = value.Payload is { } p ? p with { Data = p.Data.Clone() } : null;
        var data = value.DataSnapshot is { } snapshot
            ? snapshot with
            {
                Artifacts = snapshot.Artifacts.Select(x => x is null
                    ? null!
                    : x with { SanitizedValue = x.SanitizedValue is { } element ? element.Clone() : null }).ToImmutableArray()
            }
            : null;
        var tags = value.Tags.OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToImmutableSortedDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        return value with { Payload = payload, DataSnapshot = data, Tags = tags };
    }

    private sealed record WriteAttempt(string SinkId, AuditSinkWriteResult? Result, string FailureCode)
    {
        public static WriteAttempt Success(string id, AuditSinkWriteResult result) => new(id, result, string.Empty);
        public static WriteAttempt Failure(string id, Exception exception) => new(id, null, exception is TimeoutException or OperationCanceledException ? "AUDIT_SINK_TIMEOUT" : "AUDIT_SINK_FAILURE");
    }
}
