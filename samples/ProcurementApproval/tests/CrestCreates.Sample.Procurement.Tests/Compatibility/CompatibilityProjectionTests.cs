using CrestCreates.Capability.Abstractions;
using FluentAssertions;

namespace CrestCreates.Sample.Procurement.Tests.Compatibility;

public class CompatibilityProjectionTests
{
    [Fact]
    public async Task Submit_via_compatibility_projection_returns_legacy_envelope()
    {
        using var factory = new ProcurementWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/procurement/compat/submit", new
        {
            title = "Office Supplies",
            amount = 500m,
            currency = "USD",
            requesterId = "user-1",
            category = "General"
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }
}
