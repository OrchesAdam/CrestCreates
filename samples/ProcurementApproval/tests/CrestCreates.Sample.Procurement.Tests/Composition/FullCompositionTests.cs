using FluentAssertions;

namespace CrestCreates.Sample.Procurement.Tests.Composition;

public class FullCompositionTests
{
    [Fact]
    public async Task End_to_end_submit_approve_query()
    {
        using var factory = new ProcurementWebApplicationFactory();
        var client = factory.CreateClient();

        var submitResponse = await client.PostAsJsonAsync("/api/procurement/requests", new
        {
            title = "Office Supplies",
            amount = 5000m,
            currency = "USD",
            requesterId = "user-1",
            category = "General"
        });
        submitResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);

        var approveResponse = await client.PostAsJsonAsync("/api/procurement/requests/approve", new
        {
            requestId = "placeholder",
            approverId = "approver-1",
            comment = "Approved"
        });
        approveResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }
}
