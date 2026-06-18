using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.FileManagement.Models;

namespace CrestCreates.FileManagement.Providers;

public interface IFileStorageProvider
{
    string ProviderName { get; }

    Task<string> UploadAsync(
        Stream stream,
        FileEntity entity,
        CancellationToken ct = default);

    Task<Stream> DownloadAsync(
        string storageKey,
        CancellationToken ct = default);

    Task DeleteAsync(
        string storageKey,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(
        string storageKey,
        CancellationToken ct = default);

    Task<IStorageMetadata> GetMetadataAsync(
        string storageKey,
        CancellationToken ct = default);
}