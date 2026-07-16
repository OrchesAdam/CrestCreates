using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.ControlPlane.Tests;

public sealed class AgentToolDescriptorKindPolicyTests
{
    [Fact]
    public void AgentTool_remains_outside_control_plane_mutation_allowlist()
        => AgentDescriptorKindPolicyEvaluator.IsValidDescriptorKind(DescriptorKind.AgentTool)
            .Should().BeFalse();
}
