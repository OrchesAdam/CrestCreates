using System.Text;
using System.Threading;
using CrestCreates.AuditLogging.Context;
using CrestCreates.AuditLogging.Interceptors;
using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.AuditLogging.Options;
using CrestCreates.AuditLogging.Services;
using CrestCreates.Domain.AuditLog;
using CrestCreates.Domain.Repositories;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CrestCreates.AuditLogging.Tests;

public class UnifiedWriteChainTests
{
    [Fact]
    public async Task NormalRequest_ShouldWriteOnlyOneAuditLog()
    {
        // Given: IAuditLogWriter that tracks how many times WriteAsync is called
        var writeCount = 0;
        AuditContext? capturedContext = null;
        var mockWriter = new Mock<IAuditLogWriter>();
        mockWriter.Setup(w => w.WriteAsync(It.IsAny<AuditContext>()))
            .Callback<AuditContext>(ctx => { writeCount++; capturedContext = ctx; })
            .Returns(Task.CompletedTask);

        // When: Middleware processes a POST request (GET is disabled by default)
        var middleware = CreateMiddleware(mockWriter.Object, _ => Task.CompletedTask);
        var context = CreateHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/test";

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        // Then: Exactly one AuditLog is written
        writeCount.Should().Be(1, "one request should produce exactly one unified AuditLog");
        capturedContext.Should().NotBeNull();
    }

    [Fact]
    public async Task NormalRequest_ShouldPersistExecutionTime()
    {
        // Given
        AuditContext? capturedContext = null;
        var mockWriter = new Mock<IAuditLogWriter>();
        mockWriter.Setup(w => w.WriteAsync(It.IsAny<AuditContext>()))
            .Callback<AuditContext>(ctx => capturedContext = ctx)
            .Returns(Task.CompletedTask);

        var middleware = CreateMiddleware(mockWriter.Object, _ => Task.CompletedTask);
        var context = CreateHttpContext();
        context.Request.Method = HttpMethods.Post;

        // When
        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        // Then: ExecutionTime is set
        capturedContext.Should().NotBeNull();
        capturedContext!.ExecutionTime.Should().NotBe(default);
    }

    [Fact]
    public async Task NormalRequest_ShouldPersistTraceId()
    {
        // Given
        AuditContext? capturedContext = null;
        var mockWriter = new Mock<IAuditLogWriter>();
        mockWriter.Setup(w => w.WriteAsync(It.IsAny<AuditContext>()))
            .Callback<AuditContext>(ctx => capturedContext = ctx)
            .Returns(Task.CompletedTask);

        var middleware = CreateMiddleware(mockWriter.Object, _ => Task.CompletedTask);
        var context = CreateHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.TraceIdentifier = "test-trace-id-12345";

        // When
        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        // Then: TraceId is persisted
        capturedContext.Should().NotBeNull();
        capturedContext!.TraceId.Should().Be("test-trace-id-12345");
    }

    [Fact]
    public async Task ExceptionRequest_ShouldWriteOnlyOneFailureAuditLog()
    {
        // Given
        var writeCount = 0;
        AuditContext? capturedContext = null;
        var mockWriter = new Mock<IAuditLogWriter>();
        mockWriter.Setup(w => w.WriteAsync(It.IsAny<AuditContext>()))
            .Callback<AuditContext>(ctx => { writeCount++; capturedContext = ctx; })
            .Returns(Task.CompletedTask);

        var middleware = CreateMiddlewareWithOptions(mockWriter.Object, new AuditLoggingOptions { IsEnabled = true, HideErrors = false });
        var context = CreateHttpContext();
        context.Request.Method = HttpMethods.Post;

        // When/Then
        Func<Task> action = () => middleware.InvokeAsync(context, _ => ThrowBoomException());
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");

        writeCount.Should().Be(1, "exception request should still write exactly one unified AuditLog");
        capturedContext.Should().NotBeNull();
        capturedContext!.IsException.Should().BeTrue();
        capturedContext.ExceptionMessage.Should().Be("boom");
    }

