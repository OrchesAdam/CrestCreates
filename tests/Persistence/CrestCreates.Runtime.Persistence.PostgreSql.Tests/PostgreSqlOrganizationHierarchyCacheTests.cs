using CrestCreates.Organization;
using CrestCreates.Organization.Abstractions;
using CrestCreates.Runtime.Persistence.PostgreSql;
using CrestCreates.Runtime.Persistence.PostgreSql.Tests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.Runtime.Persistence.PostgreSql.Tests;

[Collection(PostgreSqlRuntimeCollection.Name)]
public sealed class PostgreSqlOrganizationHierarchyCacheTests : IAsyncLifetime
{
    private readonly PostgreSqlRuntimeCollectionFixture _fixture;
    private PostgreSqlRuntimeSchemaLease _lease = null!;
    private ServiceProvider _providerA = null!;
    private ServiceProvider _providerB = null!;

    public PostgreSqlOrganizationHierarchyCacheTests(PostgreSqlRuntimeCollectionFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _lease = await _fixture.CreateSchemaLeaseAsync();
        _providerA = BuildProvider(_lease.Options);
        _providerB = BuildProvider(_lease.Options);
    }

    public async Task DisposeAsync()
    {
        await _providerA.DisposeAsync();
        await _providerB.DisposeAsync();
        await _lease.DisposeAsync();
    }

    private static ServiceProvider BuildProvider(PostgreSqlRuntimePersistenceOptions options)
    {
        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddCrestCreatesPostgreSqlRuntimePersistence(options);
        services.AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence();
        services.AddOrganizationKernel();
        return services.BuildServiceProvider();
    }

    private static OrganizationUnit Unit(string id, string? tenantId, string? parentId = null)
        => new() { Id = id, TenantId = tenantId, Name = id, ParentId = parentId };

    /// <summary>
    /// OMI01: both instances miss all events — both reject V1 through PostgreSQL G2.
    /// </summary>
    [Fact]
    public async Task MultiInstance_HierarchyCache_Should_ValidateSharedAuthorityGeneration()
    {
        var storeA = _providerA.GetRequiredService<IOrganizationStore>();
        var storeB = _providerB.GetRequiredService<IOrganizationStore>();
        var hierarchyA = _providerA.GetRequiredService<IOrganizationHierarchyService>();
        var hierarchyB = _providerB.GetRequiredService<IOrganizationHierarchyService>();

        // Save V1
        await storeA.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));
        await storeA.SaveOrganizationUnitAsync(Unit("child-v1", "tenant-a", "root"));

        // Both instances load V1
        var descA1 = await hierarchyA.GetDescendantsAsync("root", "tenant-a");
        var descB1 = await hierarchyB.GetDescendantsAsync("root", "tenant-a");
        descA1.Select(d => d.Id).Should().Equal("child-v1");
        descB1.Select(d => d.Id).Should().Equal("child-v1");

        // Save V2 through store A only (no event)
        await storeA.SaveOrganizationUnitAsync(Unit("child-v2", "tenant-a", "root"));

        // Both instances should reject V1 and return V2
        var descA2 = await hierarchyA.GetDescendantsAsync("root", "tenant-a");
        var descB2 = await hierarchyB.GetDescendantsAsync("root", "tenant-a");
        descA2.Select(d => d.Id).Should().Equal("child-v1", "child-v2");
        descB2.Select(d => d.Id).Should().Equal("child-v1", "child-v2");
    }

    /// <summary>
    /// OMI02: independent local caches — correctness does not require cache sharing.
    /// </summary>
    [Fact]
    public async Task MultiInstance_HierarchyCache_Should_NotRequireSharedCacheOrEvent()
    {
        var storeA = _providerA.GetRequiredService<IOrganizationStore>();
        var storeB = _providerB.GetRequiredService<IOrganizationStore>();
        var hierarchyA = _providerA.GetRequiredService<IOrganizationHierarchyService>();
        var hierarchyB = _providerB.GetRequiredService<IOrganizationHierarchyService>();

        // Save V1
        await storeA.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));
        await storeA.SaveOrganizationUnitAsync(Unit("child-v1", "tenant-a", "root"));

        // Only instance A loads V1
        var descA1 = await hierarchyA.GetDescendantsAsync("root", "tenant-a");
        descA1.Select(d => d.Id).Should().Equal("child-v1");

        // Save V2 through store B
        await storeB.SaveOrganizationUnitAsync(Unit("child-v2", "tenant-a", "root"));

        // Instance B should see V2 (its cache is independent)
        var descB = await hierarchyB.GetDescendantsAsync("root", "tenant-a");
        descB.Select(d => d.Id).Should().Equal("child-v1", "child-v2");

        // Instance A should also see V2 (through generation validation)
        var descA2 = await hierarchyA.GetDescendantsAsync("root", "tenant-a");
        descA2.Select(d => d.Id).Should().Equal("child-v1", "child-v2");
    }

    /// <summary>
    /// Tenant isolation: Save in tenant A cannot advance tenant B's generation.
    /// </summary>
    [Fact]
    public async Task MultiInstance_TenantGeneration_Should_Isolate()
    {
        var storeA = _providerA.GetRequiredService<IOrganizationStore>();
        var hierarchyA = _providerA.GetRequiredService<IOrganizationHierarchyService>();
        var hierarchyB = _providerB.GetRequiredService<IOrganizationHierarchyService>();

        // Save in tenant-a
        await storeA.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));

        // Load tenant-a
        var descA = await hierarchyA.GetDescendantsAsync("root", "tenant-a");
        descA.Should().BeEmpty();

        // tenant-b should still be at generation 0
        var identityStore = _providerB.GetRequiredService<IOrganizationStore>();
        var genB = await identityStore.ReadScopeGenerationAsync(OrganizationScopeIdentity.Tenant("tenant-b"));
        genB.Generation.Should().Be(0);
    }

    /// <summary>
    /// Null tenant bypass against real PostgreSQL without retaining a cross-tenant entry.
    /// </summary>
    [Fact]
    public async Task MultiInstance_NullTenant_Should_BypassCache()
    {
        var storeA = _providerA.GetRequiredService<IOrganizationStore>();
        var hierarchyA = _providerA.GetRequiredService<IOrganizationHierarchyService>();

        await storeA.SaveOrganizationUnitAsync(Unit("root", "tenant-a"));
        await storeA.SaveOrganizationUnitAsync(Unit("global-root", null));

        // Null tenant should bypass cache
        var ancestors = await hierarchyA.GetAncestorsAsync("root", null);
        ancestors.Should().BeEmpty();
    }
}
