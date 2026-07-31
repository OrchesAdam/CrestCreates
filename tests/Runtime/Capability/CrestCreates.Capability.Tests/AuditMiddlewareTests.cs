using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using CrestCreates.Accountability.Abstractions.Contracts;
using CrestCreates.Accountability.Abstractions.Identity;
using CrestCreates.Accountability.Abstractions.Recording;
using CrestCreates.Accountability.Context;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class AuditMiddlewareTests
{
    private static ILogger<AuditMiddleware> TestLogger => NullLogger<AuditMiddleware>.Instance;

    [Fact]
    public async Task AccountabilityRecorderReceivesResolvedCapabilityFact()
    {
        var recorder = new CaptureRecorder();
        var middleware = CreateMiddleware(recorder);
        var context = new CapabilityExecutionContext
        {
            ServiceProvider = null!,
            CapabilityId = "capability-1",
            CapabilityName = "Do thing",
            CapabilityVersion = 2,
            InvocationSource = InvocationSource.Agent,
            UserId = "agent-1",
            AccountabilityActor = new AuditActor { Kind = "agent", Id = "agent-1" },
            CorrelationId = "corr-1",
            AccountabilityRuntimeReferences = []
        };

        var result = await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success("ok", TimeSpan.FromMilliseconds(2))));

        result.IsSuccess.Should().BeTrue();
        recorder.Envelope.Should().NotBeNull();
        recorder.Envelope!.Action.Kind.Should().Be("capability.execute");
        recorder.Envelope.Action.Name.Should().Be("capability-1");
        recorder.Envelope.Target.Id.Should().Be("capability-1");
        recorder.Envelope.Outcome.Status.Should().Be("succeeded");
        recorder.Envelope.Actor.Kind.Should().Be("agent");
        context.AuditRecordId.Should().Be("audit-1");
    }

    [Theory]
    [InlineData(InvocationSource.Agent, "agent")]
    [InlineData(InvocationSource.Mcp, "mcp")]
    public async Task ProtocolSourceWithoutTrustedPrincipalUsesUnknownActor(
        InvocationSource source,
        string expectedRuntimeSource)
    {
        var recorder = new CaptureRecorder();
        var middleware = CreateMiddleware(recorder);
        var context = new CapabilityExecutionContext
        {
            ServiceProvider = null!,
            CapabilityId = "capability-1",
            CapabilityName = "Do thing",
            CapabilityVersion = 1,
            InvocationSource = source,
            UserId = "human-user",
            CorrelationId = "corr-1"
        };

        await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success(null, TimeSpan.Zero)));

        recorder.Envelope!.Actor.Should().Be(new AuditActor { Kind = "unknown", Id = "unknown" });
        recorder.Envelope.Runtime.InvocationSource.Should().Be(expectedRuntimeSource);
    }
    [Fact]
    public async Task InvokeAsync_Success_RecordsAuditWithSuccess()
    {
        var recorder = new CaptureRecorder();
        var middleware = CreateMiddleware(recorder);

        var context = new CapabilityExecutionContext
        {
            ServiceProvider = null!,
            CapabilityId = "test.cap",
            CapabilityName = "Test Cap",
            CapabilityVersion = 1,
            InvocationSource = InvocationSource.Workflow,
            CorrelationId = "correlation-1"
        };

        var result = await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success("output", TimeSpan.FromMilliseconds(10))));

        result.IsSuccess.Should().BeTrue();
        recorder.Envelope.Should().NotBeNull();
        recorder.Envelope!.Target.Id.Should().Be("test.cap");
        recorder.Envelope.Outcome.Status.Should().Be("succeeded");
        recorder.Envelope.Runtime.Duration.Should().BePositive();
        recorder.Envelope.CorrelationId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task InvokeAsync_Failure_RecordsAuditWithErrorCode()
    {
        var recorder = new CaptureRecorder();
        var middleware = CreateMiddleware(recorder);

        var context = new CapabilityExecutionContext
        {
            ServiceProvider = null!,
            CapabilityId = "test.cap",
            CapabilityName = "Test Cap",
            CapabilityVersion = 1,
            InvocationSource = InvocationSource.Http,
            CorrelationId = "correlation-1"
        };

        var result = await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Failure("ERR_001", "Something went wrong", TimeSpan.Zero)));

        result.ErrorCode.Should().Be("ERR_001");
        recorder.Envelope.Should().NotBeNull();
        recorder.Envelope!.Outcome.Status.Should().Be("failed");
        recorder.Envelope.Outcome.Code.Should().Be("ERR_001");
        recorder.Envelope.Runtime.Duration.Should().BePositive();
        recorder.Envelope.CorrelationId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task InvokeAsync_HandlerThrows_RecordsUnhandledException()
    {
        var recorder = new CaptureRecorder();
        var middleware = CreateMiddleware(recorder);

        var context = new CapabilityExecutionContext
        {
            ServiceProvider = null!,
            CapabilityId = "test.cap",
            CapabilityName = "Test Cap",
            CapabilityVersion = 1,
            InvocationSource = InvocationSource.Agent
        };

        var thrown = false;
        try
        {
            await middleware.InvokeAsync(context, _ =>
                throw new InvalidOperationException("Boom"));
        }
        catch (InvalidOperationException)
        {
            thrown = true;
        }

        thrown.Should().BeTrue();
        recorder.Envelope.Should().NotBeNull();
        recorder.Envelope!.Outcome.Status.Should().Be("failed");
        recorder.Envelope.Outcome.Code.Should().Be("UNHANDLED_EXCEPTION");
    }

    [Fact]
    public async Task CapabilityFailureException_AuditPreservesCanonicalErrorCode()
    {
        var recorder = new CaptureRecorder();
        var middleware = CreateMiddleware(recorder);
        var context = new CapabilityExecutionContext
        {
            ServiceProvider = null!,
            CapabilityId = "test.canonical-failure",
            CapabilityName = "Canonical failure",
            CapabilityVersion = 1,
            InvocationSource = InvocationSource.Http
        };

        var act = () => middleware.InvokeAsync(context, _ =>
            throw new CapabilityFailureException("CAPABILITY_RESOURCE_NOT_FOUND", "Unavailable."));

        await act.Should().ThrowAsync<CapabilityFailureException>();
        recorder.Envelope!.Outcome.Code.Should().Be("CAPABILITY_RESOURCE_NOT_FOUND");
    }

    [Fact]
    public async Task InvokeAsync_Cancelled_RecordsCancelledStatus()
    {
        var recorder = new CaptureRecorder();
        var middleware = CreateMiddleware(recorder);

        var context = new CapabilityExecutionContext
        {
            ServiceProvider = null!,
            CapabilityId = "test.cap",
            CapabilityName = "Test Cap",
            CapabilityVersion = 1,
            InvocationSource = InvocationSource.Workflow
        };

        var thrown = false;
        try
        {
            await middleware.InvokeAsync(context, _ =>
                throw new OperationCanceledException());
        }
        catch (OperationCanceledException)
        {
            thrown = true;
        }

        thrown.Should().BeTrue();
        recorder.Envelope.Should().NotBeNull();
        recorder.Envelope!.Outcome.Status.Should().Be("cancelled");
        recorder.Envelope.Outcome.Code.Should().Be("CANCELLED");
    }

    [Fact]
    public async Task InvokeAsync_RecorderThrows_PipelineStillReturnsResult()
    {
        var middleware = CreateMiddleware(new ThrowingRecorder());

        var context = new CapabilityExecutionContext
        {
            ServiceProvider = null!,
            CapabilityId = "test.cap",
            CapabilityName = "Test Cap",
            CapabilityVersion = 1,
            InvocationSource = InvocationSource.Http
        };

        var result = await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success("ok", TimeSpan.Zero)));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DuplicateReplayPreservesCapabilityAuditRecordId()
    {
        var middleware = CreateMiddleware(new DuplicateRecorder());
        var context = new CapabilityExecutionContext
        {
            ServiceProvider = null!,
            CapabilityId = "test.cap",
            CapabilityName = "Mutable display name",
            CapabilityVersion = 1,
            InvocationSource = InvocationSource.Http,
            CorrelationId = "correlation-1"
        };

        var result = await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success("ok", TimeSpan.Zero)));

        result.AuditRecordId.Should().Be("audit-1");
        context.AuditRecordId.Should().Be("audit-1");
    }

    [Fact]
    public async Task EmitsReturnedSuccessAndFailure()
    {
        await InvokeAsync_Success_RecordsAuditWithSuccess();
        await InvokeAsync_Failure_RecordsAuditWithErrorCode();
    }

    [Fact]
    public async Task EmitsCapabilityFailureCancellationAndUnhandledException()
    {
        await CapabilityFailureException_AuditPreservesCanonicalErrorCode();
        await InvokeAsync_Cancelled_RecordsCancelledStatus();
        await InvokeAsync_HandlerThrows_RecordsUnhandledException();
    }

    [Fact]
    public Task ResolvedCapabilityEnteringPipelineAlwaysEmitsFact()
        => AccountabilityRecorderReceivesResolvedCapabilityFact();

    [Fact]
    public async Task CapabilityOccurredAtIsTerminalObservationTime()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var recorder = new CaptureRecorder();
        var middleware = CreateMiddleware(recorder, clock);
        var context = Context();

        await middleware.InvokeAsync(context, _ =>
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            return Task.FromResult(CapabilityExecutionResult.Success(null, TimeSpan.Zero));
        });

        recorder.Envelope!.OccurredAt.Should().Be(DateTimeOffset.UnixEpoch.AddMinutes(1));
    }

    [Fact]
    public async Task CapabilityActorFollowsEffectiveInvocationAuthority()
    {
        var recorder = new CaptureRecorder();
        var context = Context();
        context.InvocationSource = InvocationSource.Http;
        context.UserId = "user-1";
        await CreateMiddleware(recorder).InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success(null, TimeSpan.Zero)));
        recorder.Envelope!.Actor.Should().Be(new AuditActor { Kind = "user", Id = "user-1" });
    }

    [Fact]
    public async Task PreservesOriginalExceptionStack()
    {
        var recorder = new CaptureRecorder();
        var expected = new InvalidOperationException("original");
        var action = () => CreateMiddleware(recorder).InvokeAsync(Context(), _ => throw expected);
        var thrown = await action.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task CarriesCorrelationCausationParentAndActor()
    {
        var recorder = new CaptureRecorder();
        var context = Context();
        context.CausationId = "cause-1";
        context.ParentAuditId = "parent-1";
        context.AccountabilityActor = new AuditActor { Kind = "agent", Id = "agent-1" };
        await CreateMiddleware(recorder).InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success(null, TimeSpan.Zero)));
        recorder.Envelope!.CorrelationId.Should().Be("correlation-1");
        recorder.Envelope.CausationId.Should().Be("cause-1");
        recorder.Envelope.ParentAuditId.Should().Be("parent-1");
        recorder.Envelope.Actor.Id.Should().Be("agent-1");
    }

    [Fact]
    public async Task CarriesStructuredDescriptorContractHash()
    {
        var recorder = new CaptureRecorder();
        var context = Context();
        context.AccountabilityContract = TestHash;
        await CreateMiddleware(recorder).InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success(null, TimeSpan.Zero)));
        recorder.Envelope!.Descriptors.Items.Single().ContractHash.Should().Be(TestHash);
    }

    [Fact]
    public async Task AttachesAuditRecordIdOnlyWhenAccepted()
    {
        var context = Context();
        var result = await CreateMiddleware(new RejectedRecorder()).InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success(null, TimeSpan.Zero)));
        result.AuditRecordId.Should().BeNull();
        context.AuditRecordId.Should().BeNull();
    }

    [Fact]
    public async Task CapabilityDoesNotExposeAuditRecordIdFromStatusAlone()
    {
        var context = Context();
        var result = await CreateMiddleware(new StatusOnlyRecorder()).InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success(null, TimeSpan.Zero)));

        result.AuditRecordId.Should().BeNull();
        context.AuditRecordId.Should().BeNull();
    }

    [Fact]
    public async Task CapabilityExposesAuditRecordIdFromAcceptedSinkOnly()
    {
        var context = Context();
        var result = await CreateMiddleware(new CaptureRecorder()).InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success(null, TimeSpan.Zero)));

        result.AuditRecordId.Should().Be("audit-1");
        context.AuditRecordId.Should().Be("audit-1");
    }

    [Fact]
    public Task OuterCatchResultReceivesAcceptedAuditRecordId()
        => new CapabilityEndToEndTests().E2E_HandlerThrows_RecordsUnhandledException();

    [Fact]
    public async Task RemovesDynamicApiTraceIdentifierCausationSubstitution()
    {
        var recorder = new CaptureRecorder();
        await CreateMiddleware(recorder).InvokeAsync(Context(), _ =>
            Task.FromResult(CapabilityExecutionResult.Success(null, TimeSpan.Zero)));
        recorder.Envelope!.CausationId.Should().BeNull();
    }

    [Fact]
    public Task AuditFailureDoesNotChangeCapabilityResult()
        => InvokeAsync_RecorderThrows_PipelineStillReturnsResult();

    private static AuditMiddleware CreateMiddleware(IAuditRecorder recorder, TimeProvider? timeProvider = null)
        => new(TestLogger, recorder, new FixedIdentity(), new AuditOperationContextAccessor(), timeProvider);

    private static CapabilityExecutionContext Context()
        => new()
        {
            ServiceProvider = null!,
            CapabilityId = "test.capability",
            CapabilityName = "Mutable display name",
            CapabilityVersion = 1,
            InvocationSource = InvocationSource.Internal,
            CorrelationId = "correlation-1"
        };

    private sealed class CaptureRecorder : IAuditRecorder
    {
        public AuditEnvelope? Envelope { get; private set; }
        public ValueTask<AuditRecordResult> RecordAsync(AuditEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Envelope = envelope;
            return ValueTask.FromResult(AcceptedResult("audit-1"));
        }
    }

    private static AuditRecordResult AcceptedResult(string auditId)
        => new()
        {
            AuditId = auditId,
            Status = AuditRecordStatus.Recorded,
            ProcessedAt = DateTimeOffset.UtcNow,
            SinkResults =
            [
                new CrestCreates.Accountability.Abstractions.Sinks.AuditSinkWriteResult
                {
                    SinkId = "test",
                    AuditId = auditId,
                    Integrity = TestHash,
                    Status = CrestCreates.Accountability.Abstractions.Sinks.AuditSinkWriteStatus.Accepted
                }
            ]
        };

    private sealed class ThrowingRecorder : IAuditRecorder
    {
        public ValueTask<AuditRecordResult> RecordAsync(AuditEnvelope envelope, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Audit failure");
    }

    private sealed class RejectedRecorder : IAuditRecorder
    {
        public ValueTask<AuditRecordResult> RecordAsync(
            AuditEnvelope envelope,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AuditRecordResult
            {
                AuditId = envelope.AuditId,
                Status = AuditRecordStatus.Rejected,
                ProcessedAt = DateTimeOffset.UtcNow,
                Issues = [new AuditRecordIssue("TEST_REJECTION")]
            });
    }

    private sealed class StatusOnlyRecorder : IAuditRecorder
    {
        public ValueTask<AuditRecordResult> RecordAsync(
            AuditEnvelope envelope,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AuditRecordResult
            {
                AuditId = envelope.AuditId,
                Status = AuditRecordStatus.Recorded,
                ProcessedAt = DateTimeOffset.UtcNow
            });
    }

    private sealed class DuplicateRecorder : IAuditRecorder
    {
        public ValueTask<AuditRecordResult> RecordAsync(
            AuditEnvelope envelope,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AuditRecordResult
            {
                AuditId = envelope.AuditId,
                Status = AuditRecordStatus.Recorded,
                ProcessedAt = DateTimeOffset.UtcNow,
                SinkResults =
                [
                    new CrestCreates.Accountability.Abstractions.Sinks.AuditSinkWriteResult
                    {
                        SinkId = "in-memory",
                        AuditId = envelope.AuditId,
                        Integrity = TestHash,
                        Status = CrestCreates.Accountability.Abstractions.Sinks.AuditSinkWriteStatus.Duplicate
                    }
                ]
            });

        private static CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHash TestHash { get; } = new()
        {
            Value = "hash",
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = "AccountabilityRecord",
            Scope = "InternalFull",
            Purpose = "AuditEvidence",
            ContractVersion = "canonical-hash-v1",
            CanonicalShapeVersion = "accountability-record-hash-v1"
        };
    }

    private sealed class FixedIdentity : IAuditIdentityGenerator
    {
        public string CreateAuditId() => "audit-1";
        public string CreateOperationId() => "operation-1";
    }

    private static CrestCreates.Metadata.Abstractions.CanonicalHashing.CanonicalHash TestHash { get; } = new()
    {
        Value = "hash",
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = "CapabilityDescriptor",
        Scope = "Contract",
        Purpose = "ContractIdentity",
        ContractVersion = "canonical-hash-v1",
        CanonicalShapeVersion = "capability-v1"
    };

    private sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now += value;
    }
}
