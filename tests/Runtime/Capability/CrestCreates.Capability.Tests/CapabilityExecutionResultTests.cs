using CrestCreates.Capability.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityExecutionResultTests
{
    [Fact]
    public void Success_Creates_Result_With_Succeeded_Status()
    {
        var result = CapabilityExecutionResult.Success("output", TimeSpan.FromMilliseconds(100));

        result.Status.Should().Be(CapabilityExecutionStatus.Succeeded);
        result.IsSuccess.Should().BeTrue();
        result.Output.Should().Be("output");
        result.Duration.Should().Be(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void Failure_Creates_Result_With_Failed_Status()
    {
        var result = CapabilityExecutionResult.Failure("ERR_01", "Something broke", TimeSpan.FromSeconds(1));

        result.Status.Should().Be(CapabilityExecutionStatus.Failed);
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("ERR_01");
        result.ErrorMessage.Should().Be("Something broke");
    }

    [Fact]
    public void Timeout_Creates_Result_With_TimedOut_Status()
    {
        var result = CapabilityExecutionResult.Timeout(TimeSpan.FromSeconds(30));

        result.Status.Should().Be(CapabilityExecutionStatus.TimedOut);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Success_Includes_EmittedEventIds()
    {
        var eventIds = new[] { "evt_01", "evt_02" };
        var result = CapabilityExecutionResult.Success(
            "output", TimeSpan.FromMilliseconds(50),
            emittedEventIds: eventIds);

        result.EmittedEventIds.Should().HaveCount(2);
        result.EmittedEventIds.Should().Contain("evt_01");
    }
}
