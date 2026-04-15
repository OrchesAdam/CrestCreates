using CrestCreates.VirtualFileSystem.Models;
using FluentAssertions;
using Xunit;

namespace CrestCreates.VirtualFileSystem.Tests;

public class VirtualPathTests
{
    [Fact]
    public void Create_WithValidInputs_ReturnsVirtualPath()
    {
        // Act
        var path = VirtualPath.Create("CodeGenerator", "Templates/Entity.txt");

        // Assert
        path.ModuleName.Should().Be("codegenerator");
        path.RelativePath.Should().Be("Templates/Entity.txt");
        path.FullPath.Should().Be("codegenerator/Templates/Entity.txt");
    }

    [Fact]
    public void Create_WithBackslash_NormalizesToForwardSlash()
    {
        // Act
        var path = VirtualPath.Create("MyModule", "path\\to\\file.txt");

        // Assert
        path.RelativePath.Should().Be("path/to/file.txt");
    }

    [Fact]
    public void Create_WithLeadingSlash_TrimsLeadingSlash()
    {
        // Act
        var path = VirtualPath.Create("MyModule", "/relative/path.txt");

        // Assert
        path.RelativePath.Should().Be("relative/path.txt");
    }

    [Fact]
    public void Create_WithNullModuleName_ThrowsArgumentException()
    {
        // Act
        var act = () => VirtualPath.Create(null!, "path.txt");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("Module name is required*");
    }

    [Fact]
    public void Create_WithEmptyModuleName_ThrowsArgumentException()
    {
        // Act
        var act = () => VirtualPath.Create("", "path.txt");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("Module name is required*");
    }

    [Fact]
    public void Create_WithNullRelativePath_ThrowsArgumentException()
    {
        // Act
        var act = () => VirtualPath.Create("MyModule", null!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("Relative path is required*");
    }

    [Fact]
    public void Create_WithEmptyRelativePath_ThrowsArgumentException()
    {
        // Act
        var act = () => VirtualPath.Create("MyModule", "");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("Relative path is required*");
    }

    [Fact]
    public void Create_ModuleNameIsLowercased()
    {
        // Act
        var path = VirtualPath.Create("MyModule", "path.txt");

        // Assert
        path.ModuleName.Should().Be("mymodule");
    }

    [Fact]
    public void Parse_WithValidFullPath_ReturnsVirtualPath()
    {
        // Act
        var path = VirtualPath.Parse("MyModule/relative/path.txt");

        // Assert
        path.Should().NotBeNull();
        path!.Value.ModuleName.Should().Be("mymodule");
        path.Value.RelativePath.Should().Be("relative/path.txt");
    }

    [Fact]
    public void Parse_WithoutSlash_ReturnsNull()
    {
        // Act
        var path = VirtualPath.Parse("NoSlashPath");

        // Assert
        path.Should().BeNull();
    }

    [Fact]
    public void Parse_WithEmptyString_ReturnsNull()
    {
        // Act
        var path = VirtualPath.Parse("");

        // Assert
        path.Should().BeNull();
    }

    [Fact]
    public void Parse_WithNull_ReturnsNull()
    {
        // Act
        var path = VirtualPath.Parse(null!);

        // Assert
        path.Should().BeNull();
    }

    [Fact]
    public void IsChildOf_WithChildPath_ReturnsTrue()
    {
        // Arrange
        var parent = VirtualPath.Create("MyModule", "parent");
        var child = VirtualPath.Create("MyModule", "parent/child/file.txt");

        // Act & Assert
        child.IsChildOf(parent).Should().BeTrue();
    }

    [Fact]
    public void IsChildOf_WithNonChildPath_ReturnsFalse()
    {
        // Arrange
        var parent = VirtualPath.Create("MyModule", "parent");
        var other = VirtualPath.Create("MyModule", "other/path");

        // Act & Assert
        other.IsChildOf(parent).Should().BeFalse();
    }

    [Fact]
    public void IsChildOf_WithDifferentModule_ReturnsFalse()
    {
        // Arrange
        var parent = VirtualPath.Create("ModuleA", "parent");
        var child = VirtualPath.Create("ModuleB", "parent/child");

        // Act & Assert
        child.IsChildOf(parent).Should().BeFalse();
    }

    [Fact]
    public void FullPath_CombinesModuleAndRelative()
    {
        // Arrange
        var path = VirtualPath.Create("CodeGenerator", "Templates/Entity.txt");

        // Assert
        path.FullPath.Should().Be("codegenerator/Templates/Entity.txt");
    }

    [Fact]
    public void VirtualPath_RecordsAreEqual_WhenSameValues()
    {
        // Arrange
        var path1 = VirtualPath.Create("MyModule", "path.txt");
        var path2 = VirtualPath.Create("MyModule", "path.txt");

        // Assert
        path1.Should().Be(path2);
    }
}
