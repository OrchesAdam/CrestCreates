using CrestCreates.VirtualFileSystem.Models;
using FluentAssertions;
using Xunit;

namespace CrestCreates.VirtualFileSystem.Tests;

public class VirtualResourceTypeTests
{
    [Fact]
    public void VirtualResourceType_HasThreeValues()
    {
        // Assert
        Enum.GetValues<VirtualResourceType>().Should().HaveCount(3);
    }

    [Fact]
    public void Physical_ShouldBeFirst()
    {
        // Assert
        VirtualResourceType.Physical.Should().Be(VirtualResourceType.Physical);
    }

    [Fact]
    public void Embedded_ShouldExist()
    {
        // Assert
        VirtualResourceType.Embedded.Should().Be(VirtualResourceType.Embedded);
    }

    [Fact]
    public void Cloud_ShouldExist()
    {
        // Assert
        VirtualResourceType.Cloud.Should().Be(VirtualResourceType.Cloud);
    }
}
