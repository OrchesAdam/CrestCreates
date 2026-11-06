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
    public void Normalized_ticket_family_has_eleven_formal_endpoints_and_no_legacy_spelling_with_authorization_metadata()
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
        endpoints.Should().OnlyContain(endpoint =>
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 1,
            "the Ticket family inherits non-empty capability permissions and must gate anonymous callers");
        var declared = Factory.Services.GetRequiredService<ICapabilityEndpointRegistry>().GetAll()
            .Where(endpoint => endpoint.Capability.Id.StartsWith("compat.appservice.ticket.", StringComparison.Ordinal)).ToArray();
        declared.Should().HaveCount(11);
    }
}
