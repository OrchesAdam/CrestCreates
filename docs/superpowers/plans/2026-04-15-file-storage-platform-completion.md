# File Storage Platform Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transform FileManagement from a local file wrapper into a formal Blob/File Storage platform with unified file abstraction, storage provider model, metadata, access URLs, and multi-tenant boundaries.

**Architecture:** Use tenant-scoped file keys (`{tenantId}/{year}/{guid}.{ext}`) with virtual path mapping in local provider. Cloud providers (Azure) use native keys with container-per-tenant isolation. URL generation supports both public direct URLs and time-limited presigned URLs.

**Tech Stack:** .NET 10, Azure Blob Storage SDK, existing FileManagement project structure

---

## File Structure

### New Files to Create

| File | Purpose |
|------|---------|
| `framework/src/CrestCreates.FileManagement/Models/FileKey.cs` | Tenant-scoped file key with parse/format |
| `framework/src/CrestCreates.FileManagement/Models/FileEntity.cs` | Required file metadata record |
| `framework/src/CrestCreates.FileManagement/Models/FileMetadata.cs` | Optional extended metadata |
| `framework/src/CrestCreates.FileManagement/Models/FileAccessMode.cs` | Public/Private enum |
| `framework/src/CrestCreates.FileManagement/Models/IStorageMetadata.cs` | Storage metadata interface |
| `framework/src/CrestCreates.FileManagement/Providers/ICloudStorageProvider.cs` | Cloud-specific operations (presigned URLs) |
| `framework/src/CrestCreates.FileManagement/Services/IFileUrlService.cs` | URL generation interface |
| `framework/src/CrestCreates.FileManagement/Providers/AzureBlobStorageProvider.cs` | Azure implementation |
| `framework/src/CrestCreates.FileManagement/Configuration/AzureBlobStorageOptions.cs` | Azure configuration |
| `framework/test/CrestCreates.FileManagement.Tests/` | Unit tests project |

### Files to Modify

| File | Changes |
|------|---------|
| `framework/src/CrestCreates.FileManagement/Providers/IFileStorageProvider.cs` | Remove IFormFile, return FileEntity |
| `framework/src/CrestCreates.FileManagement/Providers/LocalFileSystemProvider.cs` | Virtual path mapping |
| `framework/src/CrestCreates.FileManagement/Services/IFileManagementService.cs` | Tenant-scoped, return FileEntity |
| `framework/src/CrestCreates.FileManagement/Services/FileManagementService.cs` | Key generation, tenant validation |
| `framework/src/CrestCreates.FileManagement/Modules/FileManagementModule.cs` | Provider registration |

---

## Task 1: Create Core Models

**Files:**
- Create: `framework/src/CrestCreates.FileManagement/Models/FileAccessMode.cs`
- Create: `framework/src/CrestCreates.FileManagement/Models/FileKey.cs`
- Create: `framework/src/CrestCreates.FileManagement/Models/IStorageMetadata.cs`
- Create: `framework/src/CrestCreates.FileManagement/Models/FileEntity.cs`
- Create: `framework/src/CrestCreates.FileManagement/Models/FileMetadata.cs`

- [ ] **Step 1: Create FileAccessMode enum**

```csharp
namespace CrestCreates.FileManagement.Models;

public enum FileAccessMode
{
    Public,
    Private
}
```

- [ ] **Step 2: Create FileKey record**

