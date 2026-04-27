using System;
using CrestCreates.FileManagement.Configuration;
using CrestCreates.FileManagement.Models;

namespace CrestCreates.FileManagement.Services;

public class FileUrlService : IFileUrlService
{
    private readonly FileUrlOptions _options;

    public FileUrlService(FileUrlOptions options)
    {
        _options = options;
    }

    public string GetPublicUrl(FileEntity entity)
    {
        var storageKey = entity.Key.ToStorageKey();
        var basePath = _options.UseAbsoluteUrl && !string.IsNullOrEmpty(_options.AbsoluteUrlPrefix)
            ? _options.AbsoluteUrlPrefix.TrimEnd('/')
            : _options.BaseUrl.TrimEnd('/');

        return $"{basePath}/{storageKey}";
    }

    public string GetPresignedUrl(FileEntity entity, TimeSpan expiry)
    {
        throw new NotSupportedException("Presigned URLs are only available through cloud storage providers. Use IFileManagementService.GetUrl() instead.");
    }

    public string? GetStorageKeyFromUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        var basePath = _options.UseAbsoluteUrl && !string.IsNullOrEmpty(_options.AbsoluteUrlPrefix)
            ? _options.AbsoluteUrlPrefix.TrimEnd('/')
            : _options.BaseUrl.TrimEnd('/');

        if (!url.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase))
            return null;

        return url.Substring(basePath.Length + 1);
    }
}
