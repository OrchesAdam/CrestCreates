using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.VirtualFileSystem.Models;

public interface IVirtualFile
{
    VirtualPath Path { get; }
    string FileName { get; }
    string? ContentType { get; }
    long Length { get; }
    DateTimeOffset LastModified { get; }
    VirtualResourceType ResourceType { get; }

    Task<Stream> OpenReadAsync(CancellationToken ct = default);
}
