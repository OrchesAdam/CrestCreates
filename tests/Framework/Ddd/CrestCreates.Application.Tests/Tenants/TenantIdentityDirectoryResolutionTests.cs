using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Caching;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Infrastructure.Authorization;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.MultiTenancy.Middleware;
using CrestCreates.MultiTenancy.Resolvers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Tenants;

/// <summary>
/// #131 TI-B01: the directory may be addressed by name, but everything downstream — context frame,
/// identity claims, cache partition — must carry the resolved entity Id. Renaming a tenant must not move
/// a partition, and a name that looks like a Guid must never be substituted for a missing Id.
/// </summary>
public class TenantIdentityDirectoryResolutionTests : IDisposable
{
    private static readonly Guid FirstId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid InactiveId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>A name that is itself a Guid string: the trap where "looks like an id" must not become an id.</summary>
    private static readonly Guid GuidLookingName = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly Guid GuidNamedId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly Tenant _first;
    private readonly Tenant _second;
    private readonly Tenant _inactive;
    private readonly Tenant _guidNamed;
    private readonly Mock<ITenantRepository> _repository = new();
    private readonly ServiceProvider _provider;
    private readonly TenantCacheKeyContributor _cacheKeys = new();
    private readonly IdentityClaimsBuilder _claims = new();

    public TenantIdentityDirectoryResolutionTests()
    {
        _first = Active(Guid.NewGuid(), "acme-east");
        _second = Active(Guid.NewGuid(), "acme-east-two");
        _inactive = new Tenant(InactiveId, "acme-off") { IsActive = false, LifecycleState = TenantLifecycleState.Suspended };
        _guidNamed = Active(GuidNamedId, GuidLookingName.ToString("D"));

        SetupLookup(_first);
        SetupLookup(_second);
        SetupLookup(_inactive);
        SetupLookup(_guidNamed);

        _provider = new ServiceCollection()
            .AddSingleton(_repository.Object)
            .AddSingleton<ITenantProvider>(new DirectoryTenantProvider(_first, _second, _inactive, _guidNamed))
            .AddSingleton<ICurrentTenant, CurrentTenant>()
            .BuildServiceProvider();
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task HeaderGuid_ResolvesEntityId_AndKeepsNameAsDisplayOnly()
    {
        var result = await ResolveAsync(_first.Id.ToString("D"));

        result.IsResolved.Should().BeTrue();
        result.TenantId.Should().Be(_first.Id.ToString());
        result.TenantName.Should().Be(_first.Name);
        result.ResolvedBy.Should().Be("Header");

        IdentitySlots(result).Should().AllSatisfy(value => value.Should().Be(_first.Id.ToString()));
        _cacheKeys.GetTenantCacheKey(result.TenantId, "Audit").Should().Be($"Tenant:{_first.Id}:Audit");
    }

    [Fact]
    public async Task HeaderName_ResolvesDirectoryEntity_ThenUsesItsIdForIdentity()
    {
        var result = await ResolveAsync(_first.Name);

        result.IsResolved.Should().BeTrue("the name is a legal lookup alias");
        result.TenantId.Should().NotBe(_first.Name);
        result.TenantId.Should().Be(_first.Id.ToString());
        IdentitySlots(result).Should().AllSatisfy(value => value.Should().Be(_first.Id.ToString()));
        _cacheKeys.GetTenantCacheKey(result.TenantId, "Audit").Should().NotContain(_first.Name);
    }

    [Fact]
    public async Task NameThatLooksLikeAGuid_IsResolvedByIdLookup_OrFailsClosedWithoutSubstitutingTheName()
    {
        // The header value parses as a Guid, so the directory is addressed by id. Only the tenant whose
        // *name* is that Guid string matches by name, and the resolver must not fall back to name lookup
        // after a failed id lookup — that would silently turn a name into an identity.
        var result = await ResolveAsync(GuidLookingName.ToString("D"));

        result.IsResolved.Should().BeFalse();
        result.TenantId.Should().BeNull();
        result.Error!.Code.Should().Be("TENANT_NOT_FOUND");

        // The same directory is still reachable by its own id, and then identity equals that id.
        var byId = await ResolveAsync(GuidNamedId.ToString("D"));
        byId.IsResolved.Should().BeTrue();
        byId.TenantId.Should().Be(GuidNamedId.ToString());
        byId.TenantName.Should().Be(GuidLookingName.ToString("D"));
    }

    [Fact]
    public async Task Rename_LeavesIdentityAndPartitionUntouched()
    {
        var before = await ResolveAsync(_first.Name);
        var beforeKey = _cacheKeys.GetTenantCacheKey(before.TenantId, "Audit");

        var renamed = Active(_first.Id, "acme-renamed");
        SetupLookup(renamed);
        var after = await ResolveAsync("acme-renamed");

        after.TenantId.Should().Be(before.TenantId);
        after.TenantName.Should().Be("acme-renamed");
        _cacheKeys.GetTenantCacheKey(after.TenantId, "Audit").Should().Be(beforeKey);
        beforeKey.Should().NotContain("acme-east").And.NotContain("acme-renamed");
    }

    [Fact]
    public async Task SimilarNames_ProduceDistinctIdPartitions()
    {
        var first = await ResolveAsync(_first.Name);
        var second = await ResolveAsync(_second.Name);

        first.TenantId.Should().NotBe(second.TenantId);
        _cacheKeys.GetTenantCacheKey(first.TenantId, "Audit")
            .Should().NotBe(_cacheKeys.GetTenantCacheKey(second.TenantId, "Audit"));
        _cacheKeys.GetPermissionCacheKey(first.TenantId, "Role", "admin")
            .Should().StartWith($"Tenant:{first.TenantId}:");
    }

    [Fact]
    public async Task InactiveTenant_StopsBeforeAnyIdentitySlot()
    {
        var result = await ResolveAsync(_inactive.Name);

        result.IsResolved.Should().BeFalse();
        result.TenantId.Should().BeNull();
        result.Error!.Code.Should().Be("TENANT_INACTIVE");
    }

    [Fact]
    public async Task Middleware_EnterstheResolvedIdFrame_NotTheName()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/orders";
        context.Request.Headers["X-Tenant-Id"] = _first.Name;

        var currentTenant = _provider.GetRequiredService<ICurrentTenant>();
        var middleware = new MultiTenancyMiddleware(
            _ =>
            {
                currentTenant.Id.Should().Be(_first.Id.ToString());
                currentTenant.Tenant!.Id.Should().Be(_first.Id.ToString());
                currentTenant.Tenant.Name.Should().Be(_first.Name);
                return Task.CompletedTask;
            },
            NullLogger<MultiTenancyMiddleware>.Instance);

        await middleware.InvokeAsync(context, currentTenant, CreateResolver());

        context.Response.StatusCode.Should().Be(200);
        currentTenant.Id.Should().BeNull("the frame is restored on exit");
    }

