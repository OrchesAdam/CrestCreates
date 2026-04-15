# File Storage Platform Design

**Date:** 2026-04-15
**Status:** Draft
**Parent:** P1 File Storage Platform

## 1. Overview

Transform `FileManagement` from a local file wrapper into a formal Blob/File Storage platform with unified file abstraction, storage provider model, metadata, access URLs, and multi-tenant boundaries.

## 2. Architecture

```
CrestCreates.FileManagement/
├── Attributes/
│   └── FileStorageProviderAttribute.cs    # NEW: Provider discovery
├── Models/
│   ├── IFileEntity.cs                     # NEW: Unified file entity interface
│   ├── FileEntity.cs                      # NEW: Required metadata (tenant-scoped key, size, contentType, etc.)
│   ├── FileMetadata.cs                    # NEW: Optional extended metadata
│   └── FileAccessMode.cs                  # NEW: Public/Private access mode
├── Providers/
│   ├── IFileStorageProvider.cs           # MODIFIED: Remove IFormFile dependency, use streams
│   ├── LocalFileSystemProvider.cs         # MODIFIED: Virtual path mapping
│   └── AzureBlobStorageProvider.cs        # NEW: Azure Blob implementation
├── Services/
│   ├── IFileManagementService.cs          # MODIFIED: Return IFileEntity, tenant-scoped
│   ├── FileManagementService.cs           # MODIFIED: Key generation, tenant validation
│   └── IFileUrlService.cs                 # NEW: URL generation (public + presigned)
└── Modules/
    └── FileManagementModule.cs            # MODIFIED: Provider registration

CrestCreates.FileManagement.StorageProviders/
├── Abstractions/
│   ├── ICloudStorageProvider.cs           # NEW: Cloud-specific operations
│   └── IFileKeyGenerator.cs               # NEW: Tenant-scoped key generation
└── Azure/
    └── AzureBlobStorageProvider.cs        # NEW
```

## 3. Core Models

### 3.1 FileEntity (Required Metadata)

```csharp
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

### 3.2 FileKey (Tenant-Scoped)

```csharp
public record FileKey
{
    public required Guid TenantId { get; init; }
    public required int Year { get; init; }
    public required Guid Guid { get; init; }
    public required string Extension { get; init; }

    // Format: {tenantId}/{year}/{guid}.{ext}
    public string ToStorageKey() => $"{TenantId}/{Year}/{Guid}{Extension}";

    public static FileKey Create(Guid tenantId, string extension) => new()
    {
        TenantId = tenantId,
        Year = DateTimeOffset.UtcNow.Year,
        Guid = Guid.NewGuid(),
        Extension = extension
    };

    public static FileKey? Parse(string storageKey) { ... }
}
```

### 3.3 FileMetadata (Optional Extended)

```csharp
public record FileMetadata
{
    public Dictionary<string, string> Tags { get; init; } = new();
    public string? Description { get; init; }
    public string? Category { get; init; }
    public Dictionary<string, object> CustomProperties { get; init; } = new();
}
```

### 3.4 FileAccessMode

```csharp
public enum FileAccessMode
{
    Public,
    Private
}
```

## 4. Provider Interface

### 4.1 IFileStorageProvider

```csharp
public interface IFileStorageProvider
{
    string ProviderName { get; }

    Task<string> UploadAsync(Stream stream, FileEntity entity);

    Task<Stream> DownloadAsync(string storageKey);

    Task DeleteAsync(string storageKey);

    Task<bool> ExistsAsync(string storageKey);

    Task<IStorageMetadata> GetMetadataAsync(string storageKey);
}

public interface ICloudStorageProvider : IFileStorageProvider
{
    string GeneratePresignedUrl(string storageKey, TimeSpan expiry);
}

public interface IStorageMetadata
{
    long Size { get; }
    DateTimeOffset LastModified { get; }
    string ContentType { get; }
}
```

## 5. Virtual Path Mapping (Local Provider)

Local provider uses virtual path mapping to isolate tenants without exposing tenant info in physical paths:

**Mapping Strategy:**
- Logical storage key: `{tenantId}/{year}/{guid}.{ext}`
- Physical path: `{rootPath}/.vfs/{sha256(storageKey)}.dat`

**Example:**
```
Logical:  550e8400-e29b/2026/a1b2c3d4-e5f6-7890-1234-567812345678.pdf
Physical: /var/files/.vfs/7f83b1657ff1fc2aa5a94e7ad7c4a31e94e6a9c7d41f.dat
```

**Benefits:**
- True tenant isolation (no path traversal possible)
- Consistent key format across providers
- Physical paths don't reveal tenant identity
- Provider-agnostic abstraction

## 6. URL Generation

```csharp
public interface IFileUrlService
{
    string GetPublicUrl(FileEntity entity);

    string GetPresignedUrl(FileEntity entity, TimeSpan expiry);