    [Fact]
    public async Task ExceptionRequest_ShouldPreserveOriginalStackTrace()
    {
        // Given
        AuditContext? capturedContext = null;
        var mockWriter = new Mock<IAuditLogWriter>();
        mockWriter.Setup(w => w.WriteAsync(It.IsAny<AuditContext>()))
            .Callback<AuditContext>(ctx => capturedContext = ctx)
            .Returns(Task.CompletedTask);

        var middleware = CreateMiddlewareWithOptions(mockWriter.Object, new AuditLoggingOptions { IsEnabled = true, HideErrors = false });
        var context = CreateHttpContext();
        context.Request.Method = HttpMethods.Post;

        // When/Then - exception thrown from a named method to verify stack trace contains original throw location
        Func<Task> action = () => middleware.InvokeAsync(context, _ => ThrowBoomException());
        var thrownEx = await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");

        // Verify the original throw location is preserved (not just the rethrow location)
        thrownEx.And.StackTrace.Should().Contain("ThrowBoomException");

        // The audit context should have captured the exception details
        capturedContext.Should().NotBeNull();
        capturedContext!.IsException.Should().BeTrue();
        capturedContext.ExceptionMessage.Should().Contain("boom");
    }

    [Fact]
    public async Task WriteChain_ShouldMergeHttpAndMethodLevelIntoOneRecord()
    {
        // Given: A context that simulates what interceptor would enrich
        var mockWriter = new Mock<IAuditLogWriter>();
        AuditContext? capturedContext = null;
        mockWriter.Setup(w => w.WriteAsync(It.IsAny<AuditContext>()))
            .Callback<AuditContext>(ctx => capturedContext = ctx)
            .Returns(Task.CompletedTask);

        // Create middleware with a no-op next
        var middleware = CreateMiddleware(mockWriter.Object, _ => Task.CompletedTask);

        // Create POST context
        var context = CreateHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/test";

        // The enrichment happens INSIDE InvokeAsync's next parameter - simulating what interceptor does
        await middleware.InvokeAsync(context, ctx =>
        {
            // Simulate what AuditedMoAttribute.OnSuccessAsync does - enrich the shared AuditContext
            var auditCtx = AuditContext.Current;
            if (auditCtx != null)
            {
                auditCtx.IsIntercepted = true;
                auditCtx.ServiceName = "TestService";
                auditCtx.MethodName = "TestMethod";
                auditCtx.Parameters = "{\"arg\":1}";
            }
            return Task.CompletedTask;
        });

        // Then: Both HTTP-level and method-level data appear in ONE record
        capturedContext.Should().NotBeNull();
        capturedContext!.Url.Should().Contain("/api/test");
        capturedContext.HttpMethod.Should().Be(HttpMethods.Post);
        capturedContext.IsIntercepted.Should().BeTrue();
        capturedContext.ServiceName.Should().Be("TestService");
        capturedContext.MethodName.Should().Be("TestMethod");
        capturedContext.Parameters.Should().Contain("\"arg\":1");
    }

