using CrestCreates.Capability.Abstractions;
using FluentAssertions;

namespace CrestCreates.Sample.Procurement.Tests.Capability;

public class SubmitRequestCapabilityTests
{
    [Fact]
    public async Task Submit_ValidRequest_ReturnsSuccess()
    {
        var pipeline = CapabilityTestHost.CreatePipeline();
        var input = new SubmitProcurementRequestInput
        {
            Title = "Office Supplies",
            Amount = 500m,
            Currency = "USD",
            RequesterId = "user-1",
            Category = "General"
        };

        var result = await pipeline.ExecuteAsync(
            "procurement.submit-request",
            input,
            configureContext: ctx => ctx.InvocationSource = InvocationSource.Http,
            cancellationToken: CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Output.Should().NotBeNull();
    }

    [Fact]
    public async Task Submit_InvalidAmount_ReturnsValidationFailure()
    {
        var pipeline = CapabilityTestHost.CreatePipeline();
        var input = new SubmitProcurementRequestInput
        {
            Title = "Office Supplies",
            Amount = -100m,
            Currency = "USD",
            RequesterId = "user-1",
            Category = "General"
        };

        var result = await pipeline.ExecuteAsync(
            "procurement.submit-request",
            input,
            configureContext: ctx => ctx.InvocationSource = InvocationSource.Http,
            cancellationToken: CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("CAPABILITY_VALIDATION_FAILED");
    }

    [Fact]
    public async Task Submit_AmountAboveThreshold_RoutesToApproval()
    {
        var pipeline = CapabilityTestHost.CreatePipeline();
        var input = new SubmitProcurementRequestInput
        {
            Title = "Server Hardware",
            Amount = 15000m,
            Currency = "USD",
            RequesterId = "user-1",
            Category = "IT"
        };

        var result = await pipeline.ExecuteAsync(
            "procurement.submit-request",
            input,
            configureContext: ctx => ctx.InvocationSource = InvocationSource.Http,
            cancellationToken: CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
