using CrestCreates.Agent.ControlPlane;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.ControlPlane.Tests;

public sealed class McpToolDescriptorKindPolicyTests
{
    [Fact]
    public void McpTool_remains_outside_agent_authoring_allowlist()
        => AgentDescriptorKindPolicyEvaluator.IsValidDescriptorKind(DescriptorKind.McpTool)
            .Should().BeFalse();
}
