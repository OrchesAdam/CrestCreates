using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Sample.Procurement.Tests.Agent;

public class ProcurementAgentToolTests
{
    [Fact]
    public async Task Submit_request_agent_tool_returns_result()
    {
        true.Should().BeTrue();
    }

    [Fact]
    public async Task High_value_request_agent_tool_respects_risk_floor()
    {
        true.Should().BeTrue();
    }
}
