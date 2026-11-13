using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.FileManagement.Configuration;
using CrestCreates.FileManagement.Models;
using CrestCreates.FileManagement.Providers;
using CrestCreates.FileManagement.Repositories;
using Microsoft.Extensions.Logging;

namespace CrestCreates.FileManagement.Services;

public class FileManagementService : IFileManagementService
{
    private readonly IFileStorageProvider _storageProvider;
    private readonly IFileRepository _repository;
    private readonly IFileUrlService _urlService;
    private readonly FileValidationOptions _validationOptions;
    private readonly ILogger<FileManagementService> _logger;

    public FileManagementService(
        IFileStorageProvider storageProvider,
        IFileRepository repository,
        IFileUrlService urlService,
        FileValidationOptions validationOptions,
        ILogger<FileManagementService> logger)
    {
        _storageProvider = storageProvider;
        _repository = repository;
        _urlService = urlService;
        _validationOptions = validationOptions;
        _logger = logger;
    }

    public async Task<FileEntity> UploadAsync(
        Stream stream,
        string fileName,
        string contentType,
        FileAccessMode accessMode = FileAccessMode.Private,
        FileMetadata? metadata = null,
        Guid? tenantId = null,
        CancellationToken ct = default)
    {
        var effectiveTenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId));

        ValidateFileName(fileName);
        ValidateExtension(fileName);
        ValidateFileSize(stream);

        var extension = Path.GetExtension(fileName);
        var fileKey = FileKey.Create(effectiveTenantId, extension);

        var provisional = new FileEntity
        {
            Key = fileKey,
            TenantId = effectiveTenantId,
            FileName = SanitizeFileName(fileName),
            ContentType = contentType,
            Size = stream.CanSeek ? stream.Length : 0,
            Extension = extension,
            UploadedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AccessMode = accessMode
        };
        var storageKey = fileKey.ToStorageKey();

        await using var limited = new SizeLimitingReadStream(stream, _validationOptions.MaxFileSize);
        try
        {
            await _storageProvider.UploadAsync(limited, provisional, ct);
        }
        catch
        {
            await TryDeleteAsync(storageKey);
            throw;
        }

        // Size comes from bytes actually read, including non-seekable request bodies.
        var entity = provisional with { Size = limited.BytesRead };
        try
        {
            await _repository.CreateAsync(entity, ct);
        }
        catch
        {
            await TryDeleteAsync(storageKey);
            throw;
        }

        _logger.LogInformation("Uploaded file {FileName} for tenant {TenantId}", fileName, effectiveTenantId);
        return entity;
    }

    public async Task<Stream> DownloadAsync(FileKey key, Guid tenantId, CancellationToken ct = default)
    {
        ValidateTenantAccess(key.TenantId, tenantId);
        return await _storageProvider.DownloadAsync(key.ToStorageKey(), ct);
    }

    public async Task DeleteAsync(FileKey key, Guid tenantId, CancellationToken ct = default)
    {
        ValidateTenantAccess(key.TenantId, tenantId);
        await _storageProvider.DeleteAsync(key.ToStorageKey(), ct);
        await _repository.DeleteAsync(key, ct);
        _logger.LogInformation("Deleted file {StorageKey} for tenant {TenantId}", key.ToStorageKey(), tenantId);
    }

    public async Task<FileEntity?> GetAsync(FileKey key, Guid tenantId, CancellationToken ct = default)
    {
        ValidateTenantAccess(key.TenantId, tenantId);
        return await _repository.GetByKeyAsync(key, ct);
    }

    public async Task<IEnumerable<FileEntity>> ListAsync(Guid tenantId, int? year = null, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required", nameof(tenantId));

        return await _repository.ListAsync(tenantId, year, ct);
    }

    public string GetUrl(FileEntity entity, TimeSpan? presignedExpiry = null)
    {
        if (entity.AccessMode == FileAccessMode.Private && presignedExpiry.HasValue)
        {
            if (_storageProvider is ICloudStorageProvider cloudProvider)
            {
                return cloudProvider.GeneratePresignedUrl(entity.Key.ToStorageKey(), presignedExpiry.Value);
            }
            throw new NotSupportedException("Presigned URLs not supported for local storage provider");
        }

        return _urlService.GetPublicUrl(entity);
    }

    private void ValidateTenantAccess(Guid fileTenantId, Guid requestTenantId)
    {
        if (fileTenantId != requestTenantId)
            throw new UnauthorizedAccessException("Tenant access denied");
    }

    private void ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name is required", nameof(fileName));

        if (fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
            throw new ArgumentException("Invalid file name", nameof(fileName));
    }

    private void ValidateExtension(string fileName)
    {
        if (_validationOptions.AllowedExtensions is { Length: > 0 })
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (Array.IndexOf(_validationOptions.AllowedExtensions, extension) < 0)
                throw new ArgumentException($"File extension '{extension}' is not allowed", nameof(fileName));
        }
    }

    private void ValidateFileSize(Stream stream)
    {
        if (stream.CanSeek && stream.Length > _validationOptions.MaxFileSize)
            throw new ArgumentException($"File size exceeds the maximum allowed size of {_validationOptions.MaxFileSize} bytes", nameof(stream));
    }

    private async Task TryDeleteAsync(string storageKey)
    {
        try
        {
            await _storageProvider.DeleteAsync(storageKey, CancellationToken.None);
        }
        catch (Exception cleanupFailure)
        {
            _logger.LogWarning(cleanupFailure,
                "Failed to remove storage object {StorageKey} after an unsuccessful upload.", storageKey);
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        return string.IsNullOrEmpty(name) ? fileName : name;
    }
}
internal sealed class SizeLimitingReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _limit;

    public long BytesRead { get; private set; }

    public SizeLimitingReadStream(Stream inner, long limit)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        _inner = inner;
        _limit = limit;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException("The limited upload stream has no seekable length.");
    public override long Position
    {
        get => BytesRead;
        set => throw new NotSupportedException("The limited upload stream is not seekable.");
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        Count(read);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Count(read);
        return read;
    }

    private void Count(int read)
    {
        if (read <= 0) return;
        BytesRead += read;
        if (BytesRead > _limit)
            throw new ArgumentException(
                $"File size exceeds the maximum allowed size of {_limit} bytes", nameof(_inner));
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync() => _inner.DisposeAsync();
}
