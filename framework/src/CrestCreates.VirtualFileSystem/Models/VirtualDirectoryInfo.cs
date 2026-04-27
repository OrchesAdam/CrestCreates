using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.VirtualFileSystem.Models;

namespace CrestCreates.VirtualFileSystem.Providers;

public class VirtualDirectoryInfo : IVirtualDirectory
{
    private readonly Func<CancellationToken, Task<IEnumerable<IVirtualFile>>> _getFiles;
    private readonly Func<CancellationToken, Task<IEnumerable<IVirtualDirectory>>> _getDirectories;

    public VirtualPath Path { get; }
    public string Name { get; }
    public DateTimeOffset LastModified { get; }
    public bool Exists { get; }

    public VirtualDirectoryInfo(
        VirtualPath path,
        string name,
        bool exists,
        DateTimeOffset lastModified,
        Func<CancellationToken, Task<IEnumerable<IVirtualFile>>> getFiles,
        Func<CancellationToken, Task<IEnumerable<IVirtualDirectory>>> getDirectories)
    {
        Path = path;
        Name = name;
        Exists = exists;
        LastModified = lastModified;
        _getFiles = getFiles;
        _getDirectories = getDirectories;
    }

    public Task<IEnumerable<IVirtualFile>> GetFilesAsync(CancellationToken ct = default)
        => _getFiles(ct);

    public Task<IEnumerable<IVirtualDirectory>> GetDirectoriesAsync(CancellationToken ct = default)
        => _getDirectories(ct);
}
