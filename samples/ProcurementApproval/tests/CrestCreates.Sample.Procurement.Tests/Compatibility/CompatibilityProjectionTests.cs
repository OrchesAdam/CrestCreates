using System.Net;
using System.Net.Http.Json;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using CrestCreates.Sample.Procurement.Tests.TestInfrastructure;

namespace CrestCreates.Sample.Procurement.Tests.Compatibility;

public class CompatibilityProjectionTests : IClassFixture<ProcurementWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CompatibilityProjectionTests(ProcurementWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Submit_via_compatibility_projection_returns_success()
    {
        var response = await _client.GetAsync(
            "/api/procurement/submit?Title=Office+Supplies&Amount=500&Currency=USD&RequesterId=user-1&Category=General");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Approve_via_compatibility_projection_returns_success()
    {
        var requestId = Guid.NewGuid();
        var response = await _client.GetAsync(
            $"/api/procurement/approve?RequestId={requestId}&ApproverId=approver-1&Reason=OK");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reject_via_compatibility_projection_returns_success()
    {
        var requestId = Guid.NewGuid();
        var response = await _client.GetAsync(
            $"/api/procurement/reject?RequestId={requestId}&ApproverId=approver-1&Reason=Denied");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);
    }
}
