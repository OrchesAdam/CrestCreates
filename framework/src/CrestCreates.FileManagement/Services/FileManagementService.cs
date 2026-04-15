using Microsoft.AspNetCore.Http;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.FileManagement.Providers;
using CrestCreates.FileManagement.Configuration;
using CrestCreates.FileManagement.Models;

namespace CrestCreates.FileManagement.Services;

public class FileManagementService : IFileManagementService
{
    private readonly IFileStorageProvider _storageProvider;
    private readonly FileValidationOptions _validationOptions;
    private readonly FileUrlOptions _urlOptions;

    public FileManagementService(
        IFileStorageProvider storageProvider,
        FileValidationOptions validationOptions,
        FileUrlOptions urlOptions)
    {
        _storageProvider = storageProvider;
        _validationOptions = validationOptions;
        _urlOptions = urlOptions;
    }

    public async Task<string> UploadFileAsync(IFormFile file, string directory = "")
    {
        if (!ValidateFile(file))
            throw new InvalidOperationException("File validation failed");

        var extension = Path.GetExtension(file.FileName);
        var fileKey = FileKey.Create(Guid.Empty, extension); // TenantId should be resolved from context
        var entity = new FileEntity
        {
            Key = fileKey,
            TenantId = Guid.Empty,
            FileName = file.FileName,
            ContentType = file.ContentType,
            Size = file.Length,
            Extension = extension,
            UploadedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await using var stream = file.OpenReadStream();
        return await _storageProvider.UploadAsync(stream, entity);
    }

    public async Task<string> UploadStreamAsync(Stream stream, string fileName, string directory = "")
    {
        if (!ValidateFileName(fileName))
            throw new InvalidOperationException("File name validation failed");

        var extension = Path.GetExtension(fileName);
        var fileKey = FileKey.Create(Guid.Empty, extension);
        var entity = new FileEntity
        {
            Key = fileKey,
            TenantId = Guid.Empty,
            FileName = fileName,
            ContentType = "application/octet-stream",
            Size = 0,
            Extension = extension,
            UploadedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        return await _storageProvider.UploadAsync(stream, entity);
    }

    public async Task<byte[]> DownloadFileAsync(string filePath)
    {
        var stream = await _storageProvider.DownloadAsync(filePath);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }

    public async Task DownloadToStreamAsync(string filePath, Stream stream)
    {
        var fileStream = await _storageProvider.DownloadAsync(filePath);
        await fileStream.CopyToAsync(stream);
    }

    public Task DeleteFileAsync(string filePath)
    {
        return _storageProvider.DeleteAsync(filePath);
    }

    public Task<bool> FileExistsAsync(string filePath)
    {
        return _storageProvider.ExistsAsync(filePath);
    }

    public async Task<FileInformation> GetFileInfoAsync(string filePath)
    {
        var metadata = await _storageProvider.GetMetadataAsync(filePath);
        var fileKey = FileKey.Parse(filePath);

        return new FileInformation
        {
            Path = filePath,
            Name = fileKey?.FileGuid.ToString() ?? Path.GetFileName(filePath),
            Size = metadata.Size,
            CreatedAt = metadata.LastModified.UtcDateTime,
            LastModified = metadata.LastModified.UtcDateTime,
            ContentType = metadata.ContentType
        };
    }

    public Task<string> GetFileUrlAsync(string filePath)
    {
        if (_urlOptions.UseAbsoluteUrl && !string.IsNullOrEmpty(_urlOptions.AbsoluteUrlPrefix))
        {
            return Task.FromResult($"{_urlOptions.AbsoluteUrlPrefix}{_urlOptions.BaseUrl}/{filePath}".Replace("//", "/"));
        }

        return Task.FromResult($"{_urlOptions.BaseUrl}/{filePath}".Replace("//", "/"));
    }

    public bool ValidateFile(IFormFile file)
    {
        if (file.Length > _validationOptions.MaxFileSize)
            return false;

        if (_validationOptions.AllowedExtensions.Length > 0)
        {
            var extension = Path.GetExtension(file.FileName).ToLower();
            if (!Array.Exists(_validationOptions.AllowedExtensions, ext => ext.Equals(extension)))
                return false;
        }

        return true;
    }

    private bool ValidateFileName(string fileName)
    {
        if (_validationOptions.AllowedExtensions.Length > 0)
        {
            var extension = Path.GetExtension(fileName).ToLower();
            if (!Array.Exists(_validationOptions.AllowedExtensions, ext => ext.Equals(extension)))
                return false;
        }

        return true;
    }
}