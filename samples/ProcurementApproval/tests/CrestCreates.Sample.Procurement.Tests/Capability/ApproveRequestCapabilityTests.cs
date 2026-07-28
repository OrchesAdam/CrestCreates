using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using CrestCreates.Sample.Procurement.Tests.TestInfrastructure;

namespace CrestCreates.Sample.Procurement.Tests.Capability;

public class ApproveRequestCapabilityTests
{
    private readonly CapabilityTestHost _host = new();

    [Fact]
    public async Task Approve_PendingRequest_ReturnsSuccess()
    {
        var pipeline = _host.CreatePipeline();
        var submitInput = new SubmitProcurementRequestInput
        {
            Title = "Big Order",
            Amount = 20000,
            Currency = "USD",
            RequesterId = "user-1",
            Category = "Equipment"
        };
        var submitResult = await pipeline.ExecuteAsync("procurement.submit-request", submitInput);
        var submitOutput = submitResult.Output.Should().BeAssignableTo<SubmitProcurementRequestResult>().Subject;
        var requestId = submitOutput.RequestId;

        var input = new ApproveProcurementRequestInput
        {
            RequestId = requestId,
            ApproverId = "approver-1",
            Comment = "Approved - within budget"
        };

        var result = await pipeline.ExecuteAsync("procurement.approve-request", input);

        result.IsSuccess.Should().BeTrue();
        var output = result.Output.Should().BeAssignableTo<ProcurementRequestResult>().Subject;
        output.Status.Should().Be("Approved");
        output.ApproverId.Should().Be("approver-1");
        output.ApprovedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Approve_NonExistentRequest_ReturnsNotFound()
    {
        var pipeline = _host.CreatePipeline();
        var input = new ApproveProcurementRequestInput
        {
            RequestId = Guid.NewGuid(),
            ApproverId = "approver-1",
            Comment = "N/A"
        };

        var result = await pipeline.ExecuteAsync("procurement.approve-request", input);

        result.IsSuccess.Should().BeTrue();
        var output = result.Output.Should().BeAssignableTo<ProcurementRequestResult>().Subject;
        output.Status.Should().Be("NotFound");
    }

    [Fact]
    public async Task Reject_PendingRequest_ReturnsSuccess()
    {
        var pipeline = _host.CreatePipeline();
        var submitInput = new SubmitProcurementRequestInput
        {
            Title = "Expensive Item",
            Amount = 50000,
            Currency = "EUR",
            RequesterId = "user-2",
            Category = "IT"
        };
        var submitResult = await pipeline.ExecuteAsync("procurement.submit-request", submitInput);
        var submitOutput = submitResult.Output.Should().BeAssignableTo<SubmitProcurementRequestResult>().Subject;
        var requestId = submitOutput.RequestId;

        var input = new RejectProcurementRequestInput
        {
            RequestId = requestId,
            ApproverId = "approver-2",
            Reason = "Over budget limit"
        };

        var result = await pipeline.ExecuteAsync("procurement.reject-request", input);

        result.IsSuccess.Should().BeTrue();
        var output = result.Output.Should().BeAssignableTo<ProcurementRequestResult>().Subject;
        output.Status.Should().Be("Rejected");
        output.ApproverId.Should().Be("approver-2");
        output.RejectedAt.Should().NotBeNull();
    }
}
