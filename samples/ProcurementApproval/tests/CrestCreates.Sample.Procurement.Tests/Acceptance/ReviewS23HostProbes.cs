using System.Net;
using CrestCreates.Capability.Abstractions;
using CrestCreates.DynamicApi;
using CrestCreates.Generated.HttpProjection;
using CrestCreates.Metadata;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Sample.Procurement.Host.Projections;
using CrestCreates.Sample.Procurement.Tests.TestInfrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit.Abstractions;

namespace CrestCreates.Sample.Procurement.Tests.Acceptance;

public sealed class ReviewS23HostProbes(ITestOutputHelper output)
{
    [Fact]
    public async Task Repeated_generated_activation_keeps_the_real_http_contract_working()
    {
        using var factory = new ProcurementWebApplicationFactory(services => services.AddGeneratedAppServiceHttp());
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ProcurementAppService>().Should().NotBeNull();
        ProjectionCompositionAcceptanceTests.SetIdentity(client, "tenant-a", "requester-a", "procurement-requester");
        var row = await ProjectionCompositionAcceptanceTests.SubmitHttpAsync(client, "Repeated activation", 100m);
        (await client.GetAsync($"/api/procurement/query/{row.RequestId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Missing_business_contract_must_fail_at_startup_not_first_request()
    {
        using var factory = new ProcurementWebApplicationFactory(services => services.RemoveAll<ProcurementAppService>());
        HttpClient? client = null;
        var failure = Record.Exception(() => client = factory.CreateClient());
        if (failure is null)
        {
            factory.Services.GetRequiredService<IServiceProviderIsService>()
                .IsService(typeof(ProcurementAppService)).Should().BeFalse("only the required business registration was removed");
            var routes = factory.Services.GetServices<EndpointDataSource>()
                .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
                .Select(endpoint => endpoint.RoutePattern.RawText).ToArray();
            output.WriteLine("Startup succeeded without business DI. Routes: " + string.Join(" | ", routes));
            ProjectionCompositionAcceptanceTests.SetIdentity(client!, "tenant-a", "requester-a", "procurement-requester");
            var row = await ProjectionCompositionAcceptanceTests.SubmitHttpAsync(client!, "Missing business DI", 100m);
            var response = await client!.GetAsync($"/api/procurement/query/{row.RequestId}");
            output.WriteLine("First compatibility request status: " + (int)response.StatusCode);
            output.WriteLine("First compatibility request body: " + await response.Content.ReadAsStringAsync());
            client.Dispose();
        }

        failure.Should().NotBeNull("R-04 requires startup CCHTTP007, zero routes and no request-time 500");
        failure!.ToString().Should().Contain("CCHTTP007").And.Contain(nameof(ProcurementAppService));
    }

    [Fact]
    public async Task Activated_and_opted_out_hosts_do_not_share_generated_endpoints()
    {
        using var active = new ProcurementWebApplicationFactory();
        using var a = active.CreateClient();
        ProjectionCompositionAcceptanceTests.SetIdentity(a, "tenant-a", "requester-a", "procurement-requester");
        var first = await ProjectionCompositionAcceptanceTests.SubmitHttpAsync(a, "Active host", 100m);
        (await a.GetAsync($"/api/procurement/query/{first.RequestId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        using var inactive = new ProcurementWebApplicationFactory(services => services.RemoveAll<CapabilityEndpointContribution>());
        using var b = inactive.CreateClient();
        ProjectionCompositionAcceptanceTests.SetIdentity(b, "tenant-b", "requester-b", "procurement-requester");
        var second = await ProjectionCompositionAcceptanceTests.SubmitHttpAsync(b, "Opted out host", 100m);
        inactive.Services.GetRequiredService<ICapabilityRegistry>().GetById("compat.appservice.procurement.get").Should().BeNull();
        (await b.GetAsync($"/api/procurement/query/{second.RequestId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await a.GetAsync($"/api/procurement/query/{first.RequestId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
