using System;
using System.IO;
using System.Threading.Tasks;
using CrestCreates.FileManagement.Configuration;
using CrestCreates.FileManagement.Models;
using CrestCreates.FileManagement.Providers;
using CrestCreates.FileManagement.Repositories;
using CrestCreates.FileManagement.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrestCreates.FileManagement.Tests;

public class FileManagementServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileManagementService _service;
    private readonly Guid _tenantId = Guid.NewGuid();

    public FileManagementServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fm_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var options = new LocalFileSystemOptions { RootPath = _tempDir, UseAbsolutePath = true };
        var validation = new FileValidationOptions { MaxFileSize = 1024 * 1024, AllowedExtensions = Array.Empty<string>() };
        var urlOptions = new FileUrlOptions { BaseUrl = "/files" };

        var storageProvider = new LocalFileSystemProvider(options);
        var repository = new InMemoryFileRepository();
        var urlService = new FileUrlService(urlOptions);
        var logger = NullLogger<FileManagementService>.Instance;

        _service = new FileManagementService(storageProvider, repository, urlService, validation, logger);
    }

    [Fact]
    public async Task UploadAsync_CreatesFileEntity()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Hello World"));

        var entity = await _service.UploadAsync(stream, "test.txt", "text/plain", tenantId: _tenantId);

        Assert.NotNull(entity);
        Assert.Equal(_tenantId, entity.TenantId);
        Assert.Equal("test.txt", entity.FileName);
        Assert.Equal("text/plain", entity.ContentType);
    }

    [Fact]
    public async Task UploadAsync_ThenGetAsync_ReturnsEntity()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Hello World"));

        var uploaded = await _service.UploadAsync(stream, "test.txt", "text/plain", tenantId: _tenantId);
        var retrieved = await _service.GetAsync(uploaded.Key, _tenantId);

        Assert.NotNull(retrieved);
        Assert.Equal(uploaded.Key.ToStorageKey(), retrieved.Key.ToStorageKey());
    }

    [Fact]
    public async Task UploadAsync_ThenListAsync_ReturnsEntities()
    {
        using var stream1 = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("File 1"));
        using var stream2 = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("File 2"));

        await _service.UploadAsync(stream1, "file1.txt", "text/plain", tenantId: _tenantId);
        await _service.UploadAsync(stream2, "file2.txt", "text/plain", tenantId: _tenantId);

        var list = await _service.ListAsync(_tenantId);
        Assert.Equal(2, list.Count());
    }

    [Fact]
    public async Task UploadAsync_WithDisallowedExtension_Throws()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Hello"));
        var options = new LocalFileSystemOptions { RootPath = _tempDir, UseAbsolutePath = true };
        var validation = new FileValidationOptions { AllowedExtensions = new[] { ".pdf" } };
        var urlOptions = new FileUrlOptions { BaseUrl = "/files" };

        var service = new FileManagementService(
            new LocalFileSystemProvider(options),
            new InMemoryFileRepository(),
            new FileUrlService(urlOptions),
            validation,
            NullLogger<FileManagementService>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UploadAsync(stream, "test.exe", "application/octet-stream", tenantId: _tenantId));
    }

    [Fact]
    public async Task UploadAsync_WithOversizedFile_Throws()
    {
        using var stream = new MemoryStream(new byte[1024]);
        var options = new LocalFileSystemOptions { RootPath = _tempDir, UseAbsolutePath = true };
        var validation = new FileValidationOptions { MaxFileSize = 100 };
        var urlOptions = new FileUrlOptions { BaseUrl = "/files" };

        var service = new FileManagementService(
            new LocalFileSystemProvider(options),
            new InMemoryFileRepository(),
            new FileUrlService(urlOptions),
            validation,
            NullLogger<FileManagementService>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UploadAsync(stream, "big.txt", "text/plain", tenantId: _tenantId));
    }

    [Fact]
    public async Task DeleteAsync_RemovesFile()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("To delete"));
        var uploaded = await _service.UploadAsync(stream, "delete.txt", "text/plain", tenantId: _tenantId);

        await _service.DeleteAsync(uploaded.Key, _tenantId);

        var retrieved = await _service.GetAsync(uploaded.Key, _tenantId);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task GetAsync_WithWrongTenant_Throws()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Hello"));
        var uploaded = await _service.UploadAsync(stream, "test.txt", "text/plain", tenantId: _tenantId);

        var otherTenant = Guid.NewGuid();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetAsync(uploaded.Key, otherTenant));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
