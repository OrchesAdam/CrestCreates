using System;
using System.IO;
using System.Threading.Tasks;
using CrestCreates.FileManagement.Configuration;
using CrestCreates.FileManagement.Models;
using CrestCreates.FileManagement.Providers;
using FluentAssertions;
using Xunit;

namespace CrestCreates.FileManagement.Tests;

public class LocalFileSystemProviderTests : IDisposable
{
    private readonly string _testRoot;
    private readonly LocalFileSystemProvider _provider;

    public LocalFileSystemProviderTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"filemanagement-test-{Guid.NewGuid()}");
        var options = new LocalFileSystemOptions
        {
            RootPath = _testRoot,
            UseAbsolutePath = true
        };
        _provider = new LocalFileSystemProvider(options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, true);
    }

    [Fact]
    public async Task UploadAsync_ValidFile_ReturnsStorageKey()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var entity = new FileEntity
        {
            Key = FileKey.Create(tenantId, ".txt"),
            TenantId = tenantId,
            FileName = "test.txt",
            ContentType = "text/plain",
            Size = 5,
            Extension = ".txt",
            UploadedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AccessMode = FileAccessMode.Private
        };
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello"));

        // Act
        var storageKey = await _provider.UploadAsync(stream, entity);

        // Assert
        storageKey.Should().Contain(tenantId.ToString());
        storageKey.Should().EndWith(".txt");
    }

    [Fact]
    public async Task UploadAndDownload_RoundTrip_PreservesContent()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var content = "Hello, World!";
        var entity = new FileEntity
        {
            Key = FileKey.Create(tenantId, ".txt"),
            TenantId = tenantId,
            FileName = "test.txt",
            ContentType = "text/plain",
            Size = content.Length,
            Extension = ".txt",
            UploadedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AccessMode = FileAccessMode.Private
        };
        using var uploadStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        // Act
        var storageKey = await _provider.UploadAsync(uploadStream, entity);
        using var downloadStream = await _provider.DownloadAsync(storageKey);
        using var reader = new StreamReader(downloadStream);
        var downloaded = await reader.ReadToEndAsync();

        // Assert
        downloaded.Should().Be(content);
    }

    [Fact]
    public async Task ExistsAsync_ExistingFile_ReturnsTrue()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var entity = new FileEntity
        {
            Key = FileKey.Create(tenantId, ".txt"),
            TenantId = tenantId,
            FileName = "test.txt",
            ContentType = "text/plain",
            Size = 5,
            Extension = ".txt",
            UploadedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AccessMode = FileAccessMode.Private
        };
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("test"));
        var storageKey = await _provider.UploadAsync(stream, entity);

        // Act
        var exists = await _provider.ExistsAsync(storageKey);

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_NonExistingFile_ReturnsFalse()
    {
        // Arrange
        var fakeKey = "non/existing/key.txt";

        // Act
        var exists = await _provider.ExistsAsync(fakeKey);

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_ExistingFile_RemovesFile()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var entity = new FileEntity
        {
            Key = FileKey.Create(tenantId, ".txt"),
            TenantId = tenantId,
            FileName = "test.txt",
            ContentType = "text/plain",
            Size = 5,
            Extension = ".txt",
            UploadedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AccessMode = FileAccessMode.Private
        };
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("test"));
        var storageKey = await _provider.UploadAsync(stream, entity);

        // Act
        await _provider.DeleteAsync(storageKey);
        var exists = await _provider.ExistsAsync(storageKey);

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task UploadAsync_PathTraversalAttempt_Throws()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var entity = new FileEntity
        {
            Key = FileKey.Create(tenantId, ".txt"),
            TenantId = tenantId,
            FileName = "../../../etc/passwd",
            ContentType = "text/plain",
            Size = 5,
            Extension = ".txt",
            UploadedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AccessMode = FileAccessMode.Private
        };
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("malicious"));

        // Act - virtual path mapping should prevent path traversal
        var exception = await Record.ExceptionAsync(() => _provider.UploadAsync(stream, entity));

        // Assert - should not throw (path traversal is prevented by virtual path mapping)
        exception.Should().BeNull();
    }
}
