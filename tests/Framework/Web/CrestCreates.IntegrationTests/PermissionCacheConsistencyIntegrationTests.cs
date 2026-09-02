using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Caching;
using CrestCreates.Caching.Abstractions;
using CrestCreates.Data.EFCore.Repositories;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Domain.DataFilter;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.Permissions;
using FluentAssertions;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace CrestCreates.IntegrationTests;

/// <summary>
/// Real PostgreSQL/EF Core coverage for the direct-authority permission
/// cutover. Independent service scopes represent independent application
/// instances while sharing only the committed database authority.
/// </summary>
public sealed class PermissionCacheConsistencyIntegrationTests
    : IClassFixture<LibraryManagementWebApplicationFactory>
{
    private readonly LibraryManagementWebApplicationFactory _factory;

    public PermissionCacheConsistencyIntegrationTests(LibraryManagementWebApplicationFactory factory)
    {
        _factory = factory;
        _ = _factory.CreateClient();
        _factory.EnsureSeedCompleteAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task CommittedRevoke_Should_BeObservedByIndependentPermissionScopes()
    {
        var providerKey = $"role-{Guid.NewGuid():N}";
        var permissionName = $"Tests.Permission.{Guid.NewGuid():N}";
        var grant = Grant(permissionName, providerKey);
        await InsertThroughRepositoryAsync(grant);

        using var instanceA = _factory.Services.CreateScope();
        var storeA = instanceA.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        (await storeA.GetGrantedPermissionsAsync(PermissionGrantProviderType.Role, providerKey, null))
            .Should().Contain(permissionName);

        using (var writer = _factory.Services.CreateScope())
        {
            var repository = writer.ServiceProvider.GetRequiredService<IPermissionGrantRepository>();
            var persisted = await repository.FindAsync(
                permissionName,
                PermissionGrantProviderType.Role,
                providerKey,
                PermissionGrantScope.Global,
                null);
            persisted.Should().NotBeNull();
            await repository.DeleteAsync(persisted!);
        }

        using var instanceB = _factory.Services.CreateScope();
        var storeB = instanceB.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        (await storeB.GetGrantedPermissionsAsync(PermissionGrantProviderType.Role, providerKey, null))
            .Should().NotContain(permissionName);
        (await storeA.GetGrantedPermissionsAsync(PermissionGrantProviderType.Role, providerKey, null))
            .Should().NotContain(permissionName);
    }

    [Fact]
    public async Task DirectRepositoryCommit_Should_BeObservedWithoutCacheInvalidation()
    {
        var providerKey = $"role-{Guid.NewGuid():N}";
        var permissionName = $"Tests.Permission.{Guid.NewGuid():N}";

        using var reader = _factory.Services.CreateScope();
        var store = reader.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        (await store.GetGrantedPermissionsAsync(PermissionGrantProviderType.Role, providerKey, null))
            .Should().NotContain(permissionName);

        await InsertThroughRepositoryAsync(Grant(permissionName, providerKey));

        (await store.GetGrantedPermissionsAsync(PermissionGrantProviderType.Role, providerKey, null))
            .Should().Contain(permissionName);
    }

    [Fact]
    public async Task RetiredPositiveCacheEntry_Should_NotAuthorizeMissingAuthorityGrant()
    {
        var providerKey = $"role-{Guid.NewGuid():N}";
        var permissionName = $"Tests.Permission.{Guid.NewGuid():N}";

        using (var cacheScope = _factory.Services.CreateScope())
        {
            var cache = cacheScope.ServiceProvider.GetRequiredService<ICrestCacheService>();
            await cache.SetAsync(
                "Authorization.PermissionGrant",
                new List<PermissionGrantInfo>
                {
                    new()
                    {
                        PermissionName = permissionName,
                        ProviderType = PermissionGrantProviderType.Role,
                        ProviderKey = providerKey,
                        Scope = PermissionGrantScope.Global
                    }
                },
                $"{PermissionGrantProviderType.Role}:{providerKey}");
        }

        using var authorityScope = _factory.Services.CreateScope();
        var store = authorityScope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        (await store.GetGrantedPermissionsAsync(PermissionGrantProviderType.Role, providerKey, null))
            .Should().NotContain(permissionName);
    }

    [Fact]
    public async Task TenantAndGlobalGrants_Should_PreserveExactEfScopeFiltering()
    {
        var providerKey = $"role-{Guid.NewGuid():N}";
        var globalPermission = $"Tests.Permission.Global.{Guid.NewGuid():N}";
        var tenantPermission = $"Tests.Permission.Tenant.{Guid.NewGuid():N}";

        await InsertThroughRepositoryAsync(Grant(
            globalPermission,
            providerKey,
            PermissionGrantScope.Global,
            tenantId: null));
        await InsertThroughRepositoryAsync(Grant(
            tenantPermission,
            providerKey,
            PermissionGrantScope.Tenant,
            tenantId: "tenant-a"));

        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();

        (await store.GetGrantedPermissionsAsync(
                PermissionGrantProviderType.Role,
                providerKey,
                tenantId: "tenant-a"))
            .Should().BeEquivalentTo(new[] { globalPermission, tenantPermission });
        (await store.GetGrantedPermissionsAsync(
                PermissionGrantProviderType.Role,
                providerKey,
                tenantId: "tenant-b"))
            .Should().Equal(globalPermission);
        (await store.GetGrantedPermissionsAsync(
                PermissionGrantProviderType.Role,
                providerKey,
                tenantId: null))
            .Should().Equal(globalPermission);
    }

    [Fact]
    public async Task PermissionCacheBackendFailure_Should_NotAffectDirectEfAuthority()
    {
        var providerKey = $"role-{Guid.NewGuid():N}";
        var permissionName = $"Tests.Permission.CacheFailure.{Guid.NewGuid():N}";
        await InsertThroughRepositoryAsync(Grant(permissionName, providerKey));

        using var failingCacheFactory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICrestCacheService>();
                services.AddSingleton<ICrestCacheService>(sp =>
                    new PermissionPrefixThrowingCacheService(
                        new CrestCacheService(
                            sp.GetRequiredService<ICrestCache>(),
                            sp.GetRequiredService<ICrestCacheKeyGenerator>())));
            }));

        using var scope = failingCacheFactory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();

        (await store.GetGrantedPermissionsAsync(PermissionGrantProviderType.Role, providerKey, null))
            .Should().Contain(permissionName,
                "the retired Permission cache must not participate in the direct EF authority read path");
    }

    [Fact]
    public async Task PermissionRepositoryFailure_Should_FailClosedWithoutStaleCacheFallback()
    {
        var providerKey = $"role-{Guid.NewGuid():N}";
        var permissionName = $"Tests.Permission.RepositoryFailure.{Guid.NewGuid():N}";

        using var failingRepositoryFactory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPermissionGrantRepository>();
                services.AddScoped<IPermissionGrantRepository, ThrowingPermissionGrantRepository>();
            }));

        using (var cacheScope = failingRepositoryFactory.Services.CreateScope())
        {
            var cache = cacheScope.ServiceProvider.GetRequiredService<ICrestCacheService>();
            await cache.SetAsync(
                "Authorization.PermissionGrant",
                new List<PermissionGrantInfo>
                {
                    new()
                    {
                        PermissionName = permissionName,
                        ProviderType = PermissionGrantProviderType.Role,
                        ProviderKey = providerKey,
                        Scope = PermissionGrantScope.Global
                    }
                },
                $"{PermissionGrantProviderType.Role}:{providerKey}");
        }

        using var authorityScope = failingRepositoryFactory.Services.CreateScope();
        var store = authorityScope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();

        await store.Invoking(value =>
                value.GetGrantedPermissionsAsync(PermissionGrantProviderType.Role, providerKey, null))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*injected EF permission repository read failure*");
    }

    [Fact]
    public async Task SuperAdmin_Should_BypassRepositoryAccess_InRealEfHost()
    {
        using var failingRepositoryFactory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPermissionGrantRepository>();
                services.AddScoped<IPermissionGrantRepository, ThrowingPermissionGrantRepository>();
            }));

        using var scope = failingRepositoryFactory.Services.CreateScope();
        var checker = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, $"super-{Guid.NewGuid():N}"),
            new Claim("is_super_admin", "true")
        ], authenticationType: "phase9d-test"));

        (await checker.IsGrantedAsync(principal, $"Tests.Permission.SuperAdmin.{Guid.NewGuid():N}"))
            .Should().BeTrue(
                "the existing SuperAdmin bypass must remain ahead of repository-backed authorization");
    }

    private async Task InsertThroughRepositoryAsync(PermissionGrant grant)
    {
        using var writer = _factory.Services.CreateScope();
        var repository = writer.ServiceProvider.GetRequiredService<IPermissionGrantRepository>();
        await repository.InsertAsync(grant);
    }

    private static PermissionGrant Grant(string permissionName, string providerKey)
        => Grant(permissionName, providerKey, PermissionGrantScope.Global, tenantId: null);

    private static PermissionGrant Grant(
        string permissionName,
        string providerKey,
        PermissionGrantScope scope,
        string? tenantId)
        => new(
            Guid.NewGuid(),
            permissionName,
            PermissionGrantProviderType.Role,
            providerKey,
            scope,
            tenantId);

    private sealed class ThrowingPermissionGrantRepository
        : PermissionGrantRepository, IPermissionGrantRepository
    {
        public ThrowingPermissionGrantRepository(
            IDataBaseContext dbContext,
            ICurrentTenant currentTenant,
            DataFilterState dataFilterState)
            : base(dbContext, currentTenant, dataFilterState)
        {
        }

        Task<List<PermissionGrant>> IPermissionGrantRepository.GetListByProviderAsync(
            PermissionGrantProviderType providerType,
            string providerKey,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("injected EF permission repository read failure");
    }

    private sealed class PermissionPrefixThrowingCacheService : ICrestCacheService
    {
        private const string PermissionPrefix = "Authorization.PermissionGrant";
        private readonly ICrestCacheService _inner;

        public PermissionPrefixThrowingCacheService(ICrestCacheService inner)
        {
            _inner = inner;
        }

        public Task<T?> GetAsync<T>(string prefix, params object[] parts)
        {
            RejectPermissionPrefix(prefix);
            return _inner.GetAsync<T>(prefix, parts);
        }

        public Task<T?> GetAsync<T>(string prefix, string? tenantId, params object[] parts)
        {
            RejectPermissionPrefix(prefix);
            return _inner.GetAsync<T>(prefix, tenantId, parts);
        }

        public Task SetAsync<T>(string prefix, T value, params object[] parts)
        {
            RejectPermissionPrefix(prefix);
            return _inner.SetAsync(prefix, value, parts);
        }

        public Task SetAsync<T>(string prefix, T value, TimeSpan expiration, params object[] parts)
        {
            RejectPermissionPrefix(prefix);
            return _inner.SetAsync(prefix, value, expiration, parts);
        }

        public Task SetAsync<T>(string prefix, string? tenantId, T value, params object[] parts)
        {
            RejectPermissionPrefix(prefix);
            return _inner.SetAsync(prefix, tenantId, value, parts);
        }

        public Task RemoveAsync(string prefix, params object[] parts)
        {
            RejectPermissionPrefix(prefix);
            return _inner.RemoveAsync(prefix, parts);
        }

        public Task RemoveByPatternAsync(string pattern)
        {
            RejectPermissionPrefix(pattern);
            return _inner.RemoveByPatternAsync(pattern);
        }

        public Task ClearAsync() => _inner.ClearAsync();

        public Task<bool> ExistsAsync(string prefix, params object[] parts)
        {
            RejectPermissionPrefix(prefix);
            return _inner.ExistsAsync(prefix, parts);
        }

        private static void RejectPermissionPrefix(string value)
        {
            if (value.Contains(PermissionPrefix, StringComparison.Ordinal))
                throw new InvalidOperationException("injected Permission cache backend failure");
        }
    }
}
