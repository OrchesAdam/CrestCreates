using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using CrestCreates.FileManagement.Configuration;
using CrestCreates.FileManagement.Models;

namespace CrestCreates.FileManagement.Providers;

public class AzureBlobStorageProvider : ICloudStorageProvider
{
    private readonly AzureBlobStorageOptions _options;
    private readonly BlobServiceClient _blobServiceClient;

    public string ProviderName => "AzureBlobStorage";

    public AzureBlobStorageProvider(AzureBlobStorageOptions options)
    {
        _options = options;
        _blobServiceClient = new BlobServiceClient(options.ConnectionString);
    }

    public async Task<string> UploadAsync(Stream stream, FileEntity entity, CancellationToken ct = default)
    {
        var containerClient = await GetContainerClientAsync(entity.TenantId, ct);
        var blobClient = containerClient.GetBlobClient(GetBlobName(entity.Key));

        var blobHttpHeaders = new BlobHttpHeaders
        {
            ContentType = entity.ContentType
        };

        await blobClient.UploadAsync(stream, new BlobUploadOptions
        {
            HttpHeaders = blobHttpHeaders
        }, ct);

        return entity.Key.ToStorageKey();
    }

    public async Task<Stream> DownloadAsync(string storageKey, CancellationToken ct = default)
    {
        var fileKey = FileKey.Parse(storageKey)
            ?? throw new ArgumentException($"Invalid storage key: {storageKey}", nameof(storageKey));

        var containerClient = await GetContainerClientAsync(fileKey.TenantId, ct);
        var blobClient = containerClient.GetBlobClient(GetBlobName(fileKey));

        var response = await blobClient.DownloadStreamingAsync(cancellationToken: ct);
        return response.Value.Content;
    }

    public async Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var fileKey = FileKey.Parse(storageKey)
            ?? throw new ArgumentException($"Invalid storage key: {storageKey}", nameof(storageKey));

        var containerClient = await GetContainerClientAsync(fileKey.TenantId, ct);
        var blobClient = containerClient.GetBlobClient(GetBlobName(fileKey));
        await blobClient.DeleteIfExistsAsync(cancellationToken: ct);
    }

    public async Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default)
    {
        var fileKey = FileKey.Parse(storageKey)
            ?? throw new ArgumentException($"Invalid storage key: {storageKey}", nameof(storageKey));

        var containerClient = await GetContainerClientAsync(fileKey.TenantId, ct);
        var blobClient = containerClient.GetBlobClient(GetBlobName(fileKey));
        return await blobClient.ExistsAsync(ct);
    }

    public async Task<IStorageMetadata> GetMetadataAsync(string storageKey, CancellationToken ct = default)
    {
        var fileKey = FileKey.Parse(storageKey)
            ?? throw new ArgumentException($"Invalid storage key: {storageKey}", nameof(storageKey));

        var containerClient = await GetContainerClientAsync(fileKey.TenantId, ct);
        var blobClient = containerClient.GetBlobClient(GetBlobName(fileKey));
        var response = await blobClient.GetPropertiesAsync(cancellationToken: ct);

        return new LocalStorageMetadata
        {
            Size = response.Value.ContentLength,
            LastModified = response.Value.LastModified,
            ContentType = response.Value.ContentType
        };
    }

    public string GeneratePresignedUrl(string storageKey, TimeSpan expiry)
    {
        var fileKey = FileKey.Parse(storageKey)
            ?? throw new ArgumentException($"Invalid storage key: {storageKey}", nameof(storageKey));

        var containerClient = GetContainerClientReference(fileKey.TenantId);
        var blobClient = containerClient.GetBlobClient(GetBlobName(fileKey));

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = containerClient.Name,
            BlobName = blobClient.Name,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.Add(expiry)
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        return blobClient.GenerateSasUri(sasBuilder).ToString();
    }

    private Task<BlobContainerClient> GetContainerClientAsync(Guid tenantId, CancellationToken ct)
    {
        var containerName = GetContainerName(tenantId);
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        return Task.FromResult(containerClient);
    }

    private BlobContainerClient GetContainerClientReference(Guid tenantId)
    {
        var containerName = GetContainerName(tenantId);
        return _blobServiceClient.GetBlobContainerClient(containerName);
    }

    private string GetContainerName(Guid tenantId) => $"{_options.ContainerPrefix}-{tenantId:D}";

    private static string GetBlobName(FileKey key) => $"{key.Year}/{key.FileGuid}{key.Extension}";
}