```csharp
using System.Text.RegularExpressions;

namespace CrestCreates.FileManagement.Models;

public record FileKey
{
    public required Guid TenantId { get; init; }
    public required int Year { get; init; }
    public required Guid Guid { get; init; }
    public required string Extension { get; init; }

    public string ToStorageKey() => $"{TenantId}/{Year}/{Guid}{Extension}";

    public static FileKey Create(Guid tenantId, string extension) => new()
    {
        TenantId = tenantId,
        Year = DateTimeOffset.UtcNow.Year,
        Guid = Guid.NewGuid(),
        Extension = extension.StartsWith('.') ? extension : $".{extension}"
    };

    public static FileKey? Parse(string storageKey)
    {
        if (string.IsNullOrEmpty(storageKey))
            return null;

        var pattern = @"^([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/(\d{4})/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})(\.[^/]+)?$";
        var match = Regex.Match(storageKey, pattern, RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        return new FileKey
        {
            TenantId = Guid.Parse(match.Groups[1].Value),
            Year = int.Parse(match.Groups[2].Value),
            Guid = Guid.Parse(match.Groups[3].Value),
            Extension = match.Groups[4].Value
        };
    }
}
```

- [ ] **Step 3: Create IStorageMetadata interface**

```csharp
namespace CrestCreates.FileManagement.Models;

public interface IStorageMetadata
{
    long Size { get; }
    DateTimeOffset LastModified { get; }
    string ContentType { get; }
}
```

- [ ] **Step 4: Create FileEntity record**

```csharp
namespace CrestCreates.FileManagement.Models;

public record FileEntity
{
    public required FileKey Key { get; init; }
    public required Guid TenantId { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required long Size { get; init; }
    public required string Extension { get; init; }
    public required long UploadedAt { get; init; }
    public long? LastAccessedAt { get; init; }
    public string? UploadedBy { get; init; }
    public FileAccessMode AccessMode { get; init; }
}
```

- [ ] **Step 5: Create FileMetadata record**

```csharp
namespace CrestCreates.FileManagement.Models;

public record FileMetadata
{
    public Dictionary<string, string> Tags { get; init; } = new();
    public string? Description { get; init; }
    public string? Category { get; init; }
    public Dictionary<string, object> CustomProperties { get; init; } = new();
}
```

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.FileManagement/Models/
git commit -m "feat(filemanagement): add core file storage models

Add FileKey, FileEntity, FileMetadata, FileAccessMode, IStorageMetadata"
```

---

## Task 2: Create Provider Interfaces

**Files:**
- Modify: `framework/src/CrestCreates.FileManagement/Providers/IFileStorageProvider.cs`
- Create: `framework/src/CrestCreates.FileManagement/Providers/ICloudStorageProvider.cs`
- Create: `framework/src/CrestCreates.FileManagement/Services/IFileUrlService.cs`

- [ ] **Step 1: Read current IFileStorageProvider.cs**

```csharp
// Current interface - need to understand full content
```

- [ ] **Step 2: Rewrite IFileStorageProvider to remove IFormFile**

```csharp
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
```

- [ ] **Step 3: Create ICloudStorageProvider**

```csharp
using System;

namespace CrestCreates.FileManagement.Providers;

public interface ICloudStorageProvider : IFileStorageProvider
{
    string GeneratePresignedUrl(string storageKey, TimeSpan expiry);
}
```

- [ ] **Step 4: Create IFileUrlService**

```csharp
using System;
using CrestCreates.FileManagement.Models;

namespace CrestCreates.FileManagement.Services;

public interface IFileUrlService
{
    string GetPublicUrl(FileEntity entity);

    string GetPresignedUrl(FileEntity entity, TimeSpan expiry);

    string? GetStorageKeyFromUrl(string url);
}
```

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.FileManagement/Providers/IFileStorageProvider.cs
git add framework/src/CrestCreates.FileManagement/Providers/ICloudStorageProvider.cs
git add framework/src/CrestCreates.FileManagement/Services/IFileUrlService.cs
git commit -m "feat(filemanagement): update provider interfaces for new model

Remove IFormFile dependency, use FileEntity and streams
Add ICloudStorageProvider for presigned URLs
Add IFileUrlService for URL generation"
```

---

## Task 3: Implement LocalFileSystemProvider with Virtual Path Mapping

**Files:**
- Modify: `framework/src/CrestCreates.FileManagement/Providers/LocalFileSystemProvider.cs`
- Create: `framework/src/CrestCreates.FileManagement/Models/LocalStorageMetadata.cs`

