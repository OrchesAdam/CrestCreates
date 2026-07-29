using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Hashing;
using CrestCreates.Accountability.Abstractions.Sanitization;
using CrestCreates.Accountability.Abstractions.Sinks;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Accountability.CanonicalHashing;
using CrestCreates.Accountability.InMemory;
using CrestCreates.Accountability.Recording;
using CrestCreates.Accountability.Sanitization;
using CrestCreates.Accountability.Validation;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Accountability.Tests.Recording;

public sealed class DefaultAuditRecorderTests
{
    [Fact]
    public async Task RecordsAcceptedSinkWithStructuredHash()
    {
        var sink = new RecordingSink("a");
        var recorder = CreateRecorder([sink]);

        var result = await recorder.RecordAsync(CreateEnvelope());

        result.Status.Should().Be(AuditRecordStatus.Recorded);
        result.RecordHash.Should().NotBeNull();
        result.RecordHash!.Algorithm.Should().Be("SHA-256");
        result.SinkResults.Should().ContainSingle(x => x.Status == AuditSinkWriteStatus.Accepted);
        sink.Calls.Should().Be(1);
    }

    [Fact]
    public async Task ConflictIsPreservedAndIsNotProviderFailure()
    {
        var sink = new InMemoryAuditSink("a");
        var recorder = CreateRecorder([sink]);
        var first = await recorder.RecordAsync(CreateEnvelope());

        var conflicting = CreateEnvelope() with { Outcome = new AuditOutcome { Status = "failed", Code = "different" } };
        var second = await CreateRecorder([sink], hasher: new DifferentHasher()).RecordAsync(conflicting);

        first.IsAccepted.Should().BeTrue();
        second.SinkResults.Should().ContainSingle(x => x.Status == AuditSinkWriteStatus.Conflict);
        second.SinkFailures.Should().BeEmpty();
    }

    [Fact]
    public async Task RejectedSanitizerRewriteCallsNoSink()
    {
        var sink = new RecordingSink("a");
        var sanitizer = new RewritingSanitizer();
        var recorder = CreateRecorder([sink], sanitizer);

        var result = await recorder.RecordAsync(CreateEnvelope());

        result.Status.Should().Be(AuditRecordStatus.Rejected);
        result.Issues.Should().Contain(x => x.Code == "AUDIT_SANITIZER_REWROTE_PROTECTED_FACT");
        sink.Calls.Should().Be(0);
    }

    [Fact]
    public async Task SanitizerCannotRewriteTargetVersion()
    {
        var sink = new RecordingSink("a");
        var recorder = CreateRecorder([sink], new TargetVersionRewritingSanitizer());

        var result = await recorder.RecordAsync(CreateEnvelope() with
        {
            Target = new AuditTarget { Kind = "route", Id = "items", Version = "1" }
        });

        result.Status.Should().Be(AuditRecordStatus.Rejected);
        result.Issues.Should().Contain(x => x.Code == "AUDIT_SANITIZER_REWROTE_PROTECTED_FACT");
        sink.Calls.Should().Be(0);
    }

    [Fact]
    public async Task SanitizerCannotPrepopulateRecorderOwnedFields()
    {
        var sink = new RecordingSink("a");
        var recorder = CreateRecorder([sink], new StampedOutputSanitizer());

        var result = await recorder.RecordAsync(CreateEnvelope());

        result.Status.Should().Be(AuditRecordStatus.Rejected);
        result.Issues.Should().Contain(x => x.Code == "AUDIT_SANITIZED_OUTPUT_INVALID");
        sink.Calls.Should().Be(0);
    }

    [Fact]
    public void CanonicalJsonObjectPropertiesUseOrdinalOrder()
    {
        using var firstJson = JsonDocument.Parse("{\"z\":1,\"a\":{\"y\":2,\"b\":3}}");
        using var secondJson = JsonDocument.Parse("{\"a\":{\"b\":3,\"y\":2},\"z\":1}");
        var first = CreateEnvelope() with
        {
            Payload = new AuditPayload { Kind = "test.payload", Version = 1, Data = firstJson.RootElement.Clone() }
        };
        var second = first with
        {
            Payload = first.Payload! with { Data = secondJson.RootElement.Clone() }
        };
        var writer = new AccountabilityCanonicalProjectionWriter();

        Project(writer, first).Should().Be(Project(writer, second));
    }

