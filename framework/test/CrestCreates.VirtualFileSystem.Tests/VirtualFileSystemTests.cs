using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.VirtualFileSystem.Models;
using CrestCreates.VirtualFileSystem.Providers;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.VirtualFileSystem.Tests;

public class VirtualFileSystemTests
{
    private readonly Services.VirtualFileSystem _vfs;
    private readonly Mock<IVirtualFileProvider> _mockProvider;

    public VirtualFileSystemTests()
    {
        _vfs = new Services.VirtualFileSystem();
        _mockProvider = new Mock<IVirtualFileProvider>();
        _mockProvider.Setup(p => p.ProviderName).Returns("Mock");
        _mockProvider.Setup(p => p.ResourceType).Returns(VirtualResourceType.Physical);
    }

    [Fact]
    public void RegisterModule_AddsModuleProvider()
    {
        // Act
        _vfs.RegisterModule("testmodule", _mockProvider.Object);

        // Assert
        _vfs.GetRegisteredModules().Should().Contain("testmodule");
    }

    [Fact]
    public void GetRegisteredModules_ReturnsAllRegisteredModules()
    {
        // Arrange
        var mockProvider1 = new Mock<IVirtualFileProvider>();
        var mockProvider2 = new Mock<IVirtualFileProvider>();
        _vfs.RegisterModule("module1", mockProvider1.Object);
        _vfs.RegisterModule("module2", mockProvider2.Object);

        // Act
        var modules = _vfs.GetRegisteredModules().ToList();

        // Assert
        modules.Should().HaveCount(2);
        modules.Should().Contain(new[] { "module1", "module2" });
    }

    [Fact]
    public async Task GetFileAsync_WithFullPath_ParsesAndRetrievesFile()
    {
        // Arrange
        var virtualPath = VirtualPath.Create("testmodule", "test.txt");
        var mockFile = new Mock<IVirtualFile>();
        mockFile.Setup(f => f.Path).Returns(virtualPath);
        _mockProvider.Setup(p => p.GetFileAsync(virtualPath, default)).ReturnsAsync(mockFile.Object);
        _vfs.RegisterModule("testmodule", _mockProvider.Object);

        // Act
        var result = await _vfs.GetFileAsync("testmodule/test.txt");

        // Assert
        result.Should().Be(mockFile.Object);
    }

    [Fact]
    public async Task GetFileAsync_WithVirtualPath_RetrievesFile()
    {
        // Arrange
        var virtualPath = VirtualPath.Create("testmodule", "test.txt");
        var mockFile = new Mock<IVirtualFile>();
        mockFile.Setup(f => f.Path).Returns(virtualPath);
        _mockProvider.Setup(p => p.GetFileAsync(virtualPath, default)).ReturnsAsync(mockFile.Object);
        _vfs.RegisterModule("testmodule", _mockProvider.Object);

        // Act
        var result = await _vfs.GetFileAsync(virtualPath);

        // Assert
        result.Should().Be(mockFile.Object);
    }