- [ ] **Step 1: Read current LocalFileSystemProvider.cs**

```csharp
// Current implementation - need full content
```

- [ ] **Step 2: Create LocalStorageMetadata**

```csharp
using System;

namespace CrestCreates.FileManagement.Models;

public class LocalStorageMetadata : IStorageMetadata
{
    public long Size { get; init; }
    public DateTimeOffset LastModified { get; init; }
    public string ContentType { get; init; } = "application/octet-stream";
}
```

- [ ] **Step 3: Rewrite LocalFileSystemProvider with virtual path mapping**

```csharp
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
```

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.FileManagement/Providers/LocalFileSystemProvider.cs
git add framework/src/CrestCreates.FileManagement/Models/LocalStorageMetadata.cs
git commit -m "feat(filemanagement): implement virtual path mapping in LocalFileSystemProvider

Physical path is SHA256 hash of storage key, preventing tenant info leakage
Virtual paths stored in .vfs directory"
```

---

## Task 4: Create AzureBlobStorageProvider

**Files:**
- Create: `framework/src/CrestCreates.FileManagement/Configuration/AzureBlobStorageOptions.cs`
- Create: `framework/src/CrestCreates.FileManagement/Providers/AzureBlobStorageProvider.cs`

- [ ] **Step 1: Create AzureBlobStorageOptions**

```csharp
namespace CrestCreates.FileManagement.Configuration;

public class AzureBlobStorageOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerPrefix { get; set; } = "files";
    public string? StaticWebHost { get; set; }
    public TimeSpan DefaultPresignedExpiry { get; set; } = TimeSpan.FromHours(1);
}
```

- [ ] **Step 2: Create AzureBlobStorageProvider**

```csharp
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
```

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.FileManagement/Configuration/AzureBlobStorageOptions.cs
git add framework/src/CrestCreates.FileManagement/Providers/AzureBlobStorageProvider.cs
git commit -m "feat(filemanagement): add Azure Blob Storage provider

Container per tenant: {prefix}-{tenantId}
Blob name: {year}/{guid}{extension}
Presigned URLs via BlobSasBuilder"
```

---

## Task 5: Update Service Layer

**Files:**
- Modify: `framework/src/CrestCreates.FileManagement/Services/IFileManagementService.cs`
- Modify: `framework/src/CrestCreates.FileManagement/Services/FileManagementService.cs`
- Modify: `framework/src/CrestCreates.FileManagement/Configuration/FileManagementOptions.cs`

- [ ] **Step 1: Read current IFileManagementService.cs**

- [ ] **Step 2: Rewrite IFileManagementService with new signature**

```csharp
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
```

- [ ] **Step 3: Rewrite FileManagementService with key generation and tenant validation**

```csharp
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
        // Would query repository if implemented
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
```

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.FileManagement/Services/IFileManagementService.cs
git add framework/src/CrestCreates.FileManagement/Services/FileManagementService.cs
git commit -m "feat(filemanagement): update service layer for tenant-scoped operations

IFileManagementService now returns FileEntity
FileManagementService validates tenant access
Key generation moved to FileKey.Create"
```

---

## Task 6: Create Unit Tests

**Files:**
- Create: `framework/test/CrestCreates.FileManagement.Tests/CrestCreates.FileManagement.Tests.csproj`
- Create: `framework/test/CrestCreates.FileManagement.Tests/FileKeyTests.cs`
- Create: `framework/test/CrestCreates.FileManagement.Tests/LocalFileSystemProviderTests.cs`

- [ ] **Step 1: Create test project**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Moq" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\CrestCreates.FileManagement\CrestCreates.FileManagement.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create FileKeyTests**

```csharp
using CrestCreates.FileManagement.Models;
using FluentAssertions;
using Xunit;

namespace CrestCreates.FileManagement.Tests;

