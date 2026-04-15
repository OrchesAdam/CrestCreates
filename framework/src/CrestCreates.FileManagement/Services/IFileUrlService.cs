using System;
using CrestCreates.FileManagement.Models;

namespace CrestCreates.FileManagement.Services;

public interface IFileUrlService
{
    string GetPublicUrl(FileEntity entity);

    string GetPresignedUrl(FileEntity entity, TimeSpan expiry);

    string? GetStorageKeyFromUrl(string url);
}