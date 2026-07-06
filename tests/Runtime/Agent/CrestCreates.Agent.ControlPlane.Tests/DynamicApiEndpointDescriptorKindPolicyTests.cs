using CrestCreates.Agent.ControlPlane;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.ControlPlane.Tests;

public class DynamicApiEndpointDescriptorKindPolicyTests
{
    [Fact]
    public void DynamicApiEndpoint_Is_Valid_Descriptor_Kind()
    {
        AgentDescriptorKindPolicyEvaluator.IsValidDescriptorKind(DescriptorKind.DynamicApiEndpoint)
            .Should().BeTrue();
    }

    [Fact]
    public void Closed_World_Denies_DynamicApiEndpoint_When_Not_Allowed()
    {
        var evaluator = new AgentDescriptorKindPolicyEvaluator(new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.ExplicitPolicy,
            AllowedDescriptorKinds = new HashSet<string>(StringComparer.Ordinal) { nameof(DescriptorKind.Capability) }
        });

        evaluator.Evaluate(DescriptorKind.DynamicApiEndpoint)
            .Should().Be(AgentDescriptorKindDecision.Denied);
    }

    [Fact]
    public void Deny_Rule_Overrides_Allow_For_DynamicApiEndpoint()
    {
        var evaluator = new AgentDescriptorKindPolicyEvaluator(new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.ExplicitPolicy,
            AllowedDescriptorKinds = new HashSet<string>(StringComparer.Ordinal) { nameof(DescriptorKind.DynamicApiEndpoint) },
            DeniedDescriptorKinds = new HashSet<string>(StringComparer.Ordinal) { nameof(DescriptorKind.DynamicApiEndpoint) }
        });

        evaluator.Evaluate(DescriptorKind.DynamicApiEndpoint)
            .Should().Be(AgentDescriptorKindDecision.Denied);
    }
}
