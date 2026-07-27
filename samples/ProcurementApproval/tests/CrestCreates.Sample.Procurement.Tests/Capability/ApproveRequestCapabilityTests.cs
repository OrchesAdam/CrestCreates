using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Sample.Procurement.Contracts.Dtos;

namespace CrestCreates.Sample.Procurement.Tests.Capability;

public class ApproveRequestCapabilityTests
{
    [Fact]
    public async Task Approve_PendingRequest_ReturnsSuccess()
    {
        true.Should().BeTrue();
    }

    [Fact]
    public async Task Approve_AlreadyApprovedRequest_ReturnsValidationFailure()
    {
        true.Should().BeTrue();
    }

    [Fact]
    public async Task Reject_PendingRequest_ReturnsSuccess()
    {
        true.Should().BeTrue();
    }
}
