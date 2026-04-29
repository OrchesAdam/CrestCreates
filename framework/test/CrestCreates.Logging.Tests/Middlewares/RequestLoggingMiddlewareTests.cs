using System.Security.Claims;
using System.Threading.Tasks;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Logging.Middlewares;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Logging.Tests.Middlewares;

public class RequestLoggingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ShouldLogRequestAndAttachScopeValues()
    {
        var logger = new TestLogger<RequestLoggingMiddleware>();
        var services = new ServiceCollection()
            .AddSingleton<ICurrentUser>(new FakeCurrentUser())
            .AddSingleton<ICurrentTenant>(new FakeCurrentTenant("tenant-1"))
            .BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = services,
            TraceIdentifier = "trace-123"
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/books";
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");

        var middleware = new RequestLoggingMiddleware(
            _ =>
            {
                context.Response.StatusCode = StatusCodes.Status201Created;
                return Task.CompletedTask;
            },
            logger);

        await middleware.InvokeAsync(context);

        logger.Entries.Should().ContainSingle();
        var entry = logger.Entries.Single();
        entry.LogLevel.Should().Be(Microsoft.Extensions.Logging.LogLevel.Information);
        entry.Message.Should().Contain("Handled HTTP POST /api/books => 201");
        entry.Scope["UserId"].Should().Be("user-1");
        entry.Scope["UserName"].Should().Be("alice");
        entry.Scope["TenantId"].Should().Be("tenant-1");
        entry.Scope["TraceId"].Should().Be("trace-123");
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

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public string Id => "user-1";
        public string UserName => "alice";
        public bool IsAuthenticated => true;
        public string TenantId => "tenant-1";
        public string[] Roles => Array.Empty<string>();
        public Guid? OrganizationId => null;
        public IReadOnlyList<Guid> OrganizationIds => Array.Empty<Guid>();
        public int DataScopeValue => 0;
        public bool IsSuperAdmin => false;
        public string FindClaimValue(string claimType) => string.Empty;
        public string[] FindClaimValues(string claimType) => Array.Empty<string>();
        public bool IsInRole(string roleName) => false;
        public bool IsInOrganization(Guid orgId) => false;
    }
}
