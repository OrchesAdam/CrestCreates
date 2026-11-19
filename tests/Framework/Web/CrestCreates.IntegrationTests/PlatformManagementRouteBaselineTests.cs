using System;
using System.Linq;
using CrestCreates.Accountability.Abstractions.Retention;
using CrestCreates.Accountability.Retention;
using CrestCreates.DynamicApi;
using CrestCreates.Web.AuditRetention;
using CrestCreates.Web.PlatformManagement;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CrestCreates.IntegrationTests;

/// <summary>
/// G-09: the platform management HTTP surface is published through the formal ExplicitHost
/// projection. Locks the route matrix and the platform and retention activation boundaries
/// without needing a database.
/// </summary>
public class PlatformManagementRouteBaselineTests
{
    private static string[] ActivateRoutes()
    {
        var services = new ServiceCollection();
        services.AddCrestPlatformManagementHttp();
        return GetRoutes(services);
    }

    private static string[] GetRoutes(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        return provider.GetServices<CapabilityEndpointContribution>()
            .SelectMany(contribution => contribution.EndpointDescriptors)
            .Select(endpoint => $"{endpoint.HttpMethod} {endpoint.RoutePattern.TrimStart('/')}")
            .ToArray();
    }

    [Fact]
    public void Platform_management_publishes_clean_rest_matrix()
    {
        var routes = ActivateRoutes();

        routes.Should().Contain(new[]
        {
            // User
            "Post api/users",
            "Get api/users/{userId}",
            "Get api/users",
            "Put api/users/{userId}",
            "Patch api/users/{userId}/active",
            "Post api/users/{userId}/change-password",
            "Post api/users/{userId}/roles/{roleId}",
            "Delete api/users/{userId}/roles/{roleId}",
            "Get api/users/{userId}/roles",
            // Role
            "Post api/roles",
            "Get api/roles/{roleId}",
            "Put api/roles/{roleId}",
            "Patch api/roles/{roleId}/active",
            // PermissionGrant (writes are POST/DELETE + body, not GET)
            "Post api/permission-grants/user/{userId}",
            "Post api/permission-grants/role/{roleName}",
            "Delete api/permission-grants/user/{userId}",
            "Get api/permission-grants/user/{userId}",
            "Get api/permission-grants/effective/{userId}",
            // Setting
            "Get api/settings/current",
            "Get api/settings/global",
            "Get api/settings/current-tenant",
            "Put api/settings/global/{name}",
            "Put api/settings/current-tenant/{name}",
            "Delete api/settings/current-tenant/{name}",
            // SettingDefinition
            "Get api/setting-definitions/groups",
            "Get api/setting-definitions",
            "Get api/setting-definitions/{name}",
            // Feature (writes are PUT/DELETE, not GET)
            "Get api/features/global",
            "Get api/features/current-tenant",
            "Put api/features/global/{name}",
            "Put api/features/tenant/{tenantId}/{name}",
            "Delete api/features/tenant/{tenantId}/{name}",
            "Get api/features/enabled/{featureName}",
            "Get api/features/tenant/{tenantId}/enabled/{featureName}",
            // FeatureDefinition
            "Get api/feature-definitions",
            "Get api/feature-definitions/groups",
            // Tenant
            "Post api/tenants",
            "Get api/tenants/{name}",
            "Get api/tenants",
            "Put api/tenants/{name}",
            "Patch api/tenants/{name}/active",
            "Get api/tenants/{tenantId}/initialization-status",
            "Post api/tenants/{tenantId}/retry-initialization",
            // AuditLog (flattened query read)
            "Get api/audit-logs",
        });

        routes.Should().OnlyHaveUniqueItems("each platform route is published once by the formal contribution");
        routes.Should().NotContain(route => route.Contains("api/audit-retention", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Retention_activation_requires_both_kernel_and_store(bool kernel, bool store)
    {
        var services = new ServiceCollection();
        if (kernel)
        {
            services.AddAuditRetention();
        }

        if (store)
        {
            services.AddSingleton(Mock.Of<IAuditRetentionStore>());
        }

        Action activate = () => services.AddCrestAuditRetentionHttp();
        activate.Should().Throw<InvalidOperationException>().WithMessage("*IAuditRetentionService*IAuditRetentionStore*");
        GetRoutes(services).Should().BeEmpty();
    }

    [Fact]
    public void Retention_activation_adds_only_its_routes_and_both_activations_are_idempotent()
    {
        var services = new ServiceCollection();
        services.AddAuditRetention();
        services.AddSingleton(Mock.Of<IAuditRetentionStore>());
        GetRoutes(services).Should().BeEmpty();

        var retentionRoutes = new[]
        {
            "Post api/audit-retention/preview",
            "Post api/audit-retention/execute",
            "Get api/audit-retention/run",
            "Post api/audit-retention/batches/export",
            "Post api/audit-retention/holds",
            "Post api/audit-retention/holds/release",
        };
        services.AddCrestAuditRetentionHttp();
        GetRoutes(services).Should().BeEquivalentTo(retentionRoutes);

        services.AddCrestPlatformManagementHttp();
        services.AddCrestPlatformManagementHttp();
        services.AddCrestAuditRetentionHttp();

        GetRoutes(services).Should().BeEquivalentTo(ActivateRoutes().Concat(retentionRoutes));
    }

    [Fact]
    public void Legacy_platform_routes_are_not_published()
    {
        var routes = ActivateRoutes();

        // The retired GET-for-write legacy forms must not reappear.
        routes.Should().NotContain(route => route.Contains("api/permission-grant/grant-to-user"));
        routes.Should().NotContain(route => route.Contains("api/feature/set-global"));
        routes.Should().NotContain(route => route.Contains("api/setting/update-current-tenant"));
        routes.Should().NotContain(route => route.StartsWith("Get api/audit-log?"));
        routes.Should().NotContain(route => route == "Get api/audit-log");
    }
}