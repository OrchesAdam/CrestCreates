using CrestCreates.Capability.Abstractions;
using FluentAssertions;

namespace CrestCreates.Sample.Procurement.Tests.Capability;

public class ApproveRequestCapabilityTests
{
    [Fact]
    public async Task Approve_PendingRequest_ReturnsSuccess()
    {
        var pipeline = CapabilityTestHost.CreatePipeline();
        var submitResult = await SubmitHelper.SubmitAsync(pipeline, amount: 5000m);

        var input = new ApproveProcurementRequestInput
        {
            RequestId = submitResult.RequestId,
            ApproverId = "approver-1",
            Comment = "Approved"
        };

        var result = await pipeline.ExecuteAsync(
            "procurement.approve-request",
            input,
            configureContext: ctx => ctx.InvocationSource = InvocationSource.HumanTask,
            cancellationToken: CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Approve_AlreadyApprovedRequest_ReturnsValidationFailure()
    {
        var pipeline = CapabilityTestHost.CreatePipeline();
        var submitResult = await SubmitHelper.SubmitAsync(pipeline, amount: 5000m);

        var input = new ApproveProcurementRequestInput
        {
            RequestId = submitResult.RequestId,
            ApproverId = "approver-1",
            Comment = "Approved"
        };

        await pipeline.ExecuteAsync("procurement.approve-request", input,
            configureContext: ctx => ctx.InvocationSource = InvocationSource.HumanTask,
            cancellationToken: CancellationToken.None);

        var result = await pipeline.ExecuteAsync("procurement.approve-request", input,
            configureContext: ctx => ctx.InvocationSource = InvocationSource.HumanTask,
            cancellationToken: CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("CAPABILITY_VALIDATION_FAILED");
    }

    [Fact]
    public async Task Reject_PendingRequest_ReturnsSuccess()
    {
        var pipeline = CapabilityTestHost.CreatePipeline();
        var submitResult = await SubmitHelper.SubmitAsync(pipeline, amount: 5000m);

        var input = new RejectProcurementRequestInput
        {
            RequestId = submitResult.RequestId,
            ApproverId = "approver-1",
            Reason = "Budget cut"
        };

        var result = await pipeline.ExecuteAsync(
            "procurement.reject-request",
            input,
            configureContext: ctx => ctx.InvocationSource = InvocationSource.HumanTask,
            cancellationToken: CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
