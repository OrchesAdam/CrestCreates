using FluentAssertions;

namespace CrestCreates.Sample.Procurement.Tests.Mcp;

public class ProcurementMcpToolTests
{
    [Fact]
    public async Task Submit_request_tool_returns_result()
    {
        var pipeline = ProcurementMcpTestHost.CreatePipeline();
        var result = await pipeline.ExecuteAsync(
            "mcp.procurement.submit-request",
            new { title = "Office Supplies", amount = 500m, currency = "USD" },
            configureContext: ctx => ctx.InvocationSource = InvocationSource.Mcp,
            cancellationToken: CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
