using CrestCreates.AuditLogging.Context;
using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.AuditLogging.Options;
using CrestCreates.AuditLogging.Services;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CrestCreates.AuditLogging.Tests.Middlewares;

public class AuditLoggingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ShouldSkipGetRequests_WhenDisabledForGet()
    {
        var mockWriter = new Mock<IAuditLogWriter>();
        var middleware = CreateMiddleware(
            mockWriter.Object,
            new AuditLoggingOptions { IsEnabledForGetRequests = false },
            _ => Task.CompletedTask);

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/books";

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        mockWriter.Verify(w => w.WriteAsync(It.IsAny<AuditContext>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_ShouldWriteOneAuditLog_ForNormalRequest()
    {
        var mockWriter = new Mock<IAuditLogWriter>();
        mockWriter.Setup(w => w.WriteAsync(It.IsAny<AuditContext>()))
            .Returns(Task.CompletedTask);

        var middleware = CreateMiddleware(
            mockWriter.Object,
            new AuditLoggingOptions { IncludeResponseBody = true },
            async context =>
            {
                context.Response.StatusCode = StatusCodes.Status201Created;
                await context.Response.WriteAsync("{\"ok\":true}");
            });

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/login";
        var requestJson = "{\"user\":\"alice\",\"password\":\"secret\"}";
        context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(requestJson));
        context.Request.ContentLength = System.Text.Encoding.UTF8.GetByteCount(requestJson);
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        mockWriter.Verify(w => w.WriteAsync(It.IsAny<AuditContext>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_ShouldCaptureRawRequestBody_ThenWriterRedacts()
    {
        // Middleware should capture raw (unredacted) body; redaction is the writer's responsibility
        AuditContext? capturedContext = null;
        var mockWriter = new Mock<IAuditLogWriter>();
        mockWriter.Setup(w => w.WriteAsync(It.IsAny<AuditContext>()))
            .Callback<AuditContext>(ctx => capturedContext = ctx)
            .Returns(Task.CompletedTask);

        var middleware = CreateMiddleware(
            mockWriter.Object,
            new AuditLoggingOptions { IncludeRequestBody = true },
            _ => Task.CompletedTask);

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/login";
        var requestJson = "{\"user\":\"alice\",\"password\":\"secret\"}";
        context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(requestJson));
        context.Request.ContentLength = System.Text.Encoding.UTF8.GetByteCount(requestJson);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        // Middleware captures raw body (no redaction at middleware level)
        capturedContext.Should().NotBeNull();
        capturedContext!.RequestBody.Should().Contain("\"password\":\"secret\"");
        capturedContext.RequestBody.Should().Contain("\"user\":\"alice\"");
        // Verify writer received the context (redaction happens inside writer)
        mockWriter.Verify(w => w.WriteAsync(It.IsAny<AuditContext>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_ShouldWriteOnException_AndPreserveOriginalStack()
    {
        AuditContext? capturedContext = null;
        var mockWriter = new Mock<IAuditLogWriter>();
        mockWriter.Setup(w => w.WriteAsync(It.IsAny<AuditContext>()))
            .Callback<AuditContext>(ctx => capturedContext = ctx)
            .Returns(Task.CompletedTask);

        // Pass a no-op to CreateMiddleware, then pass exception-throwing next to InvokeAsync
        var middleware = CreateMiddleware(
            mockWriter.Object,
            new AuditLoggingOptions { IsEnabled = true, AlwaysLogOnException = true, HideErrors = false },
            _ => Task.CompletedTask);

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/books/1";

        // Exception thrown INSIDE InvokeAsync's next parameter - from a separate method to ensure distinct stack
        Func<Task> action = () => middleware.InvokeAsync(context, _ => ThrowTestException());

        var thrownEx = await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("boom");

        // Verify the original throw location is in the stack trace (not just the rethrow location)
        thrownEx.And.StackTrace.Should().Contain("ThrowTestException");

        mockWriter.Verify(w => w.WriteAsync(It.IsAny<AuditContext>()), Times.Once);
        capturedContext.Should().NotBeNull();
        capturedContext!.IsException.Should().BeTrue();
        capturedContext.ExceptionMessage.Should().Contain("boom");
    }

    [Fact]
    public async Task InvokeAsync_ShouldNotRethrow_WhenHideErrorsIsTrue()
    {
        var mockWriter = new Mock<IAuditLogWriter>();
        mockWriter.Setup(w => w.WriteAsync(It.IsAny<AuditContext>()))
            .Throws(new InvalidOperationException("persist failed"));

        var middleware = CreateMiddleware(
            mockWriter.Object,
            new AuditLoggingOptions { HideErrors = true },
            context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            });

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/books";
        context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"title\":\"book\"}"));
        context.Request.ContentLength = 16;

        var action = () => middleware.InvokeAsync(context, _ => Task.CompletedTask);

        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InvokeAsync_ShouldRethrow_WhenHideErrorsIsFalse_AndWriteFails()
    {
        var mockWriter = new Mock<IAuditLogWriter>();
        mockWriter.Setup(w => w.WriteAsync(It.IsAny<AuditContext>()))
            .Throws(new InvalidOperationException("persist failed"));

        var middleware = CreateMiddleware(
            mockWriter.Object,
            new AuditLoggingOptions { HideErrors = false },
            context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            });

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/books";
        context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"title\":\"book\"}"));
        context.Request.ContentLength = 16;

        var action = () => middleware.InvokeAsync(context, _ => Task.CompletedTask);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("persist failed");
    }

    // Helper method to throw an exception - used to verify original stack trace is preserved
    private static Task ThrowTestException()
    {
        throw new InvalidOperationException("boom");
    }

    private static AuditLoggingMiddleware CreateMiddleware(
        IAuditLogWriter auditLogWriter,
        AuditLoggingOptions options,
        RequestDelegate next)
    {
        return new AuditLoggingMiddleware(
            auditLogWriter,
            new FakeCurrentTenant("tenant-1"),
            new OptionsWrapper<AuditLoggingOptions>(options),
            NullLogger<AuditLoggingMiddleware>.Instance);
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