    [Fact]
    public async Task GetFileAsync_WithUnregisteredModule_ReturnsNull()
    {
        // Arrange - no registration needed, uses fallback

        // Act
        var result = await _vfs.GetFileAsync("unregistered/test.txt");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetFileAsync_WithInvalidFullPath_ReturnsNull()
    {
        // Act
        var result = await _vfs.GetFileAsync("InvalidPathNoSlash");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ExistsAsync_WithFullPath_ParsesAndChecksExistence()
    {
        // Arrange
        var virtualPath = VirtualPath.Create("testmodule", "test.txt");
        _mockProvider.Setup(p => p.ExistsAsync(virtualPath, default)).ReturnsAsync(true);
        _vfs.RegisterModule("testmodule", _mockProvider.Object);

        // Act
        var result = await _vfs.ExistsAsync("testmodule/test.txt");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_WithVirtualPath_ChecksExistence()
    {
        // Arrange
        var virtualPath = VirtualPath.Create("testmodule", "test.txt");
        _mockProvider.Setup(p => p.ExistsAsync(virtualPath, default)).ReturnsAsync(true);
        _vfs.RegisterModule("testmodule", _mockProvider.Object);

        // Act
        var result = await _vfs.ExistsAsync(virtualPath);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_WithUnregisteredModule_ReturnsFalse()
    {
        // Act
        var result = await _vfs.ExistsAsync("unregistered/test.txt");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_WithInvalidFullPath_ReturnsFalse()
    {
        // Act
        var result = await _vfs.ExistsAsync("InvalidPathNoSlash");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetFilesAsync_RetrievesFilesFromModule()
    {
        // Arrange
        var directory = VirtualPath.Create("testmodule", ".");
        var virtualPath1 = VirtualPath.Create("testmodule", "file1.txt");
        var virtualPath2 = VirtualPath.Create("testmodule", "file2.txt");

        var mockFile1 = new Mock<IVirtualFile>();
        mockFile1.Setup(f => f.Path).Returns(virtualPath1);
        var mockFile2 = new Mock<IVirtualFile>();
        mockFile2.Setup(f => f.Path).Returns(virtualPath2);

        var files = new List<IVirtualFile> { mockFile1.Object, mockFile2.Object };
        _mockProvider.Setup(p => p.GetFilesAsync(directory, "*", false, default)).ReturnsAsync(files);
        _vfs.RegisterModule("testmodule", _mockProvider.Object);

        // Act
        var results = (await _vfs.GetFilesAsync("testmodule", ".")).ToList();

        // Assert
        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetFilesAsync_WithUnregisteredModule_ReturnsEmpty()
    {
        // Act
        var results = await _vfs.GetFilesAsync("unregistered", ".");

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFilesAsync_PassesSearchPatternAndRecursiveFlag()
    {
        // Arrange
        var directory = VirtualPath.Create("testmodule", "Templates");
        _mockProvider.Setup(p => p.GetFilesAsync(directory, "*.cs", true, default)).ReturnsAsync(new List<IVirtualFile>());
        _vfs.RegisterModule("testmodule", _mockProvider.Object);

        // Act
        await _vfs.GetFilesAsync("testmodule", "Templates", "*.cs", recursive: true);

        // Assert
        _mockProvider.Verify(p => p.GetFilesAsync(directory, "*.cs", true, default), Times.Once);
    }

    [Fact]
    public async Task GetFileAsync_CaseInsensitiveModuleName()
    {
        // Arrange
        var virtualPath = VirtualPath.Create("testmodule", "test.txt");
        var mockFile = new Mock<IVirtualFile>();
        mockFile.Setup(f => f.Path).Returns(virtualPath);
        _mockProvider.Setup(p => p.GetFileAsync(virtualPath, default)).ReturnsAsync(mockFile.Object);
        _vfs.RegisterModule("TestModule", _mockProvider.Object);

        // Act
        var result = await _vfs.GetFileAsync("TESTMODULE/test.txt");

        // Assert
        result.Should().Be(mockFile.Object);
    }

    [Fact]
    public async Task RegisterModule_AddsToExistingModuleProvider()
    {
        // Arrange
        var mockProvider1 = new Mock<IVirtualFileProvider>();
        mockProvider1.Setup(p => p.ProviderName).Returns("Provider1");
        var mockProvider2 = new Mock<IVirtualFileProvider>();
        mockProvider2.Setup(p => p.ProviderName).Returns("Provider2");

        _vfs.RegisterModule("testmodule", mockProvider1.Object);
        _vfs.RegisterModule("testmodule", mockProvider2.Object);

        // Act
        var virtualPath = VirtualPath.Create("testmodule", "test.txt");
        mockProvider1.Setup(p => p.GetFileAsync(virtualPath, default)).ReturnsAsync((IVirtualFile?)null);
        var mockFile = new Mock<IVirtualFile>();
        mockFile.Setup(f => f.Path).Returns(virtualPath);
        mockProvider2.Setup(p => p.GetFileAsync(virtualPath, default)).ReturnsAsync(mockFile.Object);

        // Both providers should be tried
        var result = await _vfs.GetFileAsync(virtualPath);

        // Assert
        result.Should().Be(mockFile.Object);
        mockProvider1.Verify(p => p.GetFileAsync(virtualPath, default), Times.Once);
        mockProvider2.Verify(p => p.GetFileAsync(virtualPath, default), Times.Once);
    }
}