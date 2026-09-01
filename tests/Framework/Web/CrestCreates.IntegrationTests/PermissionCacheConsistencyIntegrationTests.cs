using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Caching;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.Permissions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
}
