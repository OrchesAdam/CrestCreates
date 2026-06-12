using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class RelationshipKindExtensionTests
{
    [Fact]
    public void RelationshipKind_Includes_Uses()
    {
        Enum.GetValues<RelationshipKind>().Should().Contain(RelationshipKind.Uses);
    }

    [Fact]
    public void RelationshipKind_Includes_Triggers()
    {
        Enum.GetValues<RelationshipKind>().Should().Contain(RelationshipKind.Triggers);
    }

    [Fact]
    public void RelationshipKind_Has_Six_Values()
    {
        Enum.GetValues<RelationshipKind>().Should().HaveCount(6);
    }
}
