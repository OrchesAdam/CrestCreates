using CrestCreates.DynamicApi;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SaaSHelpdesk.Tests;

public sealed class ReviewS3TicketRoutingProbes(Fixtures.HelpdeskWebApplicationFactory factory) : BaseTest(factory)
{
    [Fact]
    public void Normalized_ticket_family_has_eleven_formal_endpoints_and_no_legacy_spelling_or_authorization_metadata()
    {
        var endpoints = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint =>
            {
                var path = endpoint.RoutePattern.RawText!.TrimStart('/');
                return path.Equals("api/ticket", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("api/ticket/", StringComparison.OrdinalIgnoreCase);
            }).ToArray();
        endpoints.Should().HaveCount(11);
        endpoints.Should().OnlyContain(endpoint => endpoint.DisplayName != null
            && endpoint.DisplayName.StartsWith("compat.appservice.ticket.", StringComparison.Ordinal));
        endpoints.SelectMany(endpoint => endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()).Should().BeEmpty();
        var declared = Factory.Services.GetRequiredService<ICapabilityEndpointRegistry>().GetAll()
            .Where(endpoint => endpoint.Capability.Id.StartsWith("compat.appservice.ticket.", StringComparison.Ordinal)).ToArray();
        declared.Should().HaveCount(11);
    }
}
