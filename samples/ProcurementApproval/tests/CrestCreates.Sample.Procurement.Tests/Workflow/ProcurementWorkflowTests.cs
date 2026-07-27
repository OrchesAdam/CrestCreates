using FluentAssertions;

namespace CrestCreates.Sample.Procurement.Tests.Workflow;

public class ProcurementWorkflowTests
{
    [Fact]
    public async Task High_value_request_creates_approval_workflow()
    {
        var pipeline = WorkflowTestHost.CreatePipeline();
        var result = await pipeline.ExecuteAsync(
            "procurement.submit-request",
            new SubmitProcurementRequestInput
            {
                Title = "Server Hardware",
                Amount = 15000m,
                Currency = "USD",
                RequesterId = "user-1",
                Category = "IT"
            },
            configureContext: ctx => ctx.InvocationSource = InvocationSource.Http,
            cancellationToken: CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        WorkflowTestHost.HasPendingApprovalFor(result.Output!.RequestId).Should().BeTrue();
    }
}
