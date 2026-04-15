using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CrestCreates.VirtualFileSystem.Models;
using CrestCreates.VirtualFileSystem.Providers;
using FluentAssertions;
using Xunit;

namespace CrestCreates.VirtualFileSystem.Tests;

public class PhysicalFileProviderTests : IDisposable
{
    private readonly string _testBasePath;
    private readonly PhysicalFileProvider _provider;

    public PhysicalFileProviderTests()
    {
        _testBasePath = Path.Combine(Path.GetTempPath(), $"vfs_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testBasePath);
        _provider = new PhysicalFileProvider("testmodule", _testBasePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testBasePath))
        {
            Directory.Delete(_testBasePath, true);
        }
    }

    [Fact]
    public async Task GetFileAsync_WithExistingFile_ReturnsVirtualFile()
    {
        // Arrange
        var filePath = Path.Combine(_testBasePath, "test.txt");
        await File.WriteAllTextAsync(filePath, "Hello World");
        var virtualPath = VirtualPath.Create("testmodule", "test.txt");

        // Act
        var result = await _provider.GetFileAsync(virtualPath);

        // Assert
        result.Should().NotBeNull();
        result!.FileName.Should().Be("test.txt");
        result.ContentType.Should().Be("text/plain");
        result.Length.Should().Be(11);
    }

    [Fact]
    public async Task GetFileAsync_WithNonExistingFile_ReturnsNull()
    {
        // Arrange
        var virtualPath = VirtualPath.Create("testmodule", "nonexistent.txt");

        // Act
        var result = await _provider.GetFileAsync(virtualPath);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetFileAsync_WithDifferentModule_ReturnsNull()
    {
        // Arrange
        var filePath = Path.Combine(_testBasePath, "test.txt");
        await File.WriteAllTextAsync(filePath, "Hello");
        var virtualPath = VirtualPath.Create("othermodule", "test.txt");

        // Act
        var result = await _provider.GetFileAsync(virtualPath);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetFileAsync_CanReadFileContent()
    {
        // Arrange
        var filePath = Path.Combine(_testBasePath, "test.txt");
        var content = "Hello World";
        await File.WriteAllTextAsync(filePath, content);
        var virtualPath = VirtualPath.Create("testmodule", "test.txt");

        // Act
        var file = await _provider.GetFileAsync(virtualPath);
        using var stream = await file!.OpenReadAsync();
        using var reader = new StreamReader(stream);

        // Assert
        var result = await reader.ReadToEndAsync();
        result.Should().Be(content);
    }

    [Fact]
    public async Task ExistsAsync_WithExistingFile_ReturnsTrue()
    {
        // Arrange
        var filePath = Path.Combine(_testBasePath, "test.txt");
        await File.WriteAllTextAsync(filePath, "Hello");
        var virtualPath = VirtualPath.Create("testmodule", "test.txt");

        // Act
        var result = await _provider.ExistsAsync(virtualPath);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_WithNonExistingFile_ReturnsFalse()
    {
        // Arrange
        var virtualPath = VirtualPath.Create("testmodule", "nonexistent.txt");

        // Act
        var result = await _provider.ExistsAsync(virtualPath);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_WithDifferentModule_ReturnsFalse()
    {
        // Arrange
        var filePath = Path.Combine(_testBasePath, "test.txt");
        await File.WriteAllTextAsync(filePath, "Hello");
        var virtualPath = VirtualPath.Create("othermodule", "test.txt");

        // Act
        var result = await _provider.ExistsAsync(virtualPath);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetFilesAsync_ReturnsAllFilesInDirectory()
    {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_testBasePath, "file1.txt"), "Content1");
        await File.WriteAllTextAsync(Path.Combine(_testBasePath, "file2.txt"), "Content2");
        var directory = VirtualPath.Create("testmodule", ".");

        // Act
        var results = (await _provider.GetFilesAsync(directory)).ToList();

        // Assert
        results.Should().HaveCount(2);
        results.Select(f => f.FileName).Should().Contain(new[] { "file1.txt", "file2.txt" });
    }

    [Fact]
    public async Task GetFilesAsync_WithSearchPattern_ReturnsFilteredFiles()
    {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_testBasePath, "file1.txt"), "Content1");
        await File.WriteAllTextAsync(Path.Combine(_testBasePath, "file2.json"), "Content2");
        var directory = VirtualPath.Create("testmodule", ".");

        // Act
        var results = (await _provider.GetFilesAsync(directory, "*.txt")).ToList();

        // Assert
        results.Should().HaveCount(1);
        results[0].FileName.Should().Be("file1.txt");
    }

    [Fact]
    public async Task GetFilesAsync_Recursive_ReturnsFilesInSubdirectories()
    {
        // Arrange
        var subDir = Path.Combine(_testBasePath, "subdir");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(_testBasePath, "root.txt"), "Root");
        await File.WriteAllTextAsync(Path.Combine(subDir, "nested.txt"), "Nested");
        var directory = VirtualPath.Create("testmodule", ".");

        // Act
        var results = (await _provider.GetFilesAsync(directory, "*", recursive: true)).ToList();

        // Assert
        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetFilesAsync_NonRecursive_DoesNotReturnSubdirectoryFiles()
    {
        // Arrange
        var subDir = Path.Combine(_testBasePath, "subdir");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(_testBasePath, "root.txt"), "Root");
        await File.WriteAllTextAsync(Path.Combine(subDir, "nested.txt"), "Nested");
        var directory = VirtualPath.Create("testmodule", ".");

        // Act
        var results = (await _provider.GetFilesAsync(directory, "*", recursive: false)).ToList();

        // Assert
        results.Should().HaveCount(1);
        results[0].FileName.Should().Be("root.txt");
    }

    [Fact]
    public void ProviderName_ReturnsPhysical()
    {
        _provider.ProviderName.Should().Be("Physical");
    }

    [Fact]
    public void ResourceType_ReturnsPhysical()
    {
        _provider.ResourceType.Should().Be(VirtualResourceType.Physical);
    }

    [Theory]
    [InlineData(".txt", "text/plain")]
    [InlineData(".json", "application/json")]
    [InlineData(".xml", "application/xml")]
    [InlineData(".html", "text/html")]
    [InlineData(".css", "text/css")]
    [InlineData(".js", "application/javascript")]
    [InlineData(".cs", "text/plain")]
    [InlineData(".md", "text/markdown")]
    [InlineData(".pdf", "application/pdf")]
    [InlineData(".unknown", "application/octet-stream")]
    public async Task GetFileAsync_ReturnsCorrectContentType(string extension, string expectedContentType)
    {
        // Arrange
        var filePath = Path.Combine(_testBasePath, $"test{extension}");
        await File.WriteAllTextAsync(filePath, "Content");
        var virtualPath = VirtualPath.Create("testmodule", $"test{extension}");

        // Act
        var result = await _provider.GetFileAsync(virtualPath);

        // Assert
        result!.ContentType.Should().Be(expectedContentType);
    }
}