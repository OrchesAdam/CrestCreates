using System.Reflection;
using CrestCreates.Authorization;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Organization;
using CrestCreates.Organization.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

using AssemblyName = System.Reflection.AssemblyName;

namespace CrestCreates.DependencyBoundaries.Tests;

/// <summary>
/// Phase 9d architecture invariants — locks the unique Organization and
/// Permission mainlines and prevents future accidental restoration of
/// stale paths.
/// </summary>
public class VersionedCacheConsistencyArchitectureTests
{
    private static readonly Assembly OrganizationAssembly = typeof(IOrganizationHierarchyService).Assembly;
    private static readonly Assembly AuthorizationAssembly = typeof(IPermissionGrantStore).Assembly;

    [Fact]
    public void EveryOrganizationStore_Should_ImplementTypedGenerationRead()
    {
        var storeTypes = OrganizationAssembly.GetTypes()
            .Where(t => typeof(IOrganizationStore).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

        foreach (var type in storeTypes)
        {
            var method = type.GetMethod(nameof(IOrganizationStore.ReadScopeGenerationAsync));
            method.Should().NotBeNull($"concrete Store {type.Name} must implement ReadScopeGenerationAsync");
        }
    }

    [Fact]
    public void OrganizationAssembly_Should_NotReferenceFrameworkCachingOrRuntimeOrPersistence()
    {
        var referencedAssemblies = OrganizationAssembly.GetReferencedAssemblies();
        var names = referencedAssemblies.Select(a => a.Name).OrderBy(n => n).ToList();

        // Organization must not reference CrestCreates.Caching (framework caching module)
        names.Should().NotContain(n => n != null && n.StartsWith("CrestCreates.Caching"),
            $"Organization must not reference CrestCreates.Caching. Actual: [{string.Join(", ", names)}]");

        // Organization must not reference Runtime
        names.Should().NotContain(n => n != null && n.StartsWith("CrestCreates.Runtime"),
            $"Organization must not reference CrestCreates.Runtime. Actual: [{string.Join(", ", names)}]");

        // Organization must not reference concrete persistence (EFCore/FreeSql/etc)
        names.Should().NotContain(n => n != null && n.StartsWith("CrestCreates.Data."),
            $"Organization must not reference concrete persistence. Actual: [{string.Join(", ", names)}]");
    }

    [Fact]
    public void ProductionOrganizationDI_Should_ExposeOneHierarchyService()
    {
        var services = new ServiceCollection();
        services.AddOrganizationKernel();

        var hierarchyServices = services.Where(sd => sd.ServiceType == typeof(IOrganizationHierarchyService)).ToList();
        hierarchyServices.Should().HaveCount(1,
            "production DI must expose exactly one IOrganizationHierarchyService");

        // Cache owner must be registered (by type name since it is internal)
        services.Should().Contain(sd => sd.ServiceType.Name == "IOrganizationHierarchyCacheOwner",
            "production DI must register singleton IOrganizationHierarchyCacheOwner");
    }

    [Fact]
    public void PermissionGrantStoreConstructor_Should_HaveNoCacheDependency()
    {
        var ctors = typeof(PermissionGrantStore).GetConstructors();
        ctors.Should().HaveCount(1,
            "PermissionGrantStore must have exactly one constructor");

        var parameters = ctors[0].GetParameters();
        parameters.Should().HaveCount(1,
            "PermissionGrantStore must have exactly one constructor dependency (repository)");
        parameters[0].ParameterType.Name.Should().Be("IPermissionGrantRepository",
            "PermissionGrantStore must depend only on IPermissionGrantRepository");
    }

    [Fact]
    public void PermissionCacheServices_Should_BeAbsentFromProduction()
    {
        var services = new ServiceCollection();
        services.AddCrestAuthorization();

        services.Should().NotContain(sd => sd.ServiceType.Name == "PermissionGrantCacheService",
            "PermissionGrantCacheService must be retired");
        services.Should().NotContain(sd => sd.ServiceType.Name == "PermissionGrantCacheOptions",
            "PermissionGrantCacheOptions must be retired");
    }

    [Fact]
    public void AuthorizationModule_Should_NotDependOnCachingModule()
    {
        var services = new ServiceCollection();
        services.AddCrestAuthorization();

        // The authorization module must not register caching services
        // (caching is a separate concern, composed separately)
        services.Should().NotContain(sd => sd.ServiceType.Name == "TenantCacheKeyContributor",
            "TenantCacheKeyContributor must not be registered by authorization module");
        services.Should().NotContain(sd => sd.ServiceType.Name == "AuditTenantContextResolver",
            "AuditTenantContextResolver must not be registered by authorization module");
    }

    [Fact]
    public void CachedOrganizationHierarchyService_Should_UseCacheOwner()
    {
        // The cached service is registered in production DI via IOrganizationHierarchyCacheOwner.
        // Verify the DI registration wires the correct owner type.
        var services = new ServiceCollection();
        services.AddOrganizationKernel();

        var cacheOwnerDescriptor = services.FirstOrDefault(sd => sd.ServiceType.Name == "IOrganizationHierarchyCacheOwner");
        cacheOwnerDescriptor.Should().NotBeNull("IOrganizationHierarchyCacheOwner must be registered");

        // The cache owner must be a singleton (shared across scopes)
        cacheOwnerDescriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton,
            "IOrganizationHierarchyCacheOwner must be a singleton");
    }

    [Fact]
    public void DataPermissionScope_Should_NotBeCachedOrPersisted()
    {
        // DataPermissionScope is derived, not cached or persisted
        var services = new ServiceCollection();
        services.AddOrganizationKernel();

        services.Should().NotContain(sd => sd.ServiceType.Name == "IDataPermissionScopeStore",
            "DataPermissionScope must not have a store");
    }
}
