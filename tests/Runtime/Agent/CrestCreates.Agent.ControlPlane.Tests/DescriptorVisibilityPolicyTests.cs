using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Unit tests for the descriptor visibility policy evaluator.
/// Validates closed-world semantics, deny-wins behavior, and invalid kind handling.
/// </summary>
public class DescriptorVisibilityPolicyTests
{
    // ── Development mode: open world ──

    [Fact]
    public void Development_AllowsAllValidKinds()
    {
        var evaluator = new AgentDescriptorKindPolicyEvaluator(
            AgentToolAuthorizationOptions.DevelopmentDefaults);

        evaluator.Evaluate(DescriptorKind.Event).Should().Be(AgentDescriptorKindDecision.Visible);
        evaluator.Evaluate(DescriptorKind.Workflow).Should().Be(AgentDescriptorKindDecision.Visible);
        evaluator.Evaluate(DescriptorKind.Schema).Should().Be(AgentDescriptorKindDecision.Visible);
        evaluator.Evaluate(DescriptorKind.Capability).Should().Be(AgentDescriptorKindDecision.Visible);
        evaluator.Evaluate(DescriptorKind.Form).Should().Be(AgentDescriptorKindDecision.Visible);
        evaluator.Evaluate(DescriptorKind.HumanTask).Should().Be(AgentDescriptorKindDecision.Visible);
    }

    [Fact]
    public void Development_DeniesExplicitlyDeniedKind()
    {
        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = new HashSet<string>(StringComparer.Ordinal) { "Event" }
        };
        var evaluator = new AgentDescriptorKindPolicyEvaluator(options);

        evaluator.Evaluate(DescriptorKind.Event).Should().Be(AgentDescriptorKindDecision.Denied);
        evaluator.Evaluate(DescriptorKind.Workflow).Should().Be(AgentDescriptorKindDecision.Visible);
    }

    // ── Production mode: closed world ──

    [Fact]
    public void Production_Is_Closed_World_And_Deny_Wins()
    {
        var options = AgentToolAuthorizationOptions.ProductionDefaults with
        {
            AllowedDescriptorKinds = [nameof(DescriptorKind.Event), nameof(DescriptorKind.Workflow)],
            DeniedDescriptorKinds = [nameof(DescriptorKind.Event)]
        };
        var evaluator = new AgentDescriptorKindPolicyEvaluator(options);

        evaluator.Evaluate(DescriptorKind.Event).Should().Be(AgentDescriptorKindDecision.Denied);
        evaluator.Evaluate(DescriptorKind.Workflow).Should().Be(AgentDescriptorKindDecision.Visible);
        evaluator.Evaluate(DescriptorKind.Schema).Should().Be(AgentDescriptorKindDecision.Denied);
    }

    [Fact]
    public void Production_EmptyAllowedKinds_DeniesAll()
    {
        var evaluator = new AgentDescriptorKindPolicyEvaluator(
            AgentToolAuthorizationOptions.ProductionDefaults);

        evaluator.Evaluate(DescriptorKind.Event).Should().Be(AgentDescriptorKindDecision.Denied);
        evaluator.Evaluate(DescriptorKind.Workflow).Should().Be(AgentDescriptorKindDecision.Denied);
        evaluator.Evaluate(DescriptorKind.Schema).Should().Be(AgentDescriptorKindDecision.Denied);
    }

    // ── Explicit allow ──

    [Fact]
    public void ExplicitAllow_PermitsListedKind()
    {
        var options = new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.ExplicitPolicy,
            AllowedDescriptorKinds = new HashSet<string>(StringComparer.Ordinal) { "Event" }
        };
        var evaluator = new AgentDescriptorKindPolicyEvaluator(options);

        evaluator.Evaluate(DescriptorKind.Event).Should().Be(AgentDescriptorKindDecision.Visible);
        evaluator.Evaluate(DescriptorKind.Workflow).Should().Be(AgentDescriptorKindDecision.Denied);
    }

    // ── Deny wins over allow ──

    [Fact]
    public void DenyOverridesAllow()
    {
        var options = new AgentToolAuthorizationOptions
        {
            Mode = AgentToolAuthorizationMode.DevelopmentAllowAll,
            AllowedDescriptorKinds = new HashSet<string>(StringComparer.Ordinal) { "Event" },
            DeniedDescriptorKinds = new HashSet<string>(StringComparer.Ordinal) { "Event" }
        };
        var evaluator = new AgentDescriptorKindPolicyEvaluator(options);

        evaluator.Evaluate(DescriptorKind.Event).Should().Be(AgentDescriptorKindDecision.Denied);
    }

    // ── Invalid kind ──

    [Fact]
    public void InvalidKind_ReturnsInvalid()
    {
        var evaluator = new AgentDescriptorKindPolicyEvaluator(
            AgentToolAuthorizationOptions.DevelopmentDefaults);

        var invalidKind = (DescriptorKind)int.MaxValue;
        evaluator.Evaluate(invalidKind).Should().Be(AgentDescriptorKindDecision.Invalid);
    }

    // ── HasRestrictions ──

    [Fact]
    public void HasRestrictions_True_WhenClosedWorld()
    {
        var evaluator = new AgentDescriptorKindPolicyEvaluator(
            AgentToolAuthorizationOptions.ProductionDefaults);

        evaluator.HasRestrictions.Should().BeTrue();
    }

    [Fact]
    public void HasRestrictions_True_WhenDeniedKindsExist()
    {
        var options = AgentToolAuthorizationOptions.DevelopmentDefaults with
        {
            DeniedDescriptorKinds = new HashSet<string>(StringComparer.Ordinal) { "Event" }
        };
        var evaluator = new AgentDescriptorKindPolicyEvaluator(options);

        evaluator.HasRestrictions.Should().BeTrue();
    }

    [Fact]
    public void HasRestrictions_False_WhenOpenWorldAndNoDenies()
    {
        var evaluator = new AgentDescriptorKindPolicyEvaluator(
            AgentToolAuthorizationOptions.DevelopmentDefaults);

        evaluator.HasRestrictions.Should().BeFalse();
    }

    // ── LockedDown mode ──

    [Fact]
    public void LockedDown_DeniesAllKinds()
    {
        var evaluator = new AgentDescriptorKindPolicyEvaluator(
            AgentToolAuthorizationOptions.LockedDown);

        evaluator.Evaluate(DescriptorKind.Event).Should().Be(AgentDescriptorKindDecision.Denied);
        evaluator.Evaluate(DescriptorKind.Workflow).Should().Be(AgentDescriptorKindDecision.Denied);
    }
}
