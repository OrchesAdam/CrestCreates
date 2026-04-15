using System;

namespace CrestCreates.FileManagement.Providers;

public interface ICloudStorageProvider : IFileStorageProvider
{
    string GeneratePresignedUrl(string storageKey, TimeSpan expiry);
}