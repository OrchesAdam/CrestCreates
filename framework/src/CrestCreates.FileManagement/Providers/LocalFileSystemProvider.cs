using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.FileManagement.Configuration;
using CrestCreates.FileManagement.Models;

namespace CrestCreates.FileManagement.Providers;

public class LocalFileSystemProvider : IFileStorageProvider
{
    private readonly string _rootPath;
    private readonly LocalFileSystemOptions _options;
    private static readonly Dictionary<string, string> ContentTypeMap = new()
    {
        {".jpg", "image/jpeg"},
        {".jpeg", "image/jpeg"},
        {".png", "image/png"},
        {".gif", "image/gif"},
        {".pdf", "application/pdf"},
        {".doc", "application/msword"},
        {".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"},
        {".xls", "application/vnd.ms-excel"},
        {".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"},
        {".txt", "text/plain"},
        {".html", "text/html"},
        {".css", "text/css"},
        {".js", "application/javascript"},
        {".json", "application/json"},
        {".xml", "application/xml"},
        {".zip", "application/zip"},
    };

    public string ProviderName => "LocalFileSystem";

    public LocalFileSystemProvider(LocalFileSystemOptions options)
    {
        _options = options;
        _rootPath = options.UseAbsolutePath
            ? options.RootPath
            : Path.Combine(Directory.GetCurrentDirectory(), options.RootPath);

        if (!Directory.Exists(_rootPath))
            Directory.CreateDirectory(_rootPath);
    }

    public async Task<string> UploadAsync(Stream stream, FileEntity entity, CancellationToken ct = default)
    {
        var storageKey = entity.Key.ToStorageKey();
        var physicalPath = GetPhysicalPath(storageKey);

        var directory = Path.GetDirectoryName(physicalPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        await using var fileStream = new FileStream(physicalPath, FileMode.Create, FileAccess.Write);
        await stream.CopyToAsync(fileStream, ct);

        return storageKey;
    }

    public Task<Stream> DownloadAsync(string storageKey, CancellationToken ct = default)
    {
        var physicalPath = GetPhysicalPath(storageKey);
        if (!File.Exists(physicalPath))
            throw new FileNotFoundException($"File not found: {storageKey}");

        Stream stream = new FileStream(physicalPath, FileMode.Open, FileAccess.Read);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var physicalPath = GetPhysicalPath(storageKey);
        if (File.Exists(physicalPath))
            File.Delete(physicalPath);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default)
    {
        var physicalPath = GetPhysicalPath(storageKey);
        return Task.FromResult(File.Exists(physicalPath));
    }

    public Task<IStorageMetadata> GetMetadataAsync(string storageKey, CancellationToken ct = default)
    {
        var physicalPath = GetPhysicalPath(storageKey);
        var fileInfo = new FileInfo(physicalPath);

        if (!fileInfo.Exists)
            throw new FileNotFoundException($"File not found: {storageKey}");

        var metadata = new LocalStorageMetadata
        {
            Size = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc,
            ContentType = GetContentType(fileInfo.Extension)
        };

        return Task.FromResult<IStorageMetadata>(metadata);
    }

    private string GetPhysicalPath(string storageKey)
    {
        var hash = ComputeSha256Hash(storageKey);
        return Path.Combine(_rootPath, ".vfs", $"{hash}.dat");
    }

    private static string ComputeSha256Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private string GetContentType(string extension)
    {
        return ContentTypeMap.TryGetValue(extension.ToLower(), out var contentType)
            ? contentType
            : "application/octet-stream";
    }
}