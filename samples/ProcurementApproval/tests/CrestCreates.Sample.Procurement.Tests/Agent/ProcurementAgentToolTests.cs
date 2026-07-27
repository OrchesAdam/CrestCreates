using FluentAssertions;

namespace CrestCreates.Sample.Procurement.Tests.Agent;

public class ProcurementAgentToolTests
{
    [Fact]
    public async Task Submit_request_agent_tool_returns_result()
    {
        var pipeline = ProcurementAgentTestHost.CreatePipeline();
        var result = await pipeline.ExecuteAsync(
            "agent.procurement.submit-request",
            new { title = "Office Supplies", amount = 500m, currency = "USD" },
            configureContext: ctx => ctx.InvocationSource = InvocationSource.Agent,
            cancellationToken: CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task High_value_request_agent_tool_respects_risk_floor()
    {
        var pipeline = ProcurementAgentTestHost.CreatePipeline();
        var result = await pipeline.ExecuteAsync(
            "agent.procurement.submit-request",
            new { title = "Server Hardware", amount = 50000m, currency = "USD" },
            configureContext: ctx => ctx.InvocationSource = InvocationSource.Agent,
            cancellationToken: CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }
}
