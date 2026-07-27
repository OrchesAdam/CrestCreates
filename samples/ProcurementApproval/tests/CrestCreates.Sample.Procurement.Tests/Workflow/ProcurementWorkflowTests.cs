using CrestCreates.Sample.Procurement.Contracts.Dtos;
using CrestCreates.Sample.Procurement.Tests.TestInfrastructure;

namespace CrestCreates.Sample.Procurement.Tests.Workflow;

public class ProcurementWorkflowTests
{
    [Fact(Skip = "Requires HumanTask infrastructure - deferred to next iteration")]
    public async Task High_value_request_creates_approval_workflow()
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
}
