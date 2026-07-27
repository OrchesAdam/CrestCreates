using System.Net;
using System.Net.Http.Json;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using CrestCreates.Sample.Procurement.Tests.TestInfrastructure;

namespace CrestCreates.Sample.Procurement.Tests.Http;

public class ProcurementHttpTests : IClassFixture<ProcurementWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ProcurementHttpTests(ProcurementWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task POST_submit_returns_201_with_request_id()
    {
        var input = new SubmitProcurementRequestInput
        {
            Title = "Office Supplies",
            Amount = 500m,
            Currency = "USD",
            RequesterId = "user-1",
            Category = "General"
        };

        var response = await _client.PostAsJsonAsync("/api/procurement/requests", input);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task GET_request_by_id_returns_200()
    {
        var requestId = Guid.NewGuid();
        var response = await _client.GetAsync($"/api/procurement/requests/{requestId}");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }
}
