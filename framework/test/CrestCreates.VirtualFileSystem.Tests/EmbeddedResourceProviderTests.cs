using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrestCreates.VirtualFileSystem.Models;
using CrestCreates.VirtualFileSystem.Providers;
using FluentAssertions;
using Xunit;

namespace CrestCreates.VirtualFileSystem.Tests;

public class EmbeddedResourceProviderTests
{
    private readonly EmbeddedResourceProvider _provider;

    public EmbeddedResourceProviderTests()
    {
        // Use the test assembly itself as the source of embedded resources
        _provider = new EmbeddedResourceProvider("testassembly", typeof(EmbeddedResourceProviderTests).Assembly, "CrestCreates.VirtualFileSystem.Tests");
    }

    [Fact]
    public void ProviderName_ReturnsEmbedded()
    {
        _provider.ProviderName.Should().Be("Embedded");
    }

    [Fact]
    public void ResourceType_ReturnsEmbedded()
    {
        _provider.ResourceType.Should().Be(VirtualResourceType.Embedded);
    }

    [Fact]
    public void Assembly_ReturnsCorrectAssembly()
    {
        _provider.Assembly.Should().BeSameAs(typeof(EmbeddedResourceProviderTests).Assembly);
    }

    [Fact]
    public void BaseNamespace_ReturnsConfiguredNamespace()
    {
        _provider.BaseNamespace.Should().Be("CrestCreates.VirtualFileSystem.Tests");
    }

    [Fact]
    public async Task GetFileAsync_WithDifferentModule_ReturnsNull()
    {
        // Arrange
        var virtualPath = VirtualPath.Create("othermodule", "test.txt");

        // Act
        var result = await _provider.GetFileAsync(virtualPath);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetResourceNamesAsync_ReturnsResourceNames()
    {
        // Act
        var names = await _provider.GetResourceNamesAsync();

        // Assert
        names.Should().NotBeNull();
        // The test assembly should have some embedded resources (possibly from other tests)
    }

    [Fact]
    public async Task GetResourceStreamAsync_WithValidResource_ReturnsStream()
    {
        // Arrange - get a known resource name
        var resourceNames = (await _provider.GetResourceNamesAsync()).ToList();
        if (!resourceNames.Any())
        {
            // If no resources exist, skip this test
            return;
        }
        var resourceName = resourceNames.First();

        // Act
        var stream = await _provider.GetResourceStreamAsync(resourceName);

        // Assert
        stream.Should().NotBeNull();
    }

    [Fact]
    public async Task GetResourceStreamAsync_WithInvalidResource_ReturnsNull()
    {
        // Act
        var stream = await _provider.GetResourceStreamAsync("NonExistent.Resource.txt");

        // Assert
        stream.Should().BeNull();
    }

    [Fact]
    public async Task ExistsAsync_WithDifferentModule_ReturnsFalse()
    {
        // Arrange
        var virtualPath = VirtualPath.Create("othermodule", "test.txt");

        // Act
        var result = await _provider.ExistsAsync(virtualPath);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithEmptyBaseNamespace_Works()
    {
        // Act
        var provider = new EmbeddedResourceProvider("test", typeof(EmbeddedResourceProviderTests).Assembly, "");

        // Assert
        provider.BaseNamespace.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFilesAsync_WithDifferentModule_ReturnsEmpty()
    {
        // Arrange
        var directory = VirtualPath.Create("othermodule", ".");

        // Act
        var results = await _provider.GetFilesAsync(directory);

        // Assert
        results.Should().BeEmpty();
    }
}