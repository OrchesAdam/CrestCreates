using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.VirtualFileSystem.Models;
using CrestCreates.VirtualFileSystem.Providers;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.VirtualFileSystem.Tests;

public class ModuleResourceProviderTests
{
    private readonly Mock<IVirtualFileProvider> _mockProvider1;
    private readonly Mock<IVirtualFileProvider> _mockProvider2;
    private readonly ModuleResourceProvider _moduleProvider;

    public ModuleResourceProviderTests()
    {
        _moduleProvider = new ModuleResourceProvider("testmodule");
        _mockProvider1 = new Mock<IVirtualFileProvider>();
        _mockProvider1.Setup(p => p.ProviderName).Returns("Provider1");
        _mockProvider1.Setup(p => p.ResourceType).Returns(VirtualResourceType.Physical);

        _mockProvider2 = new Mock<IVirtualFileProvider>();
        _mockProvider2.Setup(p => p.ProviderName).Returns("Provider2");
        _mockProvider2.Setup(p => p.ResourceType).Returns(VirtualResourceType.Embedded);
    }

    [Fact]
    public void ProviderName_ReturnsModule()
    {
        _moduleProvider.ProviderName.Should().Be("Module");
    }

    [Fact]
    public void ResourceType_ReturnsZeroForComposite()
    {
        _moduleProvider.ResourceType.Should().Be(0);
    }

