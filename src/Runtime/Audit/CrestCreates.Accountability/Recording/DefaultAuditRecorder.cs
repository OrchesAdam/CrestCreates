using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Hashing;
using CrestCreates.Accountability.Abstractions.Recording;
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
    private readonly AuditEnvelopeValidator _validator;
    private readonly IAuditSanitizer _sanitizer;
    private readonly IAuditIntegrityHasher _hasher;
    private readonly AccountabilityCanonicalProjectionWriter _projectionWriter;
    private readonly ImmutableArray<IAuditSink> _sinks;
    private readonly AccountabilityOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DefaultAuditRecorder> _logger;

    public DefaultAuditRecorder(
        AuditEnvelopeValidator validator,
        IAuditSanitizer sanitizer,
        IAuditIntegrityHasher hasher,
        AccountabilityCanonicalProjectionWriter projectionWriter,
        IEnumerable<IAuditSink> sinks,
        AccountabilityOptions options,
        TimeProvider? timeProvider = null,
        ILogger<DefaultAuditRecorder>? logger = null)
    {
        _validator = validator;
        _sanitizer = sanitizer;
        _hasher = hasher;
        _projectionWriter = projectionWriter;
        _sinks = sinks.OrderBy(x => x.Id, StringComparer.Ordinal).ToImmutableArray();
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<DefaultAuditRecorder>.Instance;
    }

    public async ValueTask<AuditRecordResult> RecordAsync(AuditEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        AuditEnvelope candidate;
        try
        {
            var structuralValidation = _validator.ValidateStructure(envelope);
            if (!structuralValidation.IsValid)
                return Rejected(envelope.AuditId, structuralValidation.Issues);

            var candidateValidation = _validator.ValidateCandidate(envelope);
            if (!candidateValidation.IsValid)
                return Rejected(envelope.AuditId, candidateValidation.Issues);

            candidate = Snapshot(envelope);
            if (candidate.Sanitization is not null)
                return Rejected(candidate.AuditId, [new("AUDIT_SANITIZATION_STAMP_SUPPLIED", "Sanitization")]);
            if (candidate.Integrity is not null)
                return Rejected(candidate.AuditId, [new("AUDIT_INVALID_HASH_METADATA", "Integrity")]);

            if (_projectionWriter.MeasureBytes(candidate) > AuditContractLimits.MaxCandidateEnvelopeBytes)
                return Rejected(candidate.AuditId, [new("AUDIT_LIMIT_EXCEEDED", "CandidateEnvelope")]);

            var sanitizedResult = await _sanitizer.SanitizeAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (sanitizedResult?.Envelope is null)
                return Rejected(candidate.AuditId, [new("AUDIT_SANITIZED_OUTPUT_INVALID")]);
            var sanitizedStructuralValidation = _validator.ValidateStructure(sanitizedResult.Envelope);
            if (!sanitizedStructuralValidation.IsValid)
                return Rejected(candidate.AuditId, sanitizedStructuralValidation.Issues);

            AuditEnvelope sanitized;
            try
            {
                var sanitizerOutputValidation = _validator.ValidateSafeSnapshot(sanitizedResult.Envelope);
                if (!sanitizerOutputValidation.IsValid)
                    return Rejected(candidate.AuditId, sanitizerOutputValidation.Issues);
                sanitized = Snapshot(sanitizedResult.Envelope);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Accountability sanitizer returned an unreadable output for {AuditId}", candidate.AuditId);
                return Rejected(candidate.AuditId, [new("AUDIT_SANITIZED_OUTPUT_INVALID")]);
            }
            if (sanitized.Sanitization is not null || sanitized.Integrity is not null)
                return Rejected(candidate.AuditId, [new("AUDIT_SANITIZED_OUTPUT_INVALID", sanitized.Sanitization is not null ? "Sanitization" : "Integrity")]);
            if (!AuditProtectedFactComparer.AreEqual(candidate, sanitized))
                return Rejected(candidate.AuditId, [new("AUDIT_SANITIZER_REWROTE_PROTECTED_FACT")]);
            if (!AuditSanitizerOutputComparer.IsAllowed(candidate, sanitized))
                return Rejected(candidate.AuditId, [new("AUDIT_SANITIZER_REWROTE_PROTECTED_FACT")]);
            if (sanitizedResult.Stamp is null
                || string.IsNullOrWhiteSpace(sanitizedResult.Stamp.PolicyId)
                || sanitizedResult.Stamp.PolicyVersion <= 0
                || sanitizedResult.Stamp.AppliedRuleIds.IsDefault)
                return Rejected(candidate.AuditId, [new("AUDIT_SANITIZATION_STAMP_INVALID", "Sanitization")]);

            sanitized = sanitized with { Sanitization = null, Integrity = null };

            var safeValidation = _validator.ValidateSafeSnapshot(sanitized);
            if (!safeValidation.IsValid)
                return Rejected(candidate.AuditId, safeValidation.Issues);

            sanitized = sanitized with { Sanitization = sanitizedResult.Stamp };
            var stampedValidation = _validator.ValidateSafeSnapshot(sanitized);
            if (!stampedValidation.IsValid)
                return Rejected(candidate.AuditId, stampedValidation.Issues);
            if (_projectionWriter.MeasureBytes(sanitized) > AuditContractLimits.MaxSafeEnvelopeBytes)
                return Rejected(candidate.AuditId, [new("AUDIT_MAX_SAFE_ENVELOPE_BYTES_EXCEEDED")]);

            var hash = _hasher.Compute(sanitized);
            sanitized = sanitized with { Integrity = hash };
            return await FanOutAsync(sanitized, hash, cancellationToken).ConfigureAwait(false);
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

    private async ValueTask<AuditRecordResult> FanOutAsync(
        AuditEnvelope envelope,
        CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHash hash,
        CancellationToken callerCancellation)
    {
        if (_sinks.IsDefaultOrEmpty)
        {
            return new AuditRecordResult
            {
                AuditId = envelope.AuditId,
                Status = AuditRecordStatus.NoSinkConfigured,
                ProcessedAt = _timeProvider.GetUtcNow(),
                RecordHash = hash,
                Issues = _options.RequireAtLeastOneSink ? [new("AUDIT_NO_SINK_CONFIGURED")] : []
            };
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(callerCancellation);
        timeout.CancelAfter(_options.WriteTimeout);
        var attempts = new List<Task<WriteAttempt>>(_sinks.Length);
        foreach (var sink in _sinks)
        {
            try
            {
                var valueTask = sink.WriteAsync(envelope, timeout.Token);
                attempts.Add(CaptureAsync(sink, valueTask));
            }
            catch (Exception ex)
            {
                attempts.Add(Task.FromResult(WriteAttempt.Failure(sink.Id, ex)));
            }
        }

        var all = Task.WhenAll(attempts);
        try
        {
            await all.WaitAsync(_options.WriteTimeout, callerCancellation).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            timeout.Cancel();
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        callerCancellation.ThrowIfCancellationRequested();

        var results = ImmutableArray.CreateBuilder<AuditSinkWriteResult>();
        var failures = ImmutableArray.CreateBuilder<AuditSinkFailure>();
        for (var index = 0; index < attempts.Count; index++)
        {
            var sinkId = _sinks[index].Id;
            var attempt = attempts[index].IsCompletedSuccessfully
                ? await attempts[index].ConfigureAwait(false)
                : WriteAttempt.Failure(sinkId, new TimeoutException());
            if (attempt.Result is { } result)
            {
                if (!string.Equals(result.SinkId, attempt.SinkId, StringComparison.Ordinal)
                    || !string.Equals(result.AuditId, envelope.AuditId, StringComparison.Ordinal)
                    || result.Integrity != hash)
                {
                    failures.Add(new(attempt.SinkId, "AUDIT_SINK_RESULT_IDENTITY_MISMATCH"));
                }
                else if (result.Status == AuditSinkWriteStatus.Conflict
                    ? result.ExistingIntegrity is null || result.ExistingIntegrity == hash
                    : result.Status is AuditSinkWriteStatus.Accepted or AuditSinkWriteStatus.Duplicate
                        ? result.ExistingIntegrity is not null
                        : true)
                {
                    failures.Add(new(attempt.SinkId, "AUDIT_SINK_RESULT_CONTRACT_MISMATCH"));
                }
                else results.Add(result);
            }
            else failures.Add(new(attempt.SinkId, attempt.FailureCode));
        }

        var accepted = results.Any(x => x.Status is AuditSinkWriteStatus.Accepted or AuditSinkWriteStatus.Duplicate);
        var conflicts = results.Any(x => x.Status == AuditSinkWriteStatus.Conflict);
        var status = accepted
            ? (failures.Count == 0 && !conflicts ? AuditRecordStatus.Recorded : AuditRecordStatus.PartiallyRecorded)
            : conflicts ? AuditRecordStatus.Failed : AuditRecordStatus.Failed;

        return new AuditRecordResult
        {
            AuditId = envelope.AuditId,
            Status = status,
            ProcessedAt = _timeProvider.GetUtcNow(),
            RecordHash = hash,
            SinkResults = results.ToImmutable(),
            SinkFailures = failures.ToImmutable()
        };
    }

    private static async Task<WriteAttempt> CaptureAsync(IAuditSink sink, ValueTask<AuditSinkWriteResult> operation)
    {
        try { return WriteAttempt.Success(sink.Id, await operation.ConfigureAwait(false)); }
        catch (Exception ex) { return WriteAttempt.Failure(sink.Id, ex); }
    }

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
