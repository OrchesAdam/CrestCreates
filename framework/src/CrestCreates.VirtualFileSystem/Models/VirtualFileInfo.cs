using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.VirtualFileSystem.Models;

public class VirtualFileInfo : IVirtualFile
{
    private readonly Func<CancellationToken, Task<Stream>> _openReadFunc;

    public VirtualPath Path { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string? ContentType { get; init; }
    public long Length { get; init; }
    public DateTimeOffset LastModified { get; init; }
    public VirtualResourceType ResourceType { get; init; }

    public VirtualFileInfo(
        VirtualPath path,
        Func<CancellationToken, Task<Stream>> openReadFunc,
        string fileName,
        string? contentType = null,
        long length = 0,
        DateTimeOffset? lastModified = null,
        VirtualResourceType resourceType = VirtualResourceType.Physical)
    {
        Path = path;
        FileName = fileName;
        ContentType = contentType;
        Length = length;
        LastModified = lastModified ?? DateTimeOffset.UtcNow;
        ResourceType = resourceType;
        _openReadFunc = openReadFunc ?? throw new ArgumentNullException(nameof(openReadFunc));
    }

    public Task<Stream> OpenReadAsync(CancellationToken ct = default)
        => _openReadFunc(ct);
}