    [Fact]
    public async Task UnifiedWriter_ShouldCallAuditLogServiceCreateOnce()
    {
        // Given
        var createCallCount = 0;
        var mockRepository = new Mock<IRepository<AuditLog, Guid>>();
        mockRepository.Setup(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback(() => createCallCount++)
            .ReturnsAsync((AuditLog a, CancellationToken _) => a);

        var auditLogService = new AuditLogService(mockRepository.Object);
        var auditLogWriter = new AuditLogWriter(auditLogService, NullLogger<AuditLogWriter>.Instance);

        var context = new AuditContext
        {
            TraceId = "trace-123",
            HttpMethod = HttpMethods.Get,
            Url = "http://localhost/api/test",
            ExecutionTime = DateTime.UtcNow,
            StartTime = DateTime.UtcNow,
            TenantId = "tenant-1",
            HttpStatusCode = 200,
            IsException = false,
            ExtraProperties = new Dictionary<string, object>()
        };

        // When
        await auditLogWriter.WriteAsync(context);

        // Then: IAuditLogService.CreateAsync is called exactly once per request
        createCallCount.Should().Be(1, "Writer should call service.CreateAsync exactly once per request");
    }

    [Fact]
    public void AuditContext_ToAuditLog_ShouldMapAllFieldsCorrectly()
    {
        // Given
        var executionTime = DateTime.UtcNow;
        var startTime = executionTime.AddMilliseconds(-50);
        var context = new AuditContext
        {
            TraceId = "trace-abc",
            HttpMethod = HttpMethods.Post,
            Url = "http://localhost/api/users",
            ClientIpAddress = "127.0.0.1",
            UserAgent = "TestAgent",
            RequestBody = "{\"name\":\"test\"}",
            ResponseBody = "{\"id\":1}",
            HttpStatusCode = 201,
            StartTime = startTime,
            ExecutionTime = executionTime,
            UserId = "user-1",
            UserName = "TestUser",
            TenantId = "tenant-1",
            ServiceName = "UserAppService",
            MethodName = "CreateAsync",
            Parameters = "{\"name\":\"test\"}",
            ReturnValue = "{\"id\":1}",
            ExceptionMessage = null,
            ExceptionStackTrace = null,
            IsIntercepted = true,
            IsException = false,
            ExtraProperties = new Dictionary<string, object> { { "Key", "Value" } }
        };

        // When
        var auditLog = context.ToAuditLog();

        // Then: All fields are correctly mapped
        auditLog.Duration.Should().BeGreaterThanOrEqualTo(0);
        auditLog.ExecutionTime.Should().Be(executionTime);
        auditLog.TraceId.Should().Be("trace-abc");
        auditLog.HttpMethod.Should().Be(HttpMethods.Post);
        auditLog.Url.Should().Be("http://localhost/api/users");
        auditLog.ClientIpAddress.Should().Be("127.0.0.1");
        auditLog.UserId.Should().Be("user-1");
        auditLog.UserName.Should().Be("TestUser");
        auditLog.TenantId.Should().Be("tenant-1");
        auditLog.ServiceName.Should().Be("UserAppService");
        auditLog.MethodName.Should().Be("CreateAsync");
        auditLog.Parameters.Should().Contain("\"name\":\"test\"");
        auditLog.ReturnValue.Should().Contain("\"id\":1");
        auditLog.Status.Should().Be((int)AuditLogStatus.Success);
        auditLog.ExtraProperties.Should().ContainKey("Key");
    }

    [Fact]
    public void AuditContext_ToAuditLog_OnException_ShouldSetFailureStatus()
    {
        // Given
        var context = new AuditContext
        {
            ExecutionTime = DateTime.UtcNow,
            StartTime = DateTime.UtcNow,
            ExceptionMessage = "Something went wrong",
            ExceptionStackTrace = "at TestMethod() in Test.cs:line 42",
            IsException = true,
            IsIntercepted = false
        };

        // When
        var auditLog = context.ToAuditLog();

        // Then
        auditLog.Status.Should().Be((int)AuditLogStatus.Failure);
        auditLog.ExceptionMessage.Should().Be("Something went wrong");
        auditLog.ExceptionStackTrace.Should().Contain("Test.cs");
    }

    // Helper to throw exception from a named method - verifies stack trace preservation
    private static Task ThrowBoomException()
    {
        throw new InvalidOperationException("boom");
    }

    private static AuditLoggingMiddleware CreateMiddleware(
        IAuditLogWriter auditLogWriter,
        RequestDelegate next)
    {
        return new AuditLoggingMiddleware(
            auditLogWriter,
            new FakeCurrentTenant("tenant-1"),
            new OptionsWrapper<AuditLoggingOptions>(new AuditLoggingOptions { IsEnabled = true }),
            NullLogger<AuditLoggingMiddleware>.Instance);
    }

    private static AuditLoggingMiddleware CreateMiddlewareWithOptions(
        IAuditLogWriter auditLogWriter,
        AuditLoggingOptions options)
    {
        return new AuditLoggingMiddleware(
            auditLogWriter,
            new FakeCurrentTenant("tenant-1"),
            new OptionsWrapper<AuditLoggingOptions>(options),
            NullLogger<AuditLoggingMiddleware>.Instance);
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/test";
        context.Request.Body = new MemoryStream();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private sealed class FakeCurrentTenant : ICurrentTenant
    {
        public FakeCurrentTenant(string id)
        {
            Id = id;
            Tenant = new FakeTenantInfo(id);
        }

        public ITenantInfo Tenant { get; }
        public string Id { get; }
        public IDisposable Change(string tenantId) => throw new NotSupportedException();
        public void SetTenantId(string tenantId) => throw new NotSupportedException();
    }

    private sealed class FakeTenantInfo : ITenantInfo
    {
        public FakeTenantInfo(string id)
        {
            Id = id;
            Name = id;
        }

        public string Id { get; }
        public string Name { get; }
        public string? ConnectionString => null;
    }
}
