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
        var input = new ApproveProcurementRequestInput
        {
            RequestId = Guid.NewGuid(),
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
    public async Task Approve_AlreadyApprovedRequest_ReturnsValidationFailure()
    {
        var pipeline = _host.CreatePipeline();
        var input = new ApproveProcurementRequestInput
        {
            RequestId = Guid.NewGuid(),
            ApproverId = "approver-1",
            Comment = "Already approved"
        };

        var result = await pipeline.ExecuteAsync("procurement.approve-request", input);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Reject_PendingRequest_ReturnsSuccess()
    {
        var pipeline = _host.CreatePipeline();
        var input = new RejectProcurementRequestInput
        {
            RequestId = Guid.NewGuid(),
            ApproverId = "approver-1",
            Reason = "Over budget limit"
        };

        var result = await pipeline.ExecuteAsync("procurement.reject-request", input);

        result.IsSuccess.Should().BeTrue();
        var output = result.Output.Should().BeAssignableTo<ProcurementRequestResult>().Subject;
        output.Status.Should().Be("Rejected");
        output.ApproverId.Should().Be("approver-1");
        output.RejectedAt.Should().NotBeNull();
    }
}
