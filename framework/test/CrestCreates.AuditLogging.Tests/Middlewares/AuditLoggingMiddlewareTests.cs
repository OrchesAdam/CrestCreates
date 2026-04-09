using System.Text;
using CrestCreates.AuditLogging.Entities;
using CrestCreates.AuditLogging.Middlewares;
using CrestCreates.AuditLogging.Options;
using CrestCreates.AuditLogging.Services;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace CrestCreates.AuditLogging.Tests.Middlewares;

public class AuditLoggingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ShouldSkipGetRequests_WhenDisabledForGet()
    {
        var auditService = new RecordingAuditLogService();
        var middleware = CreateMiddleware(
            auditService,
            new AuditLoggingOptions { IsEnabledForGetRequests = false },
            _ => Task.CompletedTask);

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/books";

        await middleware.InvokeAsync(context);

        auditService.Logs.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_ShouldSanitizeRequestBody_AndPersistAuditLog()
    {
        var auditService = new RecordingAuditLogService();
        var middleware = CreateMiddleware(
            auditService,
            new AuditLoggingOptions
            {
                IncludeResponseBody = true
            },
            async context =>
            {
                context.Response.StatusCode = StatusCodes.Status201Created;
                await context.Response.WriteAsync("{\"ok\":true}");
            });

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/login";
        var requestJson = "{\"user\":\"alice\",\"password\":\"secret\"}";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestJson));
        context.Request.ContentLength = Encoding.UTF8.GetByteCount(requestJson);

        await middleware.InvokeAsync(context);

        auditService.Logs.Should().ContainSingle();
        var log = auditService.Logs.Single();
        log.Request.Should().Contain("\"password\":\"***\"");
        log.Response.Should().Be("{\"ok\":true}");
        log.StatusCode.Should().Be(StatusCodes.Status201Created);
    }

    [Fact]
    public async Task InvokeAsync_ShouldPersistOnException_WhenAlwaysLogOnException()
    {
        var auditService = new RecordingAuditLogService();
        var middleware = CreateMiddleware(
            auditService,
            new AuditLoggingOptions
            {
                IsEnabledForGetRequests = false,
                AlwaysLogOnException = true
            },
            _ => throw new InvalidOperationException("boom"));

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/books/1";

        var action = () => middleware.InvokeAsync(context);

        await action.Should().ThrowAsync<InvalidOperationException>();
        auditService.Logs.Should().ContainSingle();
        auditService.Logs.Single().Exception.Should().Contain("boom");
    }

    [Fact]
    public async Task InvokeAsync_ShouldRethrowAuditStoreFailure_WhenHideErrorsIsFalse()
    {
        var auditService = new ThrowingAuditLogService();
        var middleware = CreateMiddleware(
            auditService,
            new AuditLoggingOptions
            {
                HideErrors = false
            },
            context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            });

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/books";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"title\":\"book\"}"));
        context.Request.ContentLength = 16;

        var action = () => middleware.InvokeAsync(context);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("persist failed");
    }

    private static AuditLoggingMiddleware CreateMiddleware(
        IAuditLogService auditLogService,
        AuditLoggingOptions options,
        RequestDelegate next)
    {
        return new AuditLoggingMiddleware(
            next,
            auditLogService,
            new FakeCurrentTenant("tenant-1"),
            Microsoft.Extensions.Options.Options.Create(options),
            new TestLogger<AuditLoggingMiddleware>());
    }

    private sealed class RecordingAuditLogService : IAuditLogService
    {
        public List<AuditLog> Logs { get; } = new();

        public Task CreateAsync(AuditLog auditLog)
        {
            Logs.Add(auditLog);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<AuditLog>> GetListAsync(string userId = null!, string action = null!, DateTime? startTime = null, DateTime? endTime = null, int skip = 0, int take = 100)
            => Task.FromResult<IEnumerable<AuditLog>>(Logs);

        public Task<long> GetCountAsync(string userId = null!, string action = null!, DateTime? startTime = null, DateTime? endTime = null)
            => Task.FromResult((long)Logs.Count);

        public Task DeleteAsync(DateTime olderThan) => Task.CompletedTask;
    }

    private sealed class ThrowingAuditLogService : IAuditLogService
    {
        public Task CreateAsync(AuditLog auditLog) => throw new InvalidOperationException("persist failed");
        public Task<IEnumerable<AuditLog>> GetListAsync(string userId = null!, string action = null!, DateTime? startTime = null, DateTime? endTime = null, int skip = 0, int take = 100) => Task.FromResult<IEnumerable<AuditLog>>(Array.Empty<AuditLog>());
        public Task<long> GetCountAsync(string userId = null!, string action = null!, DateTime? startTime = null, DateTime? endTime = null) => Task.FromResult(0L);
        public Task DeleteAsync(DateTime olderThan) => Task.CompletedTask;
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
