using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.MultiTenancy.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrestCreates.Web.Tests.Middlewares;

public class MultiTenancyMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WithUnavailableTenant_ReturnsForbidden()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantProvider>(new NullTenantProvider());
        var serviceProvider = services.BuildServiceProvider();
        var currentTenant = new CurrentTenant(serviceProvider);
        var middleware = new MultiTenancyMiddleware(
            _ => Task.CompletedTask,
            NullLogger<MultiTenancyMiddleware>.Instance);
        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider,
            TraceIdentifier = "trace-tenant-unavailable"
        };
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, currentTenant, new StaticTenantResolver("tenant-a"));

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task InvokeAsync_WithValidTenant_SetsCurrentTenantAndCallsNext()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantProvider>(new StaticTenantProvider("tenant-a"));
        var serviceProvider = services.BuildServiceProvider();
        var currentTenant = new CurrentTenant(serviceProvider);
        var nextCalled = false;
        var middleware = new MultiTenancyMiddleware(
            _ =>
            {
                nextCalled = true;
                currentTenant.Id.Should().Be("tenant-a");
                return Task.CompletedTask;
            },
            NullLogger<MultiTenancyMiddleware>.Instance);
        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider
        };

        await middleware.InvokeAsync(context, currentTenant, new StaticTenantResolver("tenant-a"));

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WithTenantMismatchAfterAuthentication_ReturnsForbidden()
    {
        var middleware = new TenantBoundaryMiddleware(
            _ => Task.CompletedTask,
            NullLogger<TenantBoundaryMiddleware>.Instance);
        var currentTenant = new FakeCurrentTenant("tenant-a");
        var currentUser = new FakeCurrentUser("tenant-b", isAuthenticated: true, isSuperAdmin: false);
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "trace-tenant-boundary"
        };
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-1")
        }, "Test"));

        await middleware.InvokeAsync(context, currentTenant, currentUser);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    private sealed class StaticTenantResolver : ITenantResolver
    {
        private readonly string? _tenantId;

        public StaticTenantResolver(string? tenantId)
        {
            _tenantId = tenantId;
        }


        public async Task<TenantResolutionResult> ResolveAsync(HttpContext httpContext)
        {
            return TenantResolutionResult.Success(
                _tenantId,
                "tenantId",
                null,
                "StaticTenantResolver");
        }
    }

    private sealed class StaticTenantProvider : ITenantProvider
    {
        private readonly string _tenantId;

        public StaticTenantProvider(string tenantId)
        {
            _tenantId = tenantId;
        }

        public Task<ITenantInfo> GetTenantAsync(string tenantId, System.Threading.CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ITenantInfo>(new TenantInfo(_tenantId, _tenantId));
        }
    }

    private sealed class NullTenantProvider : ITenantProvider
    {
        public Task<ITenantInfo> GetTenantAsync(string tenantId, System.Threading.CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ITenantInfo>(null!);
        }
    }

    private sealed class FakeCurrentTenant : ICurrentTenant
    {
        public FakeCurrentTenant(string tenantId)
        {
            Id = tenantId;
            Tenant = new TenantInfo(tenantId, tenantId);
        }

        public ITenantInfo Tenant { get; }
        public string Id { get; }
        public IDisposable Change(string tenantId) => throw new NotSupportedException();
        public void SetTenantId(string tenantId) => throw new NotSupportedException();
    }

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public FakeCurrentUser(string tenantId, bool isAuthenticated, bool isSuperAdmin)
        {
            TenantId = tenantId;
            IsAuthenticated = isAuthenticated;
            IsSuperAdmin = isSuperAdmin;
        }

        public string Id => "user-1";
        public string UserName => "tester";
        public bool IsAuthenticated { get; }
        public string TenantId { get; }
        public string[] Roles => Array.Empty<string>();
        public Guid? OrganizationId => null;
        public IReadOnlyList<Guid> OrganizationIds => Array.Empty<Guid>();
        public int DataScopeValue => 0;
        public bool IsSuperAdmin { get; }
        public string FindClaimValue(string claimType) => string.Empty;
        public string[] FindClaimValues(string claimType) => Array.Empty<string>();
        public bool IsInRole(string roleName) => false;
        public bool IsInOrganization(Guid orgId) => false;
    }
}
