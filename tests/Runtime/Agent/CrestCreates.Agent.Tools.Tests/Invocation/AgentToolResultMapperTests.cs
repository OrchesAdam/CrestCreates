using CrestCreates.Agent.Tools;
using CrestCreates.Capability.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests.Invocation;

public sealed class AgentToolResultMapperTests
{
    [Fact]
    public void CapabilityFailure_DoesNotExposeArbitraryIssues()
    {
        var result = CapabilityExecutionResult.Failure(
            "CAPABILITY_INTERNAL_POLICY",
            "internal",
            TimeSpan.Zero,
            [new CapabilityExecutionIssue("SECRET_POLICY_NAME", "internal detail", "privateField")]);

        var outcome = new AgentToolResultMapper().CapabilityFailure(result);

        outcome.Issues.Should().BeEmpty();
    }

    [Fact]
    public void CapabilityValidationFailure_ExposesOnlySafeSchemaIssues()
    {
        var result = CapabilityExecutionResult.Failure(
            CapabilityExecutionErrorCodes.ValidationFailed,
            "invalid",
            TimeSpan.Zero,
            [
                new CapabilityExecutionIssue("FIELD_REQUIRED", "required", "name"),
                new CapabilityExecutionIssue("SECRET_POLICY_NAME", "internal detail", "privateField")
            ]);

        var outcome = new AgentToolResultMapper().CapabilityFailure(result);

        outcome.Issues.Should().ContainSingle(issue =>
            issue.Code == "FIELD_REQUIRED" && issue.FieldPath == "name");
    }
}