    [Fact]
    public async Task AddProvider_IncreasesProviderCount()
    {
        // Arrange
        var initialCount = _moduleProvider.GetType().GetField("_providers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act
        _moduleProvider.AddProvider(_mockProvider1.Object);
        _moduleProvider.AddProvider(_mockProvider2.Object);

        // Assert - verify providers were added (via behavior)
        var virtualPath = VirtualPath.Create("testmodule", "test.txt");
        _mockProvider1.Setup(p => p.GetFileAsync(virtualPath, default)).ReturnsAsync((IVirtualFile?)null);
        _mockProvider2.Setup(p => p.GetFileAsync(virtualPath, default)).ReturnsAsync((IVirtualFile?)null);

        await _moduleProvider.GetFileAsync(virtualPath, default);

        _mockProvider1.Verify(p => p.GetFileAsync(virtualPath, default), Times.Once);
        _mockProvider2.Verify(p => p.GetFileAsync(virtualPath, default), Times.Once);
    }

    [Fact]
    public async Task GetFileAsync_WithDifferentModule_ReturnsNull()
    {
        // Arrange
        var virtualPath = VirtualPath.Create("othermodule", "test.txt");

        // Act
        var result = await _moduleProvider.GetFileAsync(virtualPath);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetFileAsync_TriesProvidersInOrder()
    {
        // Arrange
        var virtualPath = VirtualPath.Create("testmodule", "test.txt");
        var mockFile = new Mock<IVirtualFile>();
        mockFile.Setup(f => f.Path).Returns(virtualPath);

        _mockProvider1.Setup(p => p.GetFileAsync(virtualPath, default)).ReturnsAsync((IVirtualFile?)null);
        _mockProvider2.Setup(p => p.GetFileAsync(virtualPath, default)).ReturnsAsync(mockFile.Object);

        _moduleProvider.AddProvider(_mockProvider1.Object);
        _moduleProvider.AddProvider(_mockProvider2.Object);

        // Act
        var result = await _moduleProvider.GetFileAsync(virtualPath);

        // Assert
        result.Should().Be(mockFile.Object);
        _mockProvider1.Verify(p => p.GetFileAsync(virtualPath, default), Times.Once);
        _mockProvider2.Verify(p => p.GetFileAsync(virtualPath, default), Times.Once);
    }

    [Fact]
    public async Task GetFileAsync_StopsAtFirstFoundFile()
    {
        // Arrange
        var virtualPath = VirtualPath.Create("testmodule", "test.txt");
        var mockFile = new Mock<IVirtualFile>();
        mockFile.Setup(f => f.Path).Returns(virtualPath);

        _mockProvider1.Setup(p => p.GetFileAsync(virtualPath, default)).ReturnsAsync(mockFile.Object);
        // Provider2 should NOT be called since Provider1 already found the file

        _moduleProvider.AddProvider(_mockProvider1.Object);
        _moduleProvider.AddProvider(_mockProvider2.Object);

        // Act
        var result = await _moduleProvider.GetFileAsync(virtualPath);

        // Assert
        result.Should().Be(mockFile.Object);
        _mockProvider2.Verify(p => p.GetFileAsync(virtualPath, default), Times.Never);
    }

    [Fact]
    public async Task GetFileAsync_WithNoProviders_ReturnsNull()
    {
        // Arrange
        var emptyProvider = new ModuleResourceProvider("testmodule");
        var virtualPath = VirtualPath.Create("testmodule", "test.txt");

        // Act
        var result = await emptyProvider.GetFileAsync(virtualPath);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ExistsAsync_WithDifferentModule_ReturnsFalse()
    {
        // Arrange
        var virtualPath = VirtualPath.Create("othermodule", "test.txt");

        // Act
        var result = await _moduleProvider.ExistsAsync(virtualPath);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_WithExistingFile_ReturnsTrue()
    {
        // Arrange
        var virtualPath = VirtualPath.Create("testmodule", "test.txt");
        _mockProvider1.Setup(p => p.ExistsAsync(virtualPath, default)).ReturnsAsync(false);
        _mockProvider2.Setup(p => p.ExistsAsync(virtualPath, default)).ReturnsAsync(true);

        _moduleProvider.AddProvider(_mockProvider1.Object);
        _moduleProvider.AddProvider(_mockProvider2.Object);

        // Act
        var result = await _moduleProvider.ExistsAsync(virtualPath);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_WithNoExistingFile_ReturnsFalse()
    {
        // Arrange
        var virtualPath = VirtualPath.Create("testmodule", "test.txt");
        _mockProvider1.Setup(p => p.ExistsAsync(virtualPath, default)).ReturnsAsync(false);
        _mockProvider2.Setup(p => p.ExistsAsync(virtualPath, default)).ReturnsAsync(false);

        _moduleProvider.AddProvider(_mockProvider1.Object);
        _moduleProvider.AddProvider(_mockProvider2.Object);

        // Act
        var result = await _moduleProvider.ExistsAsync(virtualPath);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetFilesAsync_DeduplicatesByVirtualPath()
    {
        // Arrange
        var directory = VirtualPath.Create("testmodule", ".");
        var virtualPath1 = VirtualPath.Create("testmodule", "file1.txt");
        var virtualPath2 = VirtualPath.Create("testmodule", "file2.txt");

        var mockFile1 = new Mock<IVirtualFile>();
        mockFile1.Setup(f => f.Path).Returns(virtualPath1);
        var mockFile2 = new Mock<IVirtualFile>();
        mockFile2.Setup(f => f.Path).Returns(virtualPath2);

        var files1 = new List<IVirtualFile> { mockFile1.Object };
        var files2 = new List<IVirtualFile> { mockFile1.Object, mockFile2.Object }; // duplicate

        _mockProvider1.Setup(p => p.GetFilesAsync(directory, "*", false, default)).ReturnsAsync(files1);
        _mockProvider2.Setup(p => p.GetFilesAsync(directory, "*", false, default)).ReturnsAsync(files2);

        _moduleProvider.AddProvider(_mockProvider1.Object);
        _moduleProvider.AddProvider(_mockProvider2.Object);

        // Act
        var results = (await _moduleProvider.GetFilesAsync(directory)).ToList();

        // Assert
        results.Should().HaveCount(2); // deduplicated
    }

    [Fact]
    public async Task GetFilesAsync_WithDifferentModule_ReturnsEmpty()
    {
        // Arrange
        var directory = VirtualPath.Create("othermodule", ".");

        // Act
        var results = await _moduleProvider.GetFilesAsync(directory);

        // Assert
        results.Should().BeEmpty();
    }
}