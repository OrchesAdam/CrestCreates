using System.Collections.Concurrent;
using System.Collections.Immutable;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Accountability.InMemory;

public sealed class InMemoryAuditSink : IAuditSink
{
    private readonly ConcurrentDictionary<string, StoredRecord> _records = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public InMemoryAuditSink(string id = "in-memory", TimeProvider? timeProvider = null)
    {
        Id = id;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string Id { get; }

    public ValueTask<AuditSinkWriteResult> WriteAsync(AuditEnvelope envelope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _timeProvider.GetUtcNow();
        while (true)
        {
            if (_records.TryGetValue(envelope.AuditId, out var existing))
            {
                var status = existing.Integrity == envelope.Integrity
                    ? AuditSinkWriteStatus.Duplicate
                    : AuditSinkWriteStatus.Conflict;
                return ValueTask.FromResult(new AuditSinkWriteResult
                {
                    SinkId = Id,
                    AuditId = envelope.AuditId,
                    Integrity = envelope.Integrity!,
                    ExistingIntegrity = status == AuditSinkWriteStatus.Conflict
                        ? existing.Integrity
                        : null,
                    Status = status,
                    FirstAcceptedAt = existing.FirstAcceptedAt
                });
            }

            var candidate = new StoredRecord(Snapshot(envelope), envelope.Integrity!, now);
            if (_records.TryAdd(envelope.AuditId, candidate))
            {
                return ValueTask.FromResult(new AuditSinkWriteResult
                {
                    SinkId = Id,
                    AuditId = envelope.AuditId,
                    Integrity = envelope.Integrity!,
                    Status = AuditSinkWriteStatus.Accepted,
                    FirstAcceptedAt = now
                });
            }
        }
    }

    public bool TryGet(string auditId, out AuditEnvelope? envelope)
    {
        if (_records.TryGetValue(auditId, out var stored))
        {
            envelope = Snapshot(stored.Envelope);
            return true;
        }
        envelope = null;
        return false;
    }

    public IReadOnlyList<AuditEnvelope> GetRecords()
        => _records.Values
            .Select(record => Snapshot(record.Envelope))
            .OrderBy(record => record.AuditId, StringComparer.Ordinal)
            .ToArray();

    private static AuditEnvelope Snapshot(AuditEnvelope value)
    {
        var payload = value.Payload is { } p ? p with { Data = p.Data.Clone() } : null;
        var data = value.DataSnapshot is { } snapshot
            ? snapshot with
            {
                Artifacts = snapshot.Artifacts.Select(x => x with
                {
                    SanitizedValue = x.SanitizedValue is { } element ? element.Clone() : null
                }).ToImmutableArray()
            }
            : null;
        var tags = value.Tags is null
            ? AuditTagMap.Empty
            : value.Tags.OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToImmutableSortedDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        return value with { Payload = payload, DataSnapshot = data, Tags = tags };
    }

    private sealed record StoredRecord(AuditEnvelope Envelope, CanonicalHash Integrity, DateTimeOffset FirstAcceptedAt);
}
