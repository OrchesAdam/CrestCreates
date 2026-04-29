using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        var mockRedactor = new Mock<IAuditLogRedactor>();
        mockRedactor.Setup(r => r.RedactAsync(It.IsAny<AuditContext>()))
            .Returns(Task.CompletedTask);
        var auditLogWriter = new AuditLogWriter(auditLogService, mockRedactor.Object, NullLogger<AuditLogWriter>.Instance);

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

    [Fact]
    public async Task Redactor_ShouldRedactPassword_InRequestBody()
    {
        // Given
        var redactor = CreateRedactor();
        var context = new AuditContext
        {
            ExecutionTime = DateTime.UtcNow,
            StartTime = DateTime.UtcNow,
            RequestBody = "{\"user\":\"alice\",\"password\":\"secret123\"}",
            ExtraProperties = new Dictionary<string, object>()
        };

        // When
        await redactor.RedactAsync(context);

        // Then
        context.RequestBody.Should().Contain("\"password\":\"***\"");
        context.RequestBody.Should().Contain("\"user\":\"alice\"");
        context.RequestBody.Should().NotContain("secret123");
    }

    [Fact]
    public async Task Redactor_ShouldRedactToken_InParameters()
    {
        // Given
        var redactor = CreateRedactor();
        var context = new AuditContext
        {
            ExecutionTime = DateTime.UtcNow,
            StartTime = DateTime.UtcNow,
            Parameters = "{\"username\":\"bob\",\"token\":\"jwt-token-value\",\"data\":\"ok\"}",
            ExtraProperties = new Dictionary<string, object>()
        };

        // When
        await redactor.RedactAsync(context);

        // Then
        context.Parameters.Should().Contain("\"token\":\"***\"");
        context.Parameters.Should().Contain("\"username\":\"bob\"");
        context.Parameters.Should().Contain("\"data\":\"ok\"");
        context.Parameters.Should().NotContain("jwt-token-value");
    }

    [Fact]
    public async Task Redactor_ShouldRedactSecretAndConnectionString_InReturnValue()
    {
        // Given
        var redactor = CreateRedactor();
        var context = new AuditContext
        {
            ExecutionTime = DateTime.UtcNow,
            StartTime = DateTime.UtcNow,
            ReturnValue = "{\"result\":\"ok\",\"secret\":\"my-secret\",\"connectionString\":\"Server=db;\"}",
            ExtraProperties = new Dictionary<string, object>()
        };

        // When
        await redactor.RedactAsync(context);

        // Then
        context.ReturnValue.Should().Contain("\"secret\":\"***\"");
        context.ReturnValue.Should().Contain("\"connectionString\":\"***\"");
        context.ReturnValue.Should().Contain("\"result\":\"ok\"");
        context.ReturnValue.Should().NotContain("my-secret");
        context.ReturnValue.Should().NotContain("Server=db");
    }

    [Fact]
    public async Task Redactor_ShouldRedactSensitiveKeys_InExtraProperties()
    {
        // Given
        var redactor = CreateRedactor();
        var context = new AuditContext
        {
            ExecutionTime = DateTime.UtcNow,
            StartTime = DateTime.UtcNow,
            ExtraProperties = new Dictionary<string, object>
            {
                { "userId", "123" },
                { "password", "hashed-pwd" },
                { "refreshToken", "rt-abc" },
                { "secretKey", "sk-xyz" }
            }
        };

        // When
        await redactor.RedactAsync(context);

        // Then
        context.ExtraProperties["userId"].Should().Be("123");
        context.ExtraProperties["password"].Should().Be("***");
        context.ExtraProperties["refreshToken"].Should().Be("***");
        context.ExtraProperties["secretKey"].Should().Be("***");
    }

    [Fact]
    public async Task Redactor_ShouldNotModifyNonSensitiveFields()
    {
        // Given
        var redactor = CreateRedactor();
        var context = new AuditContext
        {
            ExecutionTime = DateTime.UtcNow,
            StartTime = DateTime.UtcNow,
            RequestBody = "{\"name\":\"book\",\"author\":\"author-name\"}",
            ResponseBody = "{\"id\":1,\"title\":\"book-title\"}",
            Parameters = "{\"title\":\"book\",\"author\":\"author\"}",
            ReturnValue = "{\"id\":1,\"title\":\"returned\"}",
            ExtraProperties = new Dictionary<string, object>
            {
                { "tenantId", "tenant-1" },
                { "userId", "user-1" }
            }
        };

        // When
        await redactor.RedactAsync(context);

        // Then
        context.RequestBody.Should().Contain("\"name\":\"book\"");
        context.RequestBody.Should().Contain("\"author\":\"author-name\"");
        context.ResponseBody.Should().Contain("\"id\":1");
        context.ResponseBody.Should().Contain("\"title\":\"book-title\"");
        context.Parameters.Should().Contain("\"title\":\"book\"");
        context.ReturnValue.Should().Contain("\"id\":1");
        context.ExtraProperties["tenantId"].Should().Be("tenant-1");
        context.ExtraProperties["userId"].Should().Be("user-1");
    }

    [Fact]
    public async Task WriteChain_ShouldPersistRedactedAuditLog_NotRawData()
    {
        // Given
        AuditLog? capturedAuditLog = null;
        var mockRepository = new Mock<IRepository<AuditLog, Guid>>();
        mockRepository.Setup(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((a, _) => capturedAuditLog = a)
            .ReturnsAsync((AuditLog a, CancellationToken _) => a);

        var auditLogService = new AuditLogService(mockRepository.Object);
        var redactor = new AuditLogRedactor(new OptionsWrapper<AuditLoggingOptions>(new AuditLoggingOptions()));
        var auditLogWriter = new AuditLogWriter(auditLogService, redactor, NullLogger<AuditLogWriter>.Instance);

        var context = new AuditContext
        {
            TraceId = "trace-789",
            HttpMethod = HttpMethods.Post,
            Url = "http://localhost/api/login",
            ExecutionTime = DateTime.UtcNow,
            StartTime = DateTime.UtcNow,
            TenantId = "tenant-1",
            HttpStatusCode = 200,
            IsException = false,
            // RequestBody/ResponseBody are captured in AuditContext but not mapped to AuditLog entity
            // They are covered by redactor tests separately
            Parameters = "{\"input\":\"data\",\"accessToken\":\"tok-123\"}",
            ReturnValue = "{\"result\":\"ok\",\"secretKey\":\"sk-456\"}",
            ExtraProperties = new Dictionary<string, object>
            {
                { "connectionString", "Server=localhost" },
                { "normalField", "visible" }
            }
        };

        // When
        await auditLogWriter.WriteAsync(context);

        // Then: Verify the AuditLog passed to repository is already redacted
        capturedAuditLog.Should().NotBeNull();
        capturedAuditLog!.Parameters.Should().Contain("\"accessToken\":\"***\"");
        capturedAuditLog.Parameters.Should().NotContain("tok-123");
        capturedAuditLog.ReturnValue.Should().Contain("\"secretKey\":\"***\"");
        capturedAuditLog.ReturnValue.Should().NotContain("sk-456");
        capturedAuditLog.ExtraProperties.Should().ContainKey("connectionString");
        // ExtraProperties is Dictionary<string, object>; string values deserialize as JsonElement in EF Core
        var connValue = capturedAuditLog.ExtraProperties["connectionString"];
        connValue.ToString().Should().Be("***");
        var normalValue = capturedAuditLog.ExtraProperties["normalField"];
        normalValue.ToString().Should().Be("visible");
    }

    [Fact]
    public async Task Redactor_ShouldRedactNewPassword_AndCurrentPassword()
    {
        // Given
        var redactor = CreateRedactor();
        var context = new AuditContext
        {
            ExecutionTime = DateTime.UtcNow,
            StartTime = DateTime.UtcNow,
            RequestBody = "{\"currentPassword\":\"old\",\"newPassword\":\"new-secret\"}",
            ExtraProperties = new Dictionary<string, object>()
        };

        // When
        await redactor.RedactAsync(context);

        // Then
        context.RequestBody.Should().Contain("\"currentPassword\":\"***\"");
        context.RequestBody.Should().Contain("\"newPassword\":\"***\"");
        context.RequestBody.Should().NotContain("old");
        context.RequestBody.Should().NotContain("new-secret");
    }

    [Fact]
    public async Task Redactor_ShouldRedactSensitiveKeys_InExceptionMessage()
    {
        // Given
        var redactor = CreateRedactor();
        var context = new AuditContext
        {
            ExecutionTime = DateTime.UtcNow,
            StartTime = DateTime.UtcNow,
            IsException = true,
            ExceptionMessage = "Login failed for user \"alice\" with password=\"Secret123\" and token=\"tok-abc\"",
            ExtraProperties = new Dictionary<string, object>()
        };

        // When
        await redactor.RedactAsync(context);

        // Then
        context.ExceptionMessage.Should().Contain("***");
        context.ExceptionMessage.Should().NotContain("Secret123");
        context.ExceptionMessage.Should().NotContain("tok-abc");
        context.ExceptionMessage.Should().Contain("alice");
    }

    [Fact]
    public async Task Redactor_ShouldRedactSensitiveKeys_InExceptionStackTrace()
    {
        // Given
        var redactor = CreateRedactor();
        var context = new AuditContext
        {
            ExecutionTime = DateTime.UtcNow,
            StartTime = DateTime.UtcNow,
            IsException = true,
            ExceptionStackTrace = @"at LoginService.Authenticate(String username, String password, String token) in /src/Service.cs:line 42
caused by: password=""supersecret\"", token=""tok-xyz""",
            ExtraProperties = new Dictionary<string, object>()
        };

        // When
        await redactor.RedactAsync(context);

        // Then
        context.ExceptionStackTrace.Should().Contain("***");
        context.ExceptionStackTrace.Should().NotContain("supersecret");
        context.ExceptionStackTrace.Should().NotContain("tok-xyz");
    }

    [Fact]
    public async Task WriteChain_ShouldPersistRedactedExceptionContext()
    {
        // Given
        AuditLog? capturedAuditLog = null;
        var mockRepository = new Mock<IRepository<AuditLog, Guid>>();
        mockRepository.Setup(r => r.AddAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((a, _) => capturedAuditLog = a)
            .ReturnsAsync((AuditLog a, CancellationToken _) => a);

        var auditLogService = new AuditLogService(mockRepository.Object);
        var redactor = new AuditLogRedactor(new OptionsWrapper<AuditLoggingOptions>(new AuditLoggingOptions()));
        var auditLogWriter = new AuditLogWriter(auditLogService, redactor, NullLogger<AuditLogWriter>.Instance);

        var context = new AuditContext
        {
            TraceId = "trace-exc-001",
            HttpMethod = HttpMethods.Post,
            Url = "http://localhost/api/login",
            ExecutionTime = DateTime.UtcNow,
            StartTime = DateTime.UtcNow,
            TenantId = "tenant-1",
            HttpStatusCode = 500,
            IsException = true,
            ExceptionMessage = "Auth failed for password=\"P@ssw0rd\" and refreshToken=\"rt-999\"",
            ExceptionStackTrace = "at Login(password=\"P@ssw0rd\") in /src/Service.cs:line 10",
            ExtraProperties = new Dictionary<string, object>()
        };

        // When
        await auditLogWriter.WriteAsync(context);

        // Then: Verify the AuditLog passed to repository has redacted exception context
        capturedAuditLog.Should().NotBeNull();
        capturedAuditLog!.ExceptionMessage.Should().Contain("***");
        capturedAuditLog.ExceptionMessage.Should().NotContain("P@ssw0rd");
        capturedAuditLog.ExceptionMessage.Should().NotContain("rt-999");
        capturedAuditLog.ExceptionStackTrace.Should().Contain("***");
        capturedAuditLog.ExceptionStackTrace.Should().NotContain("P@ssw0rd");
    }

    private static AuditLogRedactor CreateRedactor()
    {
        return new AuditLogRedactor(new OptionsWrapper<AuditLoggingOptions>(new AuditLoggingOptions()));
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
        public Task<IDisposable> ChangeAsync(string tenantId) => throw new NotSupportedException();
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
