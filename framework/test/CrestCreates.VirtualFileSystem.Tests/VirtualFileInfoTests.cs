using System.Text;
using CrestCreates.VirtualFileSystem.Models;
using FluentAssertions;
using Xunit;

namespace CrestCreates.VirtualFileSystem.Tests;

public class VirtualFileInfoTests
{
    [Fact]
    public void Constructor_WithValidArguments_SetsProperties()
    {
        // Arrange
        var path = VirtualPath.Create("MyModule", "test.txt");
        var lastModified = DateTimeOffset.UtcNow;
        Func<CancellationToken, Task<Stream>> openReadFunc = _ => Task.FromResult<Stream>(new MemoryStream());

        // Act
        var fileInfo = new VirtualFileInfo(
            path,
            openReadFunc,
            "test.txt",
            "text/plain",
            1024,
            lastModified,
            VirtualResourceType.Physical);

        // Assert
        fileInfo.Path.Should().Be(path);
        fileInfo.FileName.Should().Be("test.txt");
        fileInfo.ContentType.Should().Be("text/plain");
        fileInfo.Length.Should().Be(1024);
        fileInfo.LastModified.Should().Be(lastModified);
        fileInfo.ResourceType.Should().Be(VirtualResourceType.Physical);
    }

    [Fact]
    public void Constructor_WithNullOpenReadFunc_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new VirtualFileInfo(
            VirtualPath.Create("MyModule", "test.txt"),
            null!,
            "test.txt");

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithMessage("*openReadFunc*");
    }

    [Fact]
    public void Constructor_WithDefaultLastModified_UsesUtcNow()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow;

        // Act
        var fileInfo = new VirtualFileInfo(
            VirtualPath.Create("MyModule", "test.txt"),
            _ => Task.FromResult<Stream>(new MemoryStream()),
            "test.txt");

        var after = DateTimeOffset.UtcNow;

        // Assert
        fileInfo.LastModified.Should().BeOnOrAfter(before);
        fileInfo.LastModified.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void Constructor_WithDefaultResourceType_UsesPhysical()
    {
        // Act
        var fileInfo = new VirtualFileInfo(
            VirtualPath.Create("MyModule", "test.txt"),
            _ => Task.FromResult<Stream>(new MemoryStream()),
            "test.txt");

        // Assert
        fileInfo.ResourceType.Should().Be(VirtualResourceType.Physical);
    }

    [Fact]
    public async Task OpenReadAsync_CallsOpenReadFunc()
    {
        // Arrange
        var expectedContent = "Hello, World!";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(expectedContent));
        Func<CancellationToken, Task<Stream>> openReadFunc = _ => Task.FromResult<Stream>(stream);

        var fileInfo = new VirtualFileInfo(
            VirtualPath.Create("MyModule", "test.txt"),
            openReadFunc,
            "test.txt");

        // Act
        var resultStream = await fileInfo.OpenReadAsync();

        // Assert
        var reader = new StreamReader(resultStream);
        var content = await reader.ReadToEndAsync();
        content.Should().Be(expectedContent);
    }

    [Fact]
    public async Task OpenReadAsync_PassesCancellationToken()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var tokenReceived = false;
        Func<CancellationToken, Task<Stream>> openReadFunc = token =>
        {
            tokenReceived = token == cts.Token;
            return Task.FromResult<Stream>(new MemoryStream());
        };

        var fileInfo = new VirtualFileInfo(
            VirtualPath.Create("MyModule", "test.txt"),
            openReadFunc,
            "test.txt");

        // Act
        await fileInfo.OpenReadAsync(cts.Token);

        // Assert
        tokenReceived.Should().BeTrue();
    }

    [Fact]
    public void VirtualFileInfo_AsIVirtualFile_CanBeCast()
    {
        // Arrange
        IVirtualFile virtualFile = new VirtualFileInfo(
            VirtualPath.Create("MyModule", "test.txt"),
            _ => Task.FromResult<Stream>(new MemoryStream()),
            "test.txt");

        // Assert
        virtualFile.Should().NotBeNull();
        virtualFile.FileName.Should().Be("test.txt");
        virtualFile.ResourceType.Should().Be(VirtualResourceType.Physical);
    }
}
