using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.VirtualFileSystem.Models;
using CrestCreates.VirtualFileSystem.Providers;

namespace CrestCreates.VirtualFileSystem.Services;

public interface IVirtualFileSystem
{
    void RegisterModule(string moduleName, IVirtualFileProvider provider);

    Task<IVirtualFile?> GetFileAsync(string fullPath, CancellationToken ct = default);

    Task<IVirtualFile?> GetFileAsync(VirtualPath path, CancellationToken ct = default);

    Task<IEnumerable<IVirtualFile>> GetFilesAsync(
        string moduleName,
        string directory,
        string searchPattern = "*",
        bool recursive = false,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(string fullPath, CancellationToken ct = default);

    Task<bool> ExistsAsync(VirtualPath path, CancellationToken ct = default);

    IEnumerable<string> GetRegisteredModules();
}