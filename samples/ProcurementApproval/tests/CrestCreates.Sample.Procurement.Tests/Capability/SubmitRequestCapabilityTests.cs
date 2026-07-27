using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts.Dtos;
using CrestCreates.Sample.Procurement.Domain.Exceptions;
using CrestCreates.Sample.Procurement.Tests.TestInfrastructure;

namespace CrestCreates.Sample.Procurement.Tests.Capability;

public class SubmitRequestCapabilityTests
{
    private readonly CapabilityTestHost _host = new();

    [Fact]
    public async Task Submit_ValidRequest_ReturnsSuccess()
    {
        var pipeline = _host.CreatePipeline();
        var input = new SubmitProcurementRequestInput
        {
            Title = "Office Supplies",
            Amount = 500m,
            Currency = "USD",
            RequesterId = "user-1",
            Category = "General"
        };

        var result = await pipeline.ExecuteAsync("procurement.submit-request", input);

        result.IsSuccess.Should().BeTrue();
        var output = result.Output.Should().BeAssignableTo<SubmitProcurementRequestResult>().Subject;
        output.RequestId.Should().NotBeEmpty();
        output.Status.Should().Be("Approved");
        output.Amount.Should().Be(500m);
        output.Currency.Should().Be("USD");
        output.RequiresApproval.Should().BeFalse();
    }

    [Fact]
    public async Task Submit_InvalidAmount_ReturnsValidationFailure()
    {
        var pipeline = _host.CreatePipeline();
        var input = new SubmitProcurementRequestInput
        {
            Title = "",
            Amount = -100m,
            Currency = "USD",
            RequesterId = "user-1",
            Category = "General"
        };

        var act = () => pipeline.ExecuteAsync("procurement.submit-request", input);

        await act.Should().ThrowAsync<InvalidProcurementRequestException>();
    }

    [Fact]
    public async Task Submit_AmountAboveThreshold_RoutesToApproval()
    {
        var pipeline = _host.CreatePipeline();
        var input = new SubmitProcurementRequestInput
        {
            Title = "Server Equipment",
            Amount = 15000m,
            Currency = "USD",
            RequesterId = "user-1",
            Category = "IT"
        };

        var result = await pipeline.ExecuteAsync("procurement.submit-request", input);

        result.IsSuccess.Should().BeTrue();
        var output = result.Output.Should().BeAssignableTo<SubmitProcurementRequestResult>().Subject;
        output.Status.Should().Be("PendingApproval");
        output.RequiresApproval.Should().BeTrue();
    }
}
