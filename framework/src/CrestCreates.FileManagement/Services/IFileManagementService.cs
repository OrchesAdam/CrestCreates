using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.FileManagement.Models;

namespace CrestCreates.FileManagement.Services;

public interface IFileManagementService
{
    Task<FileEntity> UploadAsync(
        Stream stream,
        string fileName,
        string contentType,
        FileAccessMode accessMode = FileAccessMode.Private,
        FileMetadata? metadata = null,
        Guid? tenantId = null,
        CancellationToken ct = default);

    Task<Stream> DownloadAsync(
        FileKey key,
        Guid tenantId,
        CancellationToken ct = default);

    Task DeleteAsync(
        FileKey key,
        Guid tenantId,
        CancellationToken ct = default);

    Task<FileEntity?> GetAsync(
        FileKey key,
        Guid tenantId,
        CancellationToken ct = default);

    Task<IEnumerable<FileEntity>> ListAsync(
        Guid tenantId,
        int? year = null,
        CancellationToken ct = default);

    string GetUrl(FileEntity entity, TimeSpan? presignedExpiry = null);
}