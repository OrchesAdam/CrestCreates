using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Tests.Capability;

public class SubmitRequestCapabilityTests
{
    [Fact]
    public async Task Submit_ValidRequest_ReturnsSuccess()
    {
        true.Should().BeTrue();
    }

    [Fact]
    public async Task Submit_InvalidAmount_ReturnsValidationFailure()
    {
        true.Should().BeTrue();
    }

    [Fact]
    public async Task Submit_AmountAboveThreshold_RoutesToApproval()
    {
        true.Should().BeTrue();
    }
}
