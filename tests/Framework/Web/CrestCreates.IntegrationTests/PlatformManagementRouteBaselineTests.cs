using System.Linq;
using CrestCreates.DynamicApi;
using CrestCreates.Web.AuditRetention;
using CrestCreates.Web.PlatformManagement;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrestCreates.IntegrationTests;

/// <summary>
/// G-09: the platform management HTTP surface is published through the formal ExplicitHost
/// projection (clean REST). Locks the route matrix and the per-assembly aggregate activation
/// idempotency without needing a database.
/// </summary>
public class PlatformManagementRouteBaselineTests
{
    private static string[] ActivateRoutes()
    {
        var services = new ServiceCollection();
        services.AddCrestPlatformManagementHttp();
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
    }

    [Fact]
    public void Aggregate_activation_is_idempotent_across_platform_and_retention()
    {
        var once = ActivateRoutes();

        var services = new ServiceCollection();
        services.AddCrestPlatformManagementHttp();
        services.AddCrestPlatformManagementHttp();
        services.AddCrestAuditRetentionHttp();
        using var provider = services.BuildServiceProvider();
        var twice = provider.GetServices<CapabilityEndpointContribution>()
            .SelectMany(contribution => contribution.EndpointDescriptors)
            .Select(endpoint => $"{endpoint.HttpMethod} {endpoint.RoutePattern.TrimStart('/')}")
            .ToArray();

        twice.Should().BeEquivalentTo(once, "repeated activation and the retention entry share one idempotent aggregate");
        twice.Should().OnlyHaveUniqueItems();
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