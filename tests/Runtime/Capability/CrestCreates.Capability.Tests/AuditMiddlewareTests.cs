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

    private static AuditMiddleware CreateMiddleware(IAuditRecorder recorder)
        => new(TestLogger, recorder, new FixedIdentity(), new AuditOperationContextAccessor());

    private sealed class CaptureRecorder : IAuditRecorder
    {
        public AuditEnvelope? Envelope { get; private set; }
        public ValueTask<AuditRecordResult> RecordAsync(AuditEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Envelope = envelope;
            return ValueTask.FromResult(new AuditRecordResult { AuditId = "audit-1", Status = AuditRecordStatus.Recorded, ProcessedAt = DateTimeOffset.UtcNow });
        }
    }

    private sealed class ThrowingRecorder : IAuditRecorder
    {
        public ValueTask<AuditRecordResult> RecordAsync(AuditEnvelope envelope, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Audit failure");
    }

    private sealed class FixedIdentity : IAuditIdentityGenerator
    {
        public string CreateAuditId() => "audit-1";
        public string CreateOperationId() => "operation-1";
    }
}
