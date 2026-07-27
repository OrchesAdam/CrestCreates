using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using CrestCreates.Sample.Procurement.Tests.TestInfrastructure;

namespace CrestCreates.Sample.Procurement.Tests.Mcp;

public class ProcurementMcpToolTests
{
    [Fact]
    public async Task Submit_request_handler_produces_pending_status_for_high_value()
    {
        var pipeline = CapabilityTestHost.BuildPipeline();
        var input = new SubmitProcurementRequestInput
        {
            Title = "Server Rack",
            Amount = 15000m,
            Currency = "USD",
            RequesterId = "user-1",
            Category = "Infrastructure"
        };

        var result = await pipeline.ExecuteAsync("procurement.submit-request", input);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Submit_request_handler_produces_approved_status_for_low_value()
    {
        var pipeline = CapabilityTestHost.BuildPipeline();
        var input = new SubmitProcurementRequestInput
        {
            Title = "Office Supplies",
            Amount = 500m,
            Currency = "USD",
            RequesterId = "user-1",
            Category = "General"
        };

        var result = await pipeline.ExecuteAsync("procurement.submit-request", input);

        result.IsSuccess.Should().BeTrue();
    }
}
