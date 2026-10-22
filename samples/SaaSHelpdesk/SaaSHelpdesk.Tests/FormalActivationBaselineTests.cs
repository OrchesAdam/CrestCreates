using System;
using System.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace SaaSHelpdesk.Tests;

/// <summary>
/// #129 S4 exit: the Helpdesk host composes only the formal AppService HTTP projection. The legacy
/// runtime/analyzer/Web activation packages are not part of the build graph or the running host.
/// </summary>
public class FormalActivationBaselineTests : BaseTest
{
    public FormalActivationBaselineTests(Fixtures.HelpdeskWebApplicationFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public void Legacy_assemblies_are_not_loaded_by_the_host()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetName().Name)
            .ToArray();

        loaded.Should().NotContain("CrestCreates.DynamicApi.Legacy");
        loaded.Should().NotContain("CrestCreates.Web.Legacy");
        loaded.Should().NotContain("CrestCreates.CodeGenerator.Legacy");
    }

    [Fact]
    public void Formal_contributions_publish_every_migrated_route_exactly_once()
    {
        var routes = Factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
                                     ?? Array.Empty<string>())
                .Select(method => method.ToUpperInvariant() + " /" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/')))
            .Where(route => route.Contains(" /api/", StringComparison.Ordinal))
            .ToArray();

        routes.Should().Contain(new[]
        {
            "POST /api/agent",
            "GET /api/agent/{id}",
            "GET /api/agent/deactivate",
            "POST /api/category",
            "GET /api/category/{id}",
            "GET /api/category/all",
            "PUT /api/category/{id}",
            "DELETE /api/category/{id}",
            "GET /api/category/tree",
            "GET /api/category/roots",
            "GET /api/category/children/{parentId}",
            "GET /api/category/move",
            "GET /api/category/reorder",
            "POST /api/customer",
            "GET /api/customer/{id}",
            "GET /api/customer/all",
            "PUT /api/customer/{id}",
            "DELETE /api/customer/{id}",
            "GET /api/customer/activate",
            "GET /api/customer/deactivate",
            "POST /api/customer-portal/create-ticket",
            "GET /api/customer-portal/my-tickets",
            "GET /api/customer-portal/ticket/{id}",
            "GET /api/dashboard/summary",
            "POST /api/knowledge-base",
            "GET /api/knowledge-base/{id}",
            "GET /api/knowledge-base/all",
            "PUT /api/knowledge-base/{id}",
            "DELETE /api/knowledge-base/{id}",
            "GET /api/knowledge-base/publish",
            "GET /api/knowledge-base/unpublish",
            "GET /api/knowledge-base/increment-view-count",
            "POST /api/knowledge-base/search",
            "GET /api/knowledge-base/popular",
            "POST /api/s-l-a-policy",
            "GET /api/s-l-a-policy/{id}",
            "GET /api/s-l-a-policy/all",
            "PUT /api/s-l-a-policy/{id}",
            "DELETE /api/s-l-a-policy/{id}",
        });

        routes.Should().OnlyHaveUniqueItems("each migrated route is published once by the formal contribution");
    }
}
