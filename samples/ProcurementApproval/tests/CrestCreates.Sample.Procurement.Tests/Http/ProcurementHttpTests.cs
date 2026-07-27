using CrestCreates.Capability.Abstractions;
using FluentAssertions;

namespace CrestCreates.Sample.Procurement.Tests.Http;

public class ProcurementHttpTests
{
    [Fact]
    public async Task POST_submit_returns_201_with_request_id()
    {
        using var factory = new ProcurementWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/procurement/requests", new
        {
            title = "Office Supplies",
            amount = 500m,
            currency = "USD",
            requesterId = "user-1",
            category = "General"
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
    }

    [Fact]
    public async Task GET_request_by_id_returns_200()
    {
        using var factory = new ProcurementWebApplicationFactory();
        var client = factory.CreateClient();

        var submitResponse = await client.PostAsJsonAsync("/api/procurement/requests", new
        {
            title = "Office Supplies",
            amount = 500m,
            currency = "USD",
            requesterId = "user-1",
            category = "General"
        });

        var location = submitResponse.Headers.Location;
        var response = await client.GetAsync(location);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }
}
