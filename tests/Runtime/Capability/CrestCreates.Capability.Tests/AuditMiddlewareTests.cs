using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
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
    public async Task InvokeAsync_Success_RecordsAuditWithSuccess()
    {
        var auditStore = new InMemoryCapabilityAuditStore();
        var middleware = new AuditMiddleware(auditStore, TestLogger);

        var context = new CapabilityExecutionContext
        {
            ServiceProvider = null!,
            CapabilityId = "test.cap",
            CapabilityName = "Test Cap",
            CapabilityVersion = 1,
            InvocationSource = InvocationSource.Workflow
        };

        var result = await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success("output", TimeSpan.FromMilliseconds(10))));

        result.IsSuccess.Should().BeTrue();
        var records = auditStore.GetRecords();
        records.Should().HaveCount(1);
        records[0].CapabilityId.Should().Be("test.cap");
        records[0].IsSuccess.Should().BeTrue();
        records[0].Duration.Should().BePositive();
        records[0].CorrelationId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task InvokeAsync_Failure_RecordsAuditWithErrorCode()
    {
        var auditStore = new InMemoryCapabilityAuditStore();
        var middleware = new AuditMiddleware(auditStore, TestLogger);

        var context = new CapabilityExecutionContext
        {
            ServiceProvider = null!,
            CapabilityId = "test.cap",
            CapabilityName = "Test Cap",
            CapabilityVersion = 1,
            InvocationSource = InvocationSource.Http
        };

        var result = await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Failure("ERR_001", "Something went wrong", TimeSpan.Zero)));

        result.ErrorCode.Should().Be("ERR_001");
        var records = auditStore.GetRecords();
        records.Should().HaveCount(1);
        records[0].IsSuccess.Should().BeFalse();
        records[0].ErrorCode.Should().Be("ERR_001");
        records[0].Duration.Should().BePositive();
        records[0].CorrelationId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task InvokeAsync_HandlerThrows_RecordsUnhandledException()
    {
        var auditStore = new InMemoryCapabilityAuditStore();
        var middleware = new AuditMiddleware(auditStore, TestLogger);

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
        var records = auditStore.GetRecords();
        records.Should().HaveCount(1);
        records[0].IsSuccess.Should().BeFalse();
        records[0].ErrorCode.Should().Be("UNHANDLED_EXCEPTION");
    }

    [Fact]
    public async Task InvokeAsync_Cancelled_RecordsCancelledStatus()
    {
        var auditStore = new InMemoryCapabilityAuditStore();
        var middleware = new AuditMiddleware(auditStore, TestLogger);

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
        var records = auditStore.GetRecords();
        records.Should().HaveCount(1);
        records[0].ErrorCode.Should().Be("CANCELLED");
    }

    [Fact]
    public async Task InvokeAsync_AuditStoreThrows_PipelineStillReturnsResult()
    {
        var auditStoreMock = new Mock<ICapabilityAuditStore>();
        auditStoreMock.Setup(s => s.RecordAsync(It.IsAny<CapabilityExecutionRecord>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Audit failure"));
        var middleware = new AuditMiddleware(auditStoreMock.Object, TestLogger);

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
}