    [Fact]
    public async Task CallerCancellationThrowsOperationCanceledException()
    {
        var recorder = CreateRecorder([]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => recorder.RecordAsync(CreateEnvelope(), cancellation.Token).AsTask();

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AllSinksAreAttemptedWithinOneTotalBudget()
    {
        var started = new ConcurrentBag<string>();
        var hanging = new RecordingSink("a", started, hang: true);
        var fast = new RecordingSink("b", started);
        var recorder = CreateRecorder([hanging, fast], options: new AccountabilityOptions { WriteTimeout = TimeSpan.FromMilliseconds(50) });

        var result = await recorder.RecordAsync(CreateEnvelope());

        started.Should().BeEquivalentTo("a", "b");
        result.SinkFailures.Should().Contain(x => x.SinkId == "a" && x.Code == "AUDIT_SINK_TIMEOUT");
        result.SinkResults.Should().ContainSingle(x => x.SinkId == "b");
    }

    [Fact]
    public async Task SynchronousSinkThrowDoesNotPreventLaterSinkStart()
    {
        var started = new ConcurrentBag<string>();
        var throwing = new RecordingSink("a", started, throwSynchronously: true);
        var fast = new RecordingSink("b", started);
        var recorder = CreateRecorder([fast, throwing]);

        var result = await recorder.RecordAsync(CreateEnvelope());

        started.Should().BeEquivalentTo("a", "b");
        result.SinkFailures.Should().ContainSingle(x => x.SinkId == "a" && x.Code == "AUDIT_SINK_FAILURE");
        result.SinkResults.Should().ContainSingle(x => x.SinkId == "b");
        result.SinkFailures.Select(x => x.SinkId)
            .Concat(result.SinkResults.Select(x => x.SinkId))
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task InternalHasherFailureReturnsStableFailedResult()
    {
        var recorder = CreateRecorder([], hasher: new ThrowingHasher());

        var result = await recorder.RecordAsync(CreateEnvelope());

        result.Status.Should().Be(AuditRecordStatus.Failed);
        result.Issues.Should().ContainSingle(x => x.Code == "AUDIT_RECORDER_INTERNAL_FAILURE");
    }

    private static DefaultAuditRecorder CreateRecorder(
        IEnumerable<IAuditSink> sinks,
        IAuditSanitizer? sanitizer = null,
        AccountabilityOptions? options = null,
        IAuditIntegrityHasher? hasher = null)
    {
        var writer = new AccountabilityCanonicalProjectionWriter();
        return new DefaultAuditRecorder(
            new AuditEnvelopeValidator(),
            sanitizer ?? new PassThroughSanitizer(),
            hasher ?? new FixedHasher(),
            writer,
            sinks,
            options ?? new AccountabilityOptions(),
            TimeProvider.System);
    }

    private static AuditEnvelope CreateEnvelope()
        => new()
        {
            AuditId = "audit-1",
            OccurredAt = DateTimeOffset.Parse("2026-07-29T00:00:00Z"),
            CorrelationId = "corr-1",
            Actor = new AuditActor { Kind = "user", Id = "user-1" },
            Action = new AuditAction { Kind = "http.request", Name = "GET /items" },
            Target = new AuditTarget { Kind = "route", Id = "items" },
            Outcome = new AuditOutcome { Status = "succeeded" },
            Runtime = AuditRuntimeContext.Empty,
            Descriptors = AuditDescriptorContext.Empty,
            Evidence = [],
            Tags = AuditTagMap.Empty
        };

    private static string Project(AccountabilityCanonicalProjectionWriter projection, AuditEnvelope envelope)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        projection.Write(envelope, writer);
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private class FixedHasher : IAuditIntegrityHasher
    {
        public virtual CanonicalHash Compute(AuditEnvelope sanitizedCanonicalEnvelope)
            => new()
            {
                Value = "aabb",
                Algorithm = "SHA-256",
                AlgorithmVersion = "sha256-canonical-json-v1",
                ArtifactKind = "AccountabilityRecord",
                Scope = "InternalFull",
                Purpose = "AuditEvidence",
                ContractVersion = "canonical-hash-v1",
                CanonicalShapeVersion = "accountability-record-hash-v1"
            };
    }

    private sealed class DifferentHasher : FixedHasher
    {
        public override CanonicalHash Compute(AuditEnvelope sanitizedCanonicalEnvelope)
            => base.Compute(sanitizedCanonicalEnvelope) with { Value = "ccdd" };
    }

    private sealed class ThrowingHasher : FixedHasher
    {
        public override CanonicalHash Compute(AuditEnvelope sanitizedCanonicalEnvelope)
            => throw new InvalidOperationException("hash failed");
    }

    private sealed class PassThroughSanitizer : IAuditSanitizer
    {
        public ValueTask<AuditSanitizationResult> SanitizeAsync(AuditEnvelope candidate, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AuditSanitizationResult
            {
                Envelope = candidate,
                Stamp = new AuditSanitizationStamp { PolicyId = "test", PolicyVersion = 1, AppliedRuleIds = [] }
            });
    }

    private sealed class RewritingSanitizer : IAuditSanitizer
    {
        public ValueTask<AuditSanitizationResult> SanitizeAsync(AuditEnvelope candidate, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AuditSanitizationResult
            {
                Envelope = candidate with { Actor = candidate.Actor with { Id = "system" } },
                Stamp = new AuditSanitizationStamp { PolicyId = "test", PolicyVersion = 1, AppliedRuleIds = [] }
            });
    }

    private sealed class TargetVersionRewritingSanitizer : IAuditSanitizer
    {
        public ValueTask<AuditSanitizationResult> SanitizeAsync(AuditEnvelope candidate, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AuditSanitizationResult
            {
                Envelope = candidate with { Target = candidate.Target with { Version = "2" } },
                Stamp = new AuditSanitizationStamp { PolicyId = "test", PolicyVersion = 1, AppliedRuleIds = [] }
            });
    }

    private sealed class StampedOutputSanitizer : IAuditSanitizer
    {
        public ValueTask<AuditSanitizationResult> SanitizeAsync(AuditEnvelope candidate, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AuditSanitizationResult
            {
                Envelope = candidate with
                {
                    Sanitization = new AuditSanitizationStamp { PolicyId = "illegal", PolicyVersion = 1, AppliedRuleIds = [] }
                },
                Stamp = new AuditSanitizationStamp { PolicyId = "test", PolicyVersion = 1, AppliedRuleIds = [] }
            });
    }

    private sealed class RecordingSink : IAuditSink
    {
        private readonly ConcurrentBag<string>? _started;
        private readonly bool _hang;
        private readonly bool _throwSynchronously;
        public RecordingSink(
            string id,
            ConcurrentBag<string>? started = null,
            bool hang = false,
            bool throwSynchronously = false)
        {
            Id = id;
            _started = started;
            _hang = hang;
            _throwSynchronously = throwSynchronously;
        }
        public string Id { get; }
        public int Calls { get; private set; }
        public ValueTask<AuditSinkWriteResult> WriteAsync(AuditEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Calls++;
            _started?.Add(Id);
            if (_throwSynchronously) throw new InvalidOperationException("sync failure");
            if (_hang) return new ValueTask<AuditSinkWriteResult>(Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith<AuditSinkWriteResult>(_ => throw new OperationCanceledException(), TaskScheduler.Default));
            return ValueTask.FromResult(new AuditSinkWriteResult { SinkId = Id, AuditId = envelope.AuditId, Integrity = envelope.Integrity!, Status = AuditSinkWriteStatus.Accepted });
        }
    }
}