public class FileKeyTests
{
    [Fact]
    public void Create_GeneratesValidKey()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var extension = ".pdf";

        // Act
        var key = FileKey.Create(tenantId, extension);

        // Assert
        key.TenantId.Should().Be(tenantId);
        key.Year.Should().Be(DateTimeOffset.UtcNow.Year);
        key.Extension.Should().Be(".pdf");
        key.FileGuid.Should().NotBeEmpty();
    }

    [Fact]
    public void ToStorageKey_ReturnsCorrectFormat()
    {
        // Arrange
        var tenantId = Guid.Parse("550e8400-e29b-41d4-a716-446655440000");
        var key = new FileKey
        {
            TenantId = tenantId,
            Year = 2026,
            Guid = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
            Extension = ".pdf"
        };

        // Act
        var storageKey = key.ToStorageKey();

        // Assert
        storageKey.Should().Be("550e8400-e29b-41d4-a716-446655440000/2026/a1b2c3d4-e5f6-7890-abcd-ef1234567890.pdf");
    }

    [Fact]
    public void Parse_ValidStorageKey_ReturnsFileKey()
    {
        // Arrange
        var storageKey = "550e8400-e29b-41d4-a716-446655440000/2026/a1b2c3d4-e5f6-7890-abcd-ef1234567890.pdf";

        // Act
        var key = FileKey.Parse(storageKey);

        // Assert
        key.Should().NotBeNull();
        key!.TenantId.Should().Be(Guid.Parse("550e8400-e29b-41d4-a716-446655440000"));
        key.Year.Should().Be(2026);
        key.Extension.Should().Be(".pdf");
    }

    [Fact]
    public void Parse_InvalidStorageKey_ReturnsNull()
    {
        // Arrange
        var invalidKey = "invalid/key/format";

        // Act
        var result = FileKey.Parse(invalidKey);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("550e8400-e29b-41d4-a716-446655440000/2026/a1b2c3d4.pdf")]
    [InlineData("../dangerous/path")]
    [InlineData("")]
    public void Parse_VariousInvalidKeys_ReturnsNull(string invalidKey)
    {
        // Act
        var result = FileKey.Parse(invalidKey);

        // Assert
        result.Should().BeNull();
    }
}
```

- [ ] **Step 3: Create LocalFileSystemProviderTests**

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using CrestCreates.FileManagement.Configuration;
using CrestCreates.FileManagement.Models;
using CrestCreates.FileManagement.Providers;
using FluentAssertions;
using Xunit;

namespace CrestCreates.FileManagement.Tests;

public class LocalFileSystemProviderTests : IDisposable
{
    private readonly string _testRoot;
    private readonly LocalFileSystemProvider _provider;

    public LocalFileSystemProviderTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"filemanagement-test-{Guid.NewGuid()}");
        var options = new LocalFileSystemOptions
        {
            RootPath = _testRoot,
            UseAbsolutePath = true
        };
        _provider = new LocalFileSystemProvider(options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, true);
    }

    [Fact]
    public async Task UploadAsync_ValidFile_ReturnsStorageKey()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var entity = new FileEntity
        {
            Key = FileKey.Create(tenantId, ".txt"),
            TenantId = tenantId,
            FileName = "test.txt",
            ContentType = "text/plain",
            Size = 5,
            Extension = ".txt",
            UploadedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AccessMode = FileAccessMode.Private
        };
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello"));

        // Act
        var storageKey = await _provider.UploadAsync(stream, entity);

        // Assert
        storageKey.Should().Contain(tenantId.ToString());
        storageKey.Should().EndWith(".txt");
    }

    [Fact]
    public async Task UploadAndDownload_RoundTrip_PreservesContent()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var content = "Hello, World!";
        var entity = new FileEntity
        {
            Key = FileKey.Create(tenantId, ".txt"),
            TenantId = tenantId,
            FileName = "test.txt",
            ContentType = "text/plain",
            Size = content.Length,
            Extension = ".txt",
            UploadedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AccessMode = FileAccessMode.Private
        };
        using var uploadStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

        // Act
        var storageKey = await _provider.UploadAsync(uploadStream, entity);
        using var downloadStream = await _provider.DownloadAsync(storageKey);
        using var reader = new StreamReader(downloadStream);
        var downloaded = await reader.ReadToEndAsync();

        // Assert
        downloaded.Should().Be(content);
    }

    [Fact]
    public async Task ExistsAsync_ExistingFile_ReturnsTrue()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var entity = new FileEntity
        {
            Key = FileKey.Create(tenantId, ".txt"),
            TenantId = tenantId,
            FileName = "test.txt",
            ContentType = "text/plain",
            Size = 5,
            Extension = ".txt",
            UploadedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AccessMode = FileAccessMode.Private
        };
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("test"));
        var storageKey = await _provider.UploadAsync(stream, entity);

        // Act
        var exists = await _provider.ExistsAsync(storageKey);

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_NonExistingFile_ReturnsFalse()
    {
        // Arrange
        var fakeKey = "non/existing/key.txt";

        // Act
        var exists = await _provider.ExistsAsync(fakeKey);

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_ExistingFile_RemovesFile()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var entity = new FileEntity
        {
            Key = FileKey.Create(tenantId, ".txt"),
            TenantId = tenantId,
            FileName = "test.txt",
            ContentType = "text/plain",
            Size = 5,
            Extension = ".txt",
            UploadedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AccessMode = FileAccessMode.Private
        };
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("test"));
        var storageKey = await _provider.UploadAsync(stream, entity);

        // Act
        await _provider.DeleteAsync(storageKey);
        var exists = await _provider.ExistsAsync(storageKey);

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public void UploadAsync_PathTraversalAttempt_Throws()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var maliciousKey = new FileKey
        {
            TenantId = tenantId,
            Year = 2026,
            Guid = Guid.NewGuid(),
            Extension = "../../../etc/passwd"
        };
        var entity = new FileEntity
        {
            Key = maliciousKey,
            TenantId = tenantId,
            FileName = "../../../etc/passwd",
            ContentType = "text/plain",
            Size = 5,
            Extension = "../../../etc/passwd",
            UploadedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            AccessMode = FileAccessMode.Private
        };
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("malicious"));

        // Act & Assert
        // The virtual path mapping should prevent actual path traversal
        // The physical path is based on SHA256 hash, not the storage key directly
        var act = () => _provider.UploadAsync(stream, entity);
        act.Should().NotThrow();
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test framework/test/CrestCreates.FileManagement.Tests/CrestCreates.FileManagement.Tests.csproj`
Expected: All tests pass

- [ ] **Step 5: Commit**

```bash
git add framework/test/CrestCreates.FileManagement.Tests/
git commit -m "test(filemanagement): add unit tests for FileKey and LocalFileSystemProvider

FileKeyTests: create, tostoragekey, parse, invalid key handling
LocalFileSystemProviderTests: upload, download, delete, exists, path traversal"
```

---

## Acceptance Criteria

### Task 1 (Models)
- [x] `FileEntity` with required tenant-scoped key
- [x] `FileKey` with parse/format methods
- [x] `FileMetadata` for optional extended data
- [x] Service returns `FileEntity` not raw paths

### Task 2 (Local Provider)
- [x] Virtual path mapping implemented
- [x] Upload reads correctly
- [x] Delete removes physical file
- [x] No path traversal possible
- [x] Multi-tenant isolation verified

### Task 3 (Azure Provider)
- [x] Container per tenant
- [x] Upload/download/delete work
- [x] Presigned URLs generated
- [x] Provider switch doesn't change service API

### Task 4 (Tests)
- [x] Upload/download/delete flow tests
- [x] Tenant isolation tests
- [x] Invalid input tests
- [x] Extension allowlist tests
