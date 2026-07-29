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
    public Task ValidatesBeforeSanitizing()
        => MalformedCandidateCallsNoSanitizerOrSink();

    [Fact]
    public async Task SanitizesBeforeAnySink()
    {
        var order = new List<string>();
        var recorder = CreateRecorder([new OrderedSink("sink", order)], new OrderedSanitizer(order));

        await recorder.RecordAsync(CreateEnvelope());

        order.Should().Equal("sanitize", "sink");
    }

    [Fact]
    public Task ValidatesSanitizedSnapshot()
        => SanitizerCannotPrepopulateRecorderOwnedFields();

    [Fact]
    public Task ComputesHashWithCanonicalHashRuntime()
        => RecordsAcceptedSinkWithStructuredHash();

    [Fact]
    public void ExcludesIntegrityAndAttemptMetadataFromHash()
    {
        var writer = new AccountabilityCanonicalProjectionWriter();
        var envelope = CreateEnvelope();
        var withIntegrity = envelope with { Integrity = new FixedHasher().Compute(envelope) };

        Project(writer, withIntegrity).Should().Be(Project(writer, envelope));
        typeof(AuditEnvelope).GetProperty("ProcessedAt").Should().BeNull();
        typeof(AuditEnvelope).GetProperty("RecordedAt").Should().BeNull();
    }

    [Fact]
    public async Task IncludesSanitizationStampInHash()
    {
        var hasher = new CapturingHasher();
        await CreateRecorder([new RecordingSink("a")], hasher: hasher).RecordAsync(CreateEnvelope());

        hasher.Envelope.Should().NotBeNull();
        hasher.Envelope!.Sanitization.Should().NotBeNull();
        hasher.Envelope.Sanitization!.PolicyId.Should().Be("test");
    }

    [Fact]
    public Task OrdersSinksByStableIdAndAttemptsAll()
        => AllSinksAreAttemptedWithinOneTotalBudget();

    [Fact]
    public async Task AggregatesRecordedPartialFailedNoSinkRejected()
    {
        var recorded = await CreateRecorder([new RecordingSink("a")]).RecordAsync(CreateEnvelope());
        var partial = await CreateRecorder([new RecordingSink("a"), new RecordingSink("b", throwSynchronously: true)])
            .RecordAsync(CreateEnvelope());
        var failed = await CreateRecorder([new RecordingSink("a", throwSynchronously: true)]).RecordAsync(CreateEnvelope());
        var noSink = await CreateRecorder([]).RecordAsync(CreateEnvelope());
        var rejected = await CreateRecorder([]).RecordAsync(CreateEnvelope() with { Evidence = default });

        recorded.Status.Should().Be(AuditRecordStatus.Recorded);
        partial.Status.Should().Be(AuditRecordStatus.PartiallyRecorded);
        failed.Status.Should().Be(AuditRecordStatus.Failed);
        noSink.Status.Should().Be(AuditRecordStatus.NoSinkConfigured);
        rejected.Status.Should().Be(AuditRecordStatus.Rejected);
    }

    [Fact]
    public Task ConflictIsPreservedInRecordResult()
        => ConflictIsPreservedAndIsNotProviderFailure();

    [Fact]
    public Task ConflictIsNotReportedAsProviderFailure()
        => ConflictIsPreservedAndIsNotProviderFailure();

    [Fact]
    public async Task RejectedResultContainsStableIssueCodes()
    {
        var result = await CreateRecorder([]).RecordAsync(CreateEnvelope() with { Tags = null! });

        result.Status.Should().Be(AuditRecordStatus.Rejected);
        result.Issues.Should().ContainSingle(x =>
            x.Code == "AUDIT_NULL_IMMUTABLE_DICTIONARY" && x.Path == "Tags");
    }

    [Fact]
    public async Task AcceptedSinkIdsAreDerivedNotDuplicated()
    {
        var result = await CreateRecorder([new RecordingSink("accepted")]).RecordAsync(CreateEnvelope());

        result.SinkResults.Where(x => x.Status is AuditSinkWriteStatus.Accepted or AuditSinkWriteStatus.Duplicate)
            .Select(x => x.SinkId).Should().Equal("accepted");
        typeof(AuditRecordResult).GetProperty("AcceptedSinkIds").Should().BeNull();
    }

    [Fact]
    public void MultiSinkHasNoFalseGlobalRecordedTime()
    {
        typeof(AuditEnvelope).GetProperty("RecordedAt").Should().BeNull();
        typeof(AuditSinkWriteResult).GetProperty("FirstAcceptedAt").Should().NotBeNull();
    }

    [Fact]
    public Task ProcessedAtIsAttemptMetadataNotFactMetadata()
        => ProcessedAtIsAfterSinkAggregationCompletes();

    [Fact]
    public Task SinkCannotReturnDifferentSinkId()
        => AssertSinkIdentityMismatchAsync(result => result with { SinkId = "different" });

    [Fact]
    public Task SinkCannotReturnDifferentAuditId()
        => AssertSinkIdentityMismatchAsync(result => result with { AuditId = "different" });

    [Fact]
    public Task SinkCannotReturnDifferentIntegrity()
        => AssertSinkIdentityMismatchAsync(result => result with
        {
            Integrity = result.Integrity with { Value = "different" }
        });

    [Fact]
    public Task SanitizerCannotRewriteProtectedFactFields()
        => SanitizerCannotRewriteTargetVersion();

    [Fact]
    public async Task SanitizerMayMinimizePresentationFields()
    {
        var sink = new CapturingSink("capture");
        var envelope = CreateEnvelope() with
        {
            Actor = new AuditActor { Kind = "user", Id = "user-1", DisplayName = "Sensitive name" },
            Outcome = new AuditOutcome { Status = "succeeded", SafeSummary = "presentation" },
            Tags = AuditTagMap.Empty.Add("presentation", "remove")
        };

        var result = await CreateRecorder([sink], new MinimizingSanitizer()).RecordAsync(envelope);

        result.IsAccepted.Should().BeTrue();
        sink.Envelope!.Actor.DisplayName.Should().BeNull();
        sink.Envelope.Outcome.SafeSummary.Should().BeNull();
        sink.Envelope.Tags.Should().BeEmpty();
    }

    [Fact]
    public Task SanitizerRewriteRejectionCallsNoSink()
        => RejectedSanitizerRewriteCallsNoSink();

    [Fact]
    public async Task DoesNotMutateProducerCandidate()
    {
        var candidate = CreateEnvelope() with
        {
            Actor = new AuditActor { Kind = "user", Id = "user-1", DisplayName = "Original" },
            Tags = AuditTagMap.Empty.Add("keep", "producer")
        };

        await CreateRecorder([new RecordingSink("a")], new MinimizingSanitizer()).RecordAsync(candidate);

        candidate.Actor.DisplayName.Should().Be("Original");
        candidate.Tags.Should().ContainKey("keep");
        candidate.Sanitization.Should().BeNull();
        candidate.Integrity.Should().BeNull();
    }

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
    public async Task IdenticalReplayThroughRecorderIsAcceptedDuplicate()
    {
        var sink = new InMemoryAuditSink("a");
        var recorder = CreateRecorder([sink]);

        var first = await recorder.RecordAsync(CreateEnvelope());
        var replay = await recorder.RecordAsync(CreateEnvelope());

        first.IsAccepted.Should().BeTrue();
        replay.Status.Should().Be(AuditRecordStatus.Recorded);
        replay.IsAccepted.Should().BeTrue();
        replay.SinkResults.Should().ContainSingle(x =>
            x.Status == AuditSinkWriteStatus.Duplicate
            && x.ExistingIntegrity == null);
    }

    [Fact]
    public Task NullTagsIsRejected()
        => AssertMalformedRejectedAsync(CreateEnvelope() with { Tags = null! }, "Tags");

    [Fact]
    public Task DefaultRuntimeReferencesIsRejected()
        => AssertMalformedRejectedAsync(
            CreateEnvelope() with { Runtime = AuditRuntimeContext.Empty with { References = default } },
            "Runtime.References");

    [Fact]
    public Task DefaultDescriptorItemsIsRejected()
        => AssertMalformedRejectedAsync(
            CreateEnvelope() with { Descriptors = AuditDescriptorContext.Empty with { Items = default } },
            "Descriptors.Items");

    [Fact]
    public Task DefaultEvidenceIsRejected()
        => AssertMalformedRejectedAsync(CreateEnvelope() with { Evidence = default }, "Evidence");

    [Fact]
    public Task DefaultArtifactArrayIsRejected()
        => AssertMalformedRejectedAsync(
            CreateEnvelope() with
            {
                DataSnapshot = new AuditDataSnapshot
                {
                    CapturePolicyId = "test",
                    CapturePolicyVersion = 1,
                    Artifacts = default
                }
            },
            "DataSnapshot.Artifacts");

    [Fact]
    public async Task MalformedCandidateNeverBecomesRecorderInternalFailure()
    {
        var result = await CreateRecorder([]).RecordAsync(CreateEnvelope() with { Evidence = default });
        result.Status.Should().Be(AuditRecordStatus.Rejected);
        result.Issues.Should().NotContain(x => x.Code == "AUDIT_RECORDER_INTERNAL_FAILURE");
    }

    [Fact]
    public async Task MalformedCandidateCallsNoSanitizerOrSink()
    {
        var sanitizer = new CountingSanitizer();
        var sink = new RecordingSink("a");
        var result = await CreateRecorder([sink], sanitizer).RecordAsync(
            CreateEnvelope() with { Runtime = AuditRuntimeContext.Empty with { References = default } });

        result.Status.Should().Be(AuditRecordStatus.Rejected);
        sanitizer.Calls.Should().Be(0);
        sink.Calls.Should().Be(0);
    }

    [Fact]
    public Task SanitizerNullActorIsRejectedNotInternalFailure()
        => AssertInvalidSanitizerOutputAsync(candidate => candidate with { Actor = null! });

    [Fact]
    public Task SanitizerNullActionIsRejectedNotInternalFailure()
        => AssertInvalidSanitizerOutputAsync(candidate => candidate with { Action = null! });

    [Fact]
    public Task SanitizerNullTargetIsRejectedNotInternalFailure()
        => AssertInvalidSanitizerOutputAsync(candidate => candidate with { Target = null! });

    [Fact]
    public Task SanitizerNullOutcomeIsRejectedNotInternalFailure()
        => AssertInvalidSanitizerOutputAsync(candidate => candidate with { Outcome = null! });

    [Fact]
    public Task SanitizerNullArtifactIsRejectedNotInternalFailure()
        => AssertInvalidSanitizerOutputAsync(candidate => candidate with
        {
            DataSnapshot = new AuditDataSnapshot
            {
                CapturePolicyId = "test",
                CapturePolicyVersion = 1,
                Artifacts = [null!]
            }
        });

    [Fact]
    public Task InvalidSanitizerOutputCallsNoSink()
        => SanitizerNullActorIsRejectedNotInternalFailure();

    [Fact]
    public Task InvalidSanitizerOutputNeverReturnsRecorderInternalFailure()
        => SanitizerNullArtifactIsRejectedNotInternalFailure();

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
    public async Task MidFanOutCallerCancellationCancelsAttemptAfterStartingAllSinks()
    {
        var started = new ConcurrentBag<string>();
        var recorder = CreateRecorder(
            [new RecordingSink("a", started, hang: true), new RecordingSink("b", started)],
            options: new AccountabilityOptions { WriteTimeout = TimeSpan.FromSeconds(5) });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var action = () => recorder.RecordAsync(CreateEnvelope(), cancellation.Token).AsTask();

        await action.Should().ThrowAsync<OperationCanceledException>();
        started.Should().BeEquivalentTo("a", "b");
    }

    [Fact]
    public async Task CallerCancellationWinsWhenAllSinksCompleteAsCancelled()
    {
        var sinkA = new CancellationControlledSink("a");
        var sinkB = new CancellationControlledSink("b");
        var recorder = CreateRecorder(
            [sinkB, sinkA],
            options: new AccountabilityOptions { WriteTimeout = TimeSpan.FromSeconds(5) });
        using var cancellation = new CancellationTokenSource();
        var attempt = recorder.RecordAsync(CreateEnvelope(), cancellation.Token).AsTask();
        await Task.WhenAll(sinkA.Started, sinkB.Started);

        cancellation.Cancel();

        var action = async () => await attempt;
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RecorderTimeoutDoesNotMasqueradeAsCallerCancellation()
    {
        var recorder = CreateRecorder(
            [new RecordingSink("a", hang: true)],
            options: new AccountabilityOptions { WriteTimeout = TimeSpan.FromMilliseconds(30) });

        var result = await recorder.RecordAsync(CreateEnvelope(), CancellationToken.None);

        result.Status.Should().Be(AuditRecordStatus.Failed);
        result.SinkFailures.Should().ContainSingle(x => x.Code == "AUDIT_SINK_TIMEOUT");
    }

    [Fact]
    public Task CallerCancellationIsNeverAggregatedAsSinkTimeout()
        => CallerCancellationWinsWhenAllSinksCompleteAsCancelled();

    [Fact]
    public async Task ProcessedAtIsAfterSinkAggregationCompletes()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var recorder = CreateRecorder([new AdvancingSink("a", clock)], timeProvider: clock);

        var result = await recorder.RecordAsync(CreateEnvelope());

        result.ProcessedAt.Should().Be(DateTimeOffset.UnixEpoch.AddMinutes(1));
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
        IAuditIntegrityHasher? hasher = null,
        TimeProvider? timeProvider = null)
    {
        var writer = new AccountabilityCanonicalProjectionWriter();
        return new DefaultAuditRecorder(
            new AuditEnvelopeValidator(),
            sanitizer ?? new PassThroughSanitizer(),
            hasher ?? new FixedHasher(),
            writer,
            sinks,
            options ?? new AccountabilityOptions(),
            timeProvider ?? TimeProvider.System);
    }

    private static async Task AssertMalformedRejectedAsync(AuditEnvelope envelope, string path)
    {
        var sanitizer = new CountingSanitizer();
        var sink = new RecordingSink("a");
        var result = await CreateRecorder([sink], sanitizer).RecordAsync(envelope);

        result.Status.Should().Be(AuditRecordStatus.Rejected);
        result.Issues.Should().Contain(x => x.Path == path);
        result.Issues.Should().NotContain(x => x.Code == "AUDIT_RECORDER_INTERNAL_FAILURE");
        sanitizer.Calls.Should().Be(0);
        sink.Calls.Should().Be(0);
    }

    private static async Task AssertSinkIdentityMismatchAsync(
        Func<AuditSinkWriteResult, AuditSinkWriteResult> rewrite)
    {
        var result = await CreateRecorder([new RewritingResultSink("provider", rewrite)])
            .RecordAsync(CreateEnvelope());

        result.Status.Should().Be(AuditRecordStatus.Failed);
        result.SinkResults.Should().BeEmpty();
        result.SinkFailures.Should().ContainSingle(x =>
            x.SinkId == "provider" && x.Code == "AUDIT_SINK_RESULT_IDENTITY_MISMATCH");
    }

    private static async Task AssertInvalidSanitizerOutputAsync(
        Func<AuditEnvelope, AuditEnvelope> transform)
    {
        var sink = new RecordingSink("a");
        var result = await CreateRecorder([sink], new TransformingSanitizer(transform))
            .RecordAsync(CreateEnvelope());

        result.Status.Should().Be(AuditRecordStatus.Rejected);
        result.Issues.Should().NotContain(x => x.Code == "AUDIT_RECORDER_INTERNAL_FAILURE");
        sink.Calls.Should().Be(0);
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

    private sealed class OrderedSanitizer(ICollection<string> order) : IAuditSanitizer
    {
        public ValueTask<AuditSanitizationResult> SanitizeAsync(
            AuditEnvelope candidate,
            CancellationToken cancellationToken = default)
        {
            order.Add("sanitize");
            return ValueTask.FromResult(new AuditSanitizationResult
            {
                Envelope = candidate,
                Stamp = new AuditSanitizationStamp { PolicyId = "test", PolicyVersion = 1, AppliedRuleIds = [] }
            });
        }
    }

    private sealed class MinimizingSanitizer : IAuditSanitizer
    {
        public ValueTask<AuditSanitizationResult> SanitizeAsync(
            AuditEnvelope candidate,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AuditSanitizationResult
            {
                Envelope = candidate with
                {
                    Actor = candidate.Actor with { DisplayName = null },
                    Outcome = candidate.Outcome with { SafeSummary = null },
                    Tags = AuditTagMap.Empty
                },
                Stamp = new AuditSanitizationStamp { PolicyId = "test", PolicyVersion = 1, AppliedRuleIds = [] }
            });
    }

    private sealed class TransformingSanitizer(
        Func<AuditEnvelope, AuditEnvelope> transform) : IAuditSanitizer
    {
        public ValueTask<AuditSanitizationResult> SanitizeAsync(
            AuditEnvelope candidate,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AuditSanitizationResult
            {
                Envelope = transform(candidate),
                Stamp = new AuditSanitizationStamp
                {
                    PolicyId = "test",
                    PolicyVersion = 1,
                    AppliedRuleIds = []
                }
            });
    }

    private sealed class CountingSanitizer : IAuditSanitizer
    {
        public int Calls { get; private set; }

        public ValueTask<AuditSanitizationResult> SanitizeAsync(
            AuditEnvelope candidate,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(new AuditSanitizationResult
            {
                Envelope = candidate,
                Stamp = new AuditSanitizationStamp
                {
                    PolicyId = "test",
                    PolicyVersion = 1,
                    AppliedRuleIds = []
                }
            });
        }
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

    private sealed class OrderedSink(string id, ICollection<string> order) : IAuditSink
    {
        public string Id { get; } = id;

        public ValueTask<AuditSinkWriteResult> WriteAsync(
            AuditEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            order.Add("sink");
            return ValueTask.FromResult(new AuditSinkWriteResult
            {
                SinkId = Id,
                AuditId = envelope.AuditId,
                Integrity = envelope.Integrity!,
                Status = AuditSinkWriteStatus.Accepted
            });
        }
    }

    private sealed class CapturingSink(string id) : IAuditSink
    {
        public string Id { get; } = id;
        public AuditEnvelope? Envelope { get; private set; }

        public ValueTask<AuditSinkWriteResult> WriteAsync(
            AuditEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Envelope = envelope;
            return ValueTask.FromResult(new AuditSinkWriteResult
            {
                SinkId = Id,
                AuditId = envelope.AuditId,
                Integrity = envelope.Integrity!,
                Status = AuditSinkWriteStatus.Accepted
            });
        }
    }

    private sealed class RewritingResultSink(
        string id,
        Func<AuditSinkWriteResult, AuditSinkWriteResult> rewrite) : IAuditSink
    {
        public string Id { get; } = id;

        public ValueTask<AuditSinkWriteResult> WriteAsync(
            AuditEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            var valid = new AuditSinkWriteResult
            {
                SinkId = Id,
                AuditId = envelope.AuditId,
                Integrity = envelope.Integrity!,
                Status = AuditSinkWriteStatus.Accepted
            };
            return ValueTask.FromResult(rewrite(valid));
        }
    }

    private sealed class AdvancingSink(string id, ManualTimeProvider timeProvider) : IAuditSink
    {
        public string Id { get; } = id;

        public ValueTask<AuditSinkWriteResult> WriteAsync(
            AuditEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            timeProvider.Advance(TimeSpan.FromMinutes(1));
            return ValueTask.FromResult(new AuditSinkWriteResult
            {
                SinkId = Id,
                AuditId = envelope.AuditId,
                Integrity = envelope.Integrity!,
                Status = AuditSinkWriteStatus.Accepted
            });
        }
    }

    private sealed class CancellationControlledSink : IAuditSink
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationControlledSink(string id) => Id = id;

        public string Id { get; }
        public Task Started => _started.Task;

        public ValueTask<AuditSinkWriteResult> WriteAsync(
            AuditEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            var completion = new TaskCompletionSource<AuditSinkWriteResult>();
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            return new ValueTask<AuditSinkWriteResult>(completion.Task);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now += value;
    }

    private sealed class CapturingHasher : FixedHasher
    {
        public AuditEnvelope? Envelope { get; private set; }

        public override CanonicalHash Compute(AuditEnvelope sanitizedCanonicalEnvelope)
        {
            Envelope = sanitizedCanonicalEnvelope;
            return base.Compute(sanitizedCanonicalEnvelope);
        }
    }
}