    private System.Collections.Generic.IReadOnlyList<string> IdentitySlots(TenantResolutionResult resolution)
    {
        var claims = _claims.Build(new IdentityClaimsContext
        {
            UserId = Guid.NewGuid(),
            UserName = resolution.TenantName!,
            Email = "ops@example.test",
            TenantId = resolution.TenantId,
            IsSuperAdmin = false,
            Roles = Array.Empty<string>(),
            Permissions = Array.Empty<string>(),
        });

        return
        [
            resolution.TenantId!,
            claims.Single(claim => claim.Type == "tenantid").Value,
            claims.Single(claim => claim.Type == "tenant_id").Value,
        ];
    }

    private async Task<TenantResolutionResult> ResolveAsync(string headerValue)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant-Id"] = headerValue;
        return await CreateResolver().ResolveAsync(context);
    }

    private HeaderTenantResolver CreateResolver() =>
        new(
            Options.Create(new MultiTenancyOptions()),
            _provider.GetRequiredService<IServiceScopeFactory>(),
            new TenantIdentifierNormalizer(),
            NullLogger<HeaderTenantResolver>.Instance);

    private void SetupLookup(Tenant tenant)
    {
        _repository.Setup(r => r.FindByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        _repository
            .Setup(r => r.FindByNameAsync(new TenantIdentifierNormalizer().NormalizeName(tenant.Name), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
    }

    private static Tenant Active(Guid id, string name) =>
        new(id, name) { IsActive = true, LifecycleState = TenantLifecycleState.Active };

    private sealed class DirectoryTenantProvider(params Tenant[] tenants) : ITenantProvider
    {
        public Task<ITenantInfo?> GetTenantAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            var match = tenants.FirstOrDefault(tenant => string.Equals(tenant.Id.ToString(), tenantId, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult<ITenantInfo?>(match is null
                ? null
                : new TenantInfo(match.Id.ToString(), match.Name, match.GetDefaultConnectionString()));
        }
    }
}