    string? GetStorageKeyFromUrl(string url);
}
```

- Public files: Direct URL via configured base path
- Private files: Presigned URL with configurable expiry (cloud providers) or forbidden (local)

## 7. Service Layer

```csharp
public interface IFileManagementService
{
    Task<FileEntity> UploadAsync(
        Stream stream,
        string fileName,
        string contentType,
        FileAccessMode accessMode = FileAccessMode.Private,
        FileMetadata? metadata = null,
        Guid? tenantId = null);

    Task<Stream> DownloadAsync(FileKey key, Guid tenantId);

    Task DeleteAsync(FileKey key, Guid tenantId);

    Task<FileEntity?> GetAsync(FileKey key, Guid tenantId);

    Task<IEnumerable<FileEntity>> ListAsync(Guid tenantId, int? year = null);

    string GetUrl(FileEntity entity, TimeSpan? presignedExpiry = null);
}
```

## 8. Tenant Isolation

All operations enforce tenant boundaries:

1. `TenantId` required on all service methods
2. `FileKey` always contains `TenantId`
3. Provider validates key tenant matches request tenant
4. No cross-tenant access possible
5. Virtual path mapping prevents path traversal attacks

## 9. File Validation

- **Size limit**: Configurable max file size (default 100MB)
- **Extension allowlist**: Configurable list of allowed extensions
- **Filename sanitization**: Strip path traversal characters (`..`, `/`, `\`)
- **Content-Type validation**: Verify declared content type matches actual

## 10. Azure Blob Storage Provider

### 10.1 Configuration

```csharp
public class AzureBlobStorageOptions
{
    public string ConnectionString { get; set; }
    public string ContainerPrefix { get; set; } = "files";
    public string? StaticWebHost { get; set; }
}
```

### 10.2 Structure

- Container per tenant: `{prefix}-{tenantId:D}`
- Blob name: `{year}/{guid}{extension}`

### 10.3 Presigned URLs

Uses `BlobSasBuilder` with:
- Read permission
- Configurable expiry (default 1 hour)
- Applies to private files only

## 11. File Validation Options

```csharp
public class FileValidationOptions
{
    public long MaxFileSize { get; set; } = 104857600; // 100MB
    public string[] AllowedExtensions { get; set; } = Array.Empty<string>();
    public bool ValidateContentType { get; set; } = true;
}
```

## 12. Integration with ORM

File metadata stored via existing ORM providers when `IFileRepository` is implemented:

```csharp
public interface IFileRepository
{
    Task<FileEntity?> GetByKeyAsync(FileKey key);
    Task<IEnumerable<FileEntity>> ListAsync(Guid tenantId, int? year);
    Task CreateAsync(FileEntity entity);
    Task UpdateAsync(FileEntity entity);
    Task DeleteAsync(FileKey key);
}
```

This is optional - provider can work standalone.

## 13. File Workflow

### Upload Flow
1. Validate input (size, extension, content-type)
2. Create `FileKey` with tenant + year + guid + extension
3. Call `provider.UploadAsync(stream, entity)`
4. If repository available, save entity
5. Return `FileEntity` with URL

### Download Flow
1. Validate tenant access
2. If repository used, verify entity exists
3. Call `provider.DownloadAsync(storageKey)`
4. Return stream

### Delete Flow
1. Validate tenant access
2. Call `provider.DeleteAsync(storageKey)`
3. If repository used, delete entity

## 14. New Files Summary

| File | Purpose |
|------|---------|
| `Models/FileKey.cs` | Tenant-scoped file key |
| `Models/FileEntity.cs` | Required file metadata |
| `Models/FileMetadata.cs` | Optional extended metadata |
| `Models/FileAccessMode.cs` | Public/Private enum |
| `Providers/ICloudStorageProvider.cs` | Cloud-specific operations |
| `Providers/AzureBlobStorageProvider.cs` | Azure implementation |
| `Services/IFileUrlService.cs` | URL generation interface |

## 15. Modified Files Summary

| File | Changes |
|------|---------|
| `IFileStorageProvider.cs` | Remove IFormFile, return FileEntity |
| `LocalFileSystemProvider.cs` | Virtual path mapping |
| `IFileManagementService.cs` | Tenant-scoped, return FileEntity |
| `FileManagementService.cs` | Key generation, tenant validation |
| `FileManagementModule.cs` | Provider registration |

## 16. Acceptance Criteria

### Task 1 (Models)
- [ ] `FileEntity` with required tenant-scoped key
- [ ] `FileKey` with parse/format methods
- [ ] `FileMetadata` for optional extended data
- [ ] Service returns `FileEntity` not raw paths

### Task 2 (Local Provider)
- [ ] Virtual path mapping implemented
- [ ] Upload reads correctly
- [ ] Delete removes physical file
- [ ] No path traversal possible
- [ ] Multi-tenant isolation verified

### Task 3 (Azure Provider)
- [ ] Container per tenant
- [ ] Upload/download/delete work
- [ ] Presigned URLs generated
- [ ] Provider switch doesn't change service API

### Task 4 (Tests)
- [ ] Upload/download/delete flow tests
- [ ] Tenant isolation tests
- [ ] Invalid input tests
- [ ] Extension allowlist tests
