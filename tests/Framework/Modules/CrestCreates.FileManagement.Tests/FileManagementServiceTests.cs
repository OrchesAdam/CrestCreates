using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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

    [Fact]
    public async Task UploadAsync_NonSeekableStreamOverLimit_RejectsAndCompensates()
    {
        var provider = new RecordingStorageProvider();
        var service = CreateService(provider, new InMemoryFileRepository(), maxFileSize: 100);
        await using var stream = new NonSeekableStream(new byte[200]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UploadAsync(stream, "big.bin", "application/octet-stream", tenantId: _tenantId));

        Assert.Equal(1, provider.DeleteCalls);
        Assert.Null(provider.StoredValue);
    }

    [Fact]
    public async Task UploadAsync_NonSeekableStreamUnderLimit_RecordsActualSize()
    {
        var provider = new RecordingStorageProvider();
        var service = CreateService(provider, new InMemoryFileRepository(), maxFileSize: 100);
        var payload = System.Text.Encoding.UTF8.GetBytes("hello");
        await using var stream = new NonSeekableStream(payload);

        var entity = await service.UploadAsync(stream, "small.bin", "application/octet-stream", tenantId: _tenantId);

        Assert.Equal(payload.Length, entity.Size);
        Assert.Equal(payload.Length, provider.StoredValue!.Length);
    }

    [Fact]
    public async Task UploadAsync_MetadataFailure_RemovesStoredObjectAndPreservesRootCause()
    {
        var provider = new RecordingStorageProvider();
        var service = CreateService(provider, new ThrowingFileRepository(), maxFileSize: 100);
        await using var stream = new MemoryStream(new byte[10]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UploadAsync(stream, "small.bin", "application/octet-stream", tenantId: _tenantId));

        Assert.Equal("metadata failure", exception.Message);
        Assert.Equal(1, provider.DeleteCalls);
        Assert.Null(provider.StoredValue);
    }

    [Fact]
    public async Task UploadAsync_MetadataAndCleanupFailure_PreservesMetadataRootCause()
    {
        var provider = new RecordingStorageProvider { FailDelete = true };
        var service = CreateService(provider, new ThrowingFileRepository(), maxFileSize: 100);
        await using var stream = new MemoryStream(new byte[10]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UploadAsync(stream, "small.bin", "application/octet-stream", tenantId: _tenantId));

        Assert.Equal("metadata failure", exception.Message);
        Assert.Equal(1, provider.DeleteCalls);
    }

    [Fact]
    public async Task UploadAsync_Success_LeavesCallerStreamOpen()
    {
        var service = CreateService(new RecordingStorageProvider(), new InMemoryFileRepository(), maxFileSize: 100);
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello"));

        var entity = await service.UploadAsync(stream, "reuse.bin", "application/octet-stream", tenantId: _tenantId);

        Assert.True(stream.CanRead);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        Assert.Equal("hello", await reader.ReadToEndAsync());
        Assert.Equal(5, entity.Size);
    }

    [Fact]
    public async Task UploadAsync_Overage_LeavesCallerStreamOpen()
    {
        var provider = new RecordingStorageProvider();
        var service = CreateService(provider, new InMemoryFileRepository(), maxFileSize: 100);
        await using var stream = new NonSeekableStream(new byte[200]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UploadAsync(stream, "big.bin", "application/octet-stream", tenantId: _tenantId));

        Assert.False(stream.Disposed);
        Assert.Equal(1, provider.DeleteCalls);
    }

    [Fact]
    public async Task UploadAsync_MetadataFailure_LeavesCallerStreamOpen()
    {
        var service = CreateService(new RecordingStorageProvider(), new ThrowingFileRepository(), maxFileSize: 100);
        var stream = new MemoryStream(new byte[10]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UploadAsync(stream, "small.bin", "application/octet-stream", tenantId: _tenantId));

        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task UploadAsync_Success_DoesNotObserveCallerDisposeFailure()
    {
        var service = CreateService(new RecordingStorageProvider(), new InMemoryFileRepository(), maxFileSize: 100);
        var stream = new ThrowingDisposeStream(new byte[7]);

        var entity = await service.UploadAsync(stream, "dispose.bin", "application/octet-stream", tenantId: _tenantId);

        Assert.Equal(7, entity.Size);
    }

    [Fact]
    public async Task DeleteAsync_RepeatedDelete_IsIdempotent()
    {
        using var stream = new MemoryStream(new byte[10]);
        var entity = await _service.UploadAsync(stream, "repeat.bin", "application/octet-stream", tenantId: _tenantId);
        await _service.DeleteAsync(entity.Key, _tenantId);
        await _service.DeleteAsync(entity.Key, _tenantId);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private FileManagementService CreateService(IFileStorageProvider provider, IFileRepository repository, long maxFileSize)
        => new(provider, repository, new FileUrlService(new FileUrlOptions { BaseUrl = "/files" }),
            new FileValidationOptions { MaxFileSize = maxFileSize, AllowedExtensions = Array.Empty<string>() },
            NullLogger<FileManagementService>.Instance);

    private sealed class NonSeekableStream : Stream
    {
        private readonly MemoryStream _inner;
        public NonSeekableStream(byte[] payload) => _inner = new MemoryStream(payload);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _inner.Read(buffer);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { Disposed = true; _inner.Dispose(); }
            base.Dispose(disposing);
        }
    }

    private sealed class ThrowingDisposeStream : MemoryStream
    {
        public ThrowingDisposeStream(byte[] payload) : base(payload) { }
        protected override void Dispose(bool disposing) => throw new InvalidOperationException("caller dispose failure");
        public override ValueTask DisposeAsync() => ValueTask.FromException(new InvalidOperationException("caller dispose failure"));
    }

    private sealed class RecordingStorageProvider : IFileStorageProvider
    {
        public string ProviderName => "Recording";
        public byte[]? StoredValue { get; private set; }
        public int DeleteCalls { get; private set; }
        public bool FailDelete { get; init; }

        public async Task<string> UploadAsync(Stream stream, FileEntity entity, CancellationToken ct = default)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            StoredValue = buffer.ToArray();
            return entity.Key.ToStorageKey();
        }

        public Task<Stream> DownloadAsync(string storageKey, CancellationToken ct = default)
            => Task.FromResult<Stream>(new MemoryStream(StoredValue ?? Array.Empty<byte>()));

        public Task DeleteAsync(string storageKey, CancellationToken ct = default)
        {
            DeleteCalls++;
            if (FailDelete) throw new IOException("cleanup failure");
            StoredValue = null;
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default)
            => Task.FromResult(StoredValue is not null);

        public Task<IStorageMetadata> GetMetadataAsync(string storageKey, CancellationToken ct = default)
            => Task.FromResult<IStorageMetadata>(new LocalStorageMetadata
            {
                Size = StoredValue?.Length ?? 0,
                LastModified = DateTimeOffset.UtcNow,
                ContentType = "application/octet-stream"
            });
    }

    private sealed class ThrowingFileRepository : IFileRepository
    {
        public Task<FileEntity?> GetByKeyAsync(FileKey key, CancellationToken ct = default) => Task.FromResult<FileEntity?>(null);
        public Task<IEnumerable<FileEntity>> ListAsync(Guid tenantId, int? year = null, CancellationToken ct = default)
            => Task.FromResult<IEnumerable<FileEntity>>(Array.Empty<FileEntity>());
        public Task CreateAsync(FileEntity entity, CancellationToken ct = default)
            => throw new InvalidOperationException("metadata failure");
        public Task UpdateAsync(FileEntity entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(FileKey key, CancellationToken ct = default) => Task.CompletedTask;
    }
}
