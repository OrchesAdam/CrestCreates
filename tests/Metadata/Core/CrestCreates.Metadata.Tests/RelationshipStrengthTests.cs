using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class RelationshipStrengthTests
{
    [Fact]
    public void RelationshipStrength_Has_Strong_And_Weak_Values()
    {
        Enum.GetValues<RelationshipStrength>().Should().Contain(RelationshipStrength.Strong);
        Enum.GetValues<RelationshipStrength>().Should().Contain(RelationshipStrength.Weak);
    }

    [Fact]
    public void RelationshipStrength_Defaults_To_Strong()
    {
        var rel = new CrestCreates.Metadata.Abstractions.DescriptorRelationship.DescriptorRelationship(
            new DescriptorRef("test", "a"),
            new DescriptorRef("test", "b"),
            RelationshipKind.References);

        rel.Strength.Should().Be(RelationshipStrength.Strong);
    }
}
