using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.VirtualFileSystem.Models;

namespace CrestCreates.VirtualFileSystem.Providers;

public interface IVirtualFileProvider
{
    string ProviderName { get; }
    VirtualResourceType ResourceType { get; }

    Task<IVirtualFile?> GetFileAsync(VirtualPath path, CancellationToken ct = default);

    Task<IEnumerable<IVirtualFile>> GetFilesAsync(
        VirtualPath directory,
        string searchPattern = "*",
        bool recursive = false,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(VirtualPath path, CancellationToken ct = default);
}
