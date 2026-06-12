using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorRelationshipEnhancementTests
{
    [Fact]
    public void DescriptorRelationship_Has_Role_Property()
    {
        var rel = new DescriptorRelationship(
            new DescriptorRef("test", "a"),
            new DescriptorRef("test", "b"),
            RelationshipKind.References,
            Role: "InputSchema");

        rel.Role.Should().Be("InputSchema");
    }

    [Fact]
    public void DescriptorRelationship_Has_SourcePath_Property()
    {
        var rel = new DescriptorRelationship(
            new DescriptorRef("test", "a"),
            new DescriptorRef("test", "b"),
            RelationshipKind.References,
            SourcePath: "InputSchema");

        rel.SourcePath.Should().Be("InputSchema");
    }

    [Fact]
    public void DescriptorRelationship_Has_Strength_Property()
    {
        var rel = new DescriptorRelationship(
            new DescriptorRef("test", "a"),
            new DescriptorRef("test", "b"),
            RelationshipKind.References,
            Strength: RelationshipStrength.Weak);

        rel.Strength.Should().Be(RelationshipStrength.Weak);
    }

    [Fact]
    public void DescriptorRelationship_Has_IsRuntimeBinding_Property()
    {
        var rel = new DescriptorRelationship(
            new DescriptorRef("test", "a"),
            new DescriptorRef("test", "b"),
            RelationshipKind.References,
            IsRuntimeBinding: true);

        rel.IsRuntimeBinding.Should().BeTrue();
    }

    [Fact]
    public void DescriptorRelationship_IsRuntimeBinding_Defaults_To_False()
    {
        var rel = new DescriptorRelationship(
            new DescriptorRef("test", "a"),
            new DescriptorRef("test", "b"),
            RelationshipKind.References);

        rel.IsRuntimeBinding.Should().BeFalse();
    }

    [Fact]
    public void DescriptorRelationship_Role_Defaults_To_Null()
    {
        var rel = new DescriptorRelationship(
            new DescriptorRef("test", "a"),
            new DescriptorRef("test", "b"),
            RelationshipKind.References);

        rel.Role.Should().BeNull();
    }
}
