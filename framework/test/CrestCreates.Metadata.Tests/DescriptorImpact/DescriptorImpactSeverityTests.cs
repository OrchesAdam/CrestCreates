using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests.DescriptorImpact;

public class DescriptorImpactSeverityTests
{
    private static DescriptorImpactSeverity Compute(
        DescriptorChangeKind kind, RelationshipStrength strength,
        bool isRuntimeBinding, int depth = 1)
    {
        var segment = new DescriptorImpactPathSegment
        {
            From = new DescriptorRef("ns", "source"),
            To = new DescriptorRef("ns", "target"),
            Kind = RelationshipKind.Uses,
            Strength = strength,
            IsRuntimeBinding = isRuntimeBinding
        };
        return DescriptorImpactAnalyzer.ComputePathSeverity(kind, segment, depth);
    }

    [Fact] public void Removed_StrongRuntime_IsCritical()
        => Compute(DescriptorChangeKind.Removed, RelationshipStrength.Strong, true).Should().Be(DescriptorImpactSeverity.Critical);

    [Fact] public void Removed_StrongDescriptor_IsHigh()
        => Compute(DescriptorChangeKind.Removed, RelationshipStrength.Strong, false).Should().Be(DescriptorImpactSeverity.High);

    [Fact] public void Removed_Weak_IsMedium()
        => Compute(DescriptorChangeKind.Removed, RelationshipStrength.Weak, false).Should().Be(DescriptorImpactSeverity.Medium);

    [Fact] public void Deprecated_StrongRuntime_IsHigh()
        => Compute(DescriptorChangeKind.Deprecated, RelationshipStrength.Strong, true).Should().Be(DescriptorImpactSeverity.High);

    [Fact] public void Deprecated_StrongDescriptor_IsMedium()
        => Compute(DescriptorChangeKind.Deprecated, RelationshipStrength.Strong, false).Should().Be(DescriptorImpactSeverity.Medium);

    [Fact] public void Deprecated_Weak_IsLow()
        => Compute(DescriptorChangeKind.Deprecated, RelationshipStrength.Weak, false).Should().Be(DescriptorImpactSeverity.Low);

    [Fact] public void Updated_StrongRuntime_IsHigh()
        => Compute(DescriptorChangeKind.Updated, RelationshipStrength.Strong, true).Should().Be(DescriptorImpactSeverity.High);

    [Fact] public void StateChanged_StrongRuntime_IsMedium()
        => Compute(DescriptorChangeKind.StateChanged, RelationshipStrength.Strong, true).Should().Be(DescriptorImpactSeverity.Medium);

    [Fact] public void Activated_AlwaysInfo()
        => Compute(DescriptorChangeKind.Activated, RelationshipStrength.Strong, true).Should().Be(DescriptorImpactSeverity.Info);

    [Fact] public void TransitiveAttenuation_Removed_CriticalToHigh()
        => Compute(DescriptorChangeKind.Removed, RelationshipStrength.Strong, true, depth: 2).Should().Be(DescriptorImpactSeverity.High);

    [Fact] public void TransitiveAttenuation_Deprecated_HighToMedium()
        => Compute(DescriptorChangeKind.Deprecated, RelationshipStrength.Strong, true, depth: 2).Should().Be(DescriptorImpactSeverity.Medium);

    [Fact] public void Weak_RuntimeBinding_Boosted()
        => Compute(DescriptorChangeKind.Removed, RelationshipStrength.Weak, true).Should().Be(DescriptorImpactSeverity.High);

    [Fact] public void Depth2_StrongDescriptor_Runtime_BoostAfterAttenuation()
        => Compute(DescriptorChangeKind.Removed, RelationshipStrength.Strong, true, depth: 2).Should().Be(DescriptorImpactSeverity.High);

    [Fact] public void Depth2_Weak_Runtime_BoostAfterAttenuation()
        => Compute(DescriptorChangeKind.Removed, RelationshipStrength.Weak, true, depth: 2).Should().Be(DescriptorImpactSeverity.Medium);

    [Fact] public void IsAdvisory_SupersededBy_ReturnsTrue()
    {
        var edge = new DescriptorEdge
        {
            Index = 0, From = new("a", "x"), To = new("a", "y"),
            Kind = RelationshipKind.DependsOn, Role = RelationshipRoles.SupersededBy,
            Strength = RelationshipStrength.Weak, IsRuntimeBinding = false
        };
        DescriptorImpactAnalyzer.IsAdvisory(edge).Should().BeTrue();
    }

    [Fact] public void IsAdvisory_RuntimeBinding_ReturnsFalse()
    {
        var edge = new DescriptorEdge
        {
            Index = 0, From = new("a", "x"), To = new("a", "y"),
            Kind = RelationshipKind.References, Role = RelationshipRoles.SubWorkflowStep,
            Strength = RelationshipStrength.Weak, IsRuntimeBinding = true
        };
        DescriptorImpactAnalyzer.IsAdvisory(edge).Should().BeFalse();
    }

    [Fact] public void IsAdvisory_StrongReferences_ReturnsFalse()
    {
        var edge = new DescriptorEdge
        {
            Index = 0, From = new("a", "x"), To = new("a", "y"),
            Kind = RelationshipKind.References, Role = null,
            Strength = RelationshipStrength.Strong, IsRuntimeBinding = false
        };
        DescriptorImpactAnalyzer.IsAdvisory(edge).Should().BeFalse();
    }
}
