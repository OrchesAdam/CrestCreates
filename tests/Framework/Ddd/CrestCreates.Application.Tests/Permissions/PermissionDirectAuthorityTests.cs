using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Authorization;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Permission;
using CrestCreates.Domain.Repositories.Permission;
using CrestCreates.Domain.Shared.Permissions;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Permissions;

/// <summary>
/// Permission direct-authority cutover tests (PSC01-PSC02, PSC04-PSC06, PSC08).
/// Verifies production reads no longer depend on the retired unversioned
/// positive grant cache.
/// </summary>
public class PermissionDirectAuthorityTests
{
    private static PermissionGrantStore StoreWithRepository(IPermissionGrantRepository repository)
        => new(repository);

    private static IPermissionGrantRepository RepositoryWithGrants(params PermissionGrant[] grants)
    {
        var mock = new Mock<IPermissionGrantRepository>();
        mock.Setup(r => r.GetListByProviderAsync(
                It.IsAny<PermissionGrantProviderType>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(grants.ToList());
        return mock.Object;
    }

    private static PermissionGrant Grant(string name, string providerKey, PermissionGrantScope scope, string? tenantId = null)
        => new(Guid.NewGuid(), name, PermissionGrantProviderType.Role, providerKey, scope, tenantId);

    /// <summary>
    /// PSC01: PermissionGrantStore queries repository on every call (no cache).
    /// </summary>
    [Fact]
    public async Task PermissionGrantStore_Should_ReadAuthority_WithoutCache()
    {
        var repoMock = new Mock<IPermissionGrantRepository>();
        repoMock.Setup(r => r.GetListByProviderAsync(
                PermissionGrantProviderType.Role,
                "Librarian",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PermissionGrant> { Grant("Books.Delete", "Librarian", PermissionGrantScope.Global) });

        var store = StoreWithRepository(repoMock.Object);

        var grants1 = await store.GetGrantsAsync(PermissionGrantProviderType.Role, "Librarian");
        var grants2 = await store.GetGrantsAsync(PermissionGrantProviderType.Role, "Librarian");

        grants1.Should().HaveCount(1);
        grants2.Should().HaveCount(1);

        // Repository must be queried every time (no caching)
        repoMock.Verify(r => r.GetListByProviderAsync(
            PermissionGrantProviderType.Role,
            "Librarian",
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>
    /// PSC02: an old positive backend cache entry is ignored.
    /// </summary>
    [Fact]
    public async Task PermissionOldPositiveCache_Should_BeIgnored_AfterCutover()
    {
        var repoMock = new Mock<IPermissionGrantRepository>();
        repoMock.Setup(r => r.GetListByProviderAsync(
                PermissionGrantProviderType.Role,
                "Librarian",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PermissionGrant>());

        var store = StoreWithRepository(repoMock.Object);

        // Even if a stale cache entry existed, production reads authority directly
        var grants = await store.GetGrantsAsync(PermissionGrantProviderType.Role, "Librarian");
        grants.Should().BeEmpty();
    }

    /// <summary>
    /// PSC04: repository failure propagates and cannot fall back to a stale grant.
    /// </summary>
    [Fact]
    public async Task PermissionAuthorityFailure_Should_NotFallbackToStalePositive()
    {
        var repoMock = new Mock<IPermissionGrantRepository>();
        repoMock.Setup(r => r.GetListByProviderAsync(
                It.IsAny<PermissionGrantProviderType>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("repository unavailable"));

        var store = StoreWithRepository(repoMock.Object);

        await store.Invoking(s => s.GetGrantsAsync(PermissionGrantProviderType.Role, "Librarian"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*repository unavailable*");
    }

    /// <summary>
    /// PSC05: tenant and global filtering remains exact.
    /// </summary>
    [Fact]
    public async Task PermissionCutover_Should_PreserveTenantAndGlobalScopeFiltering()
    {
        var grants = new[]
        {
            Grant("Books.View", "Librarian", PermissionGrantScope.Global),
            Grant("Books.Edit", "Librarian", PermissionGrantScope.Tenant, "tenant-a"),
            Grant("Books.Admin", "Librarian", PermissionGrantScope.Tenant, "tenant-b"),
        };

        var store = StoreWithRepository(RepositoryWithGrants(grants));

        // Global tenant sees global + own tenant
        var tenantAPerms = await store.GetGrantedPermissionsAsync(PermissionGrantProviderType.Role, "Librarian", "tenant-a");
        tenantAPerms.Should().Equal("Books.Edit", "Books.View");

        // Other tenant sees global + own tenant
        var tenantBPerms = await store.GetGrantedPermissionsAsync(PermissionGrantProviderType.Role, "Librarian", "tenant-b");
        tenantBPerms.Should().Equal("Books.Admin", "Books.View");

        // Null tenant sees only global
        var globalPerms = await store.GetGrantedPermissionsAsync(PermissionGrantProviderType.Role, "Librarian", null);
        globalPerms.Should().Equal("Books.View");
    }

    /// <summary>
    /// PSC06: DI graph after cutover — Permission read path has no cache dependency.
    /// </summary>
    [Fact]
    public async Task AddCrestAuthorization_Should_NotCompose_UnversionedPermissionCachePath()
    {
        var services = new ServiceCollection();
        services.AddCrestAuthorization();

        // PermissionGrantCacheService must not be registered
        services.Should().NotContain(sd => sd.ServiceType.Name == "PermissionGrantCacheService",
            "PermissionGrantCacheService must be retired from production composition");

        // PermissionGrantStore must be registered
        services.Should().Contain(sd => sd.ServiceType == typeof(IPermissionGrantStore));

        // PermissionGrantManager must be registered
        services.Should().Contain(sd => sd.ServiceType == typeof(IPermissionGrantManager));
    }

    /// <summary>
    /// PSC08: Permission cache services are retired — the authorization graph
    /// remains constructible without the retired Permission cache services.
    /// </summary>
    [Fact]
    public async Task PermissionCacheRetirement_Should_Preserve_UnrelatedAuthorizationCachingConsumers()
    {
        var services = new ServiceCollection();
        services.AddCrestAuthorization();

        // The authorization core services must remain registered
        services.Should().Contain(sd => sd.ServiceType == typeof(IPermissionChecker));
        services.Should().Contain(sd => sd.ServiceType == typeof(IPermissionGrantStore));
        services.Should().Contain(sd => sd.ServiceType == typeof(IPermissionGrantManager));
        services.Should().Contain(sd => sd.ServiceType == typeof(IPermissionDefinitionManager));

        // Retired Permission cache services must NOT be registered
        services.Should().NotContain(sd => sd.ServiceType.Name == "PermissionGrantCacheService");
        services.Should().NotContain(sd => sd.ServiceType.Name == "PermissionGrantCacheOptions");
    }
}
