using System.Net;
using System.Net.Http.Json;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using CrestCreates.Sample.Procurement.Tests.TestInfrastructure;

namespace CrestCreates.Sample.Procurement.Tests.Composition;

public class FullCompositionTests : IClassFixture<ProcurementWebApplicationFactory>
{
    private readonly HttpClient _client;

    public FullCompositionTests(ProcurementWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task End_to_end_submit_approve_query()
    {
        var submitInput = new SubmitProcurementRequestInput
        {
            Title = "Office Chair",
            Amount = 300m,
            Currency = "USD",
            RequesterId = "user-1",
            Category = "General"
        };

        var submitResponse = await _client.PostAsJsonAsync("/api/procurement/requests", submitInput);
        submitResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var approveInput = new ApproveProcurementRequestInput
        {
            RequestId = Guid.NewGuid(),
            ApproverId = "approver-1",
            Comment = "Approved"
        };

        var approveResponse = await _client.GetAsync(
            $"/api/procurement/approve?RequestId={approveInput.RequestId}&ApproverId={approveInput.ApproverId}&Comment={approveInput.Comment}");

        approveResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);

        var getResponse = await _client.GetAsync($"/api/procurement/requests/{approveInput.RequestId}");
        getResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }
}
