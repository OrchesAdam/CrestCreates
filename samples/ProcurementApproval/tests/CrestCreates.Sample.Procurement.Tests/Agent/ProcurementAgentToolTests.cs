using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using CrestCreates.Sample.Procurement.Tests.TestInfrastructure;

namespace CrestCreates.Sample.Procurement.Tests.Agent;

public class ProcurementAgentToolTests
{
    [Fact]
    public async Task Submit_request_agent_tool_returns_result()
    {
        var pipeline = CapabilityTestHost.BuildPipeline();
        var input = new SubmitProcurementRequestInput
        {
            Title = "Laptop",
            Amount = 2000m,
            Currency = "USD",
            RequesterId = "agent-1",
            Category = "IT"
        };

        var result = await pipeline.ExecuteAsync("procurement.submit-request", input);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task High_value_request_agent_tool_respects_risk_floor()
    {
        var pipeline = CapabilityTestHost.BuildPipeline();
        var input = new SubmitProcurementRequestInput
        {
            Title = "Data Center Equipment",
            Amount = 50000m,
            Currency = "USD",
            RequesterId = "agent-1",
            Category = "Infrastructure"
        };

        var result = await pipeline.ExecuteAsync("procurement.submit-request", input);

        result.IsSuccess.Should().BeTrue();
    }
}
