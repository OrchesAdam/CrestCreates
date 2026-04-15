using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.FileManagement.Configuration;
using CrestCreates.FileManagement.Models;
using CrestCreates.FileManagement.Providers;
using Microsoft.Extensions.Logging;

namespace CrestCreates.FileManagement.Services;

public class FileManagementService : IFileManagementService
{
    private readonly IFileStorageProvider _storageProvider;
    private readonly IFileUrlService _urlService;
    private readonly FileValidationOptions _validationOptions;
    private readonly FileUrlOptions _urlOptions;
    private readonly ILogger<FileManagementService> _logger;

    public FileManagementService(
        IFileStorageProvider storageProvider,
        IFileUrlService urlService,
        FileValidationOptions validationOptions,
        FileUrlOptions urlOptions,
        ILogger<FileManagementService> logger)
    {
        _storageProvider = storageProvider;
        _urlService = urlService;
        _validationOptions = validationOptions;
        _urlOptions = urlOptions;
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

        // Validate
        ValidateFileName(fileName);

        var extension = Path.GetExtension(fileName);
        var fileKey = FileKey.Create(effectiveTenantId, extension);

        var entity = new FileEntity
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

        await _storageProvider.UploadAsync(stream, entity, ct);
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
        _logger.LogInformation("Deleted file {StorageKey} for tenant {TenantId}", key.ToStorageKey(), tenantId);
    }

    public Task<FileEntity?> GetAsync(FileKey key, Guid tenantId, CancellationToken ct = default)
    {
        ValidateTenantAccess(key.TenantId, tenantId);
        // Would query repository if implemented
        return Task.FromResult<FileEntity?>(null);
    }

    public Task<IEnumerable<FileEntity>> ListAsync(Guid tenantId, int? year = null, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required", nameof(tenantId));

        // Would query repository if implemented
        _ = year; // Suppress unused warning until repository is implemented
        return Task.FromResult<IEnumerable<FileEntity>>(Array.Empty<FileEntity>());
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

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        return string.IsNullOrEmpty(name) ? fileName : name;
    }
}