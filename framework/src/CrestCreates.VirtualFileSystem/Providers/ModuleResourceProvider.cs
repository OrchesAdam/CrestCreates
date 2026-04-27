using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.VirtualFileSystem.Models;

namespace CrestCreates.VirtualFileSystem.Providers;

public class ModuleResourceProvider : IVirtualFileProvider
{
    private readonly string _moduleName;
    private readonly List<IVirtualFileProvider> _providers = [];
    private readonly ReaderWriterLockSlim _lock = new();

    public string ProviderName => "Module";
    public VirtualResourceType ResourceType => 0; // Composite

    public ModuleResourceProvider(string moduleName)
    {
        _moduleName = moduleName.ToLowerInvariant();
    }

    public void AddProvider(IVirtualFileProvider provider)
    {
        _lock.EnterWriteLock();
        try
        {
            _providers.Add(provider);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public async Task<IVirtualFile?> GetFileAsync(VirtualPath path, CancellationToken ct = default)
    {
        if (path.ModuleName != _moduleName)
            return null;

        List<IVirtualFileProvider> snapshot;
        _lock.EnterReadLock();
        try
        {
            snapshot = _providers.ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }

        foreach (var provider in snapshot)
        {
            var file = await provider.GetFileAsync(path, ct);
            if (file != null)
                return file;
        }

        return null;
    }

    public async Task<IEnumerable<IVirtualFile>> GetFilesAsync(
        VirtualPath directory,
        string searchPattern = "*",
        bool recursive = false,
        CancellationToken ct = default)
    {
        if (directory.ModuleName != _moduleName)
            return Array.Empty<IVirtualFile>();

        List<IVirtualFileProvider> snapshot;
        _lock.EnterReadLock();
        try
        {
            snapshot = _providers.ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }

        var allFiles = new Dictionary<string, IVirtualFile>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in snapshot)
        {
            var files = await provider.GetFilesAsync(directory, searchPattern, recursive, ct);
            foreach (var file in files)
            {
                var key = file.Path.FullPath;
                if (!allFiles.ContainsKey(key))
                    allFiles[key] = file;
            }
        }

        return allFiles.Values.OrderBy(f => f.Path.FullPath);
    }

    public async Task<bool> ExistsAsync(VirtualPath path, CancellationToken ct = default)
    {
        if (path.ModuleName != _moduleName)
            return false;

        List<IVirtualFileProvider> snapshot;
        _lock.EnterReadLock();
        try
        {
            snapshot = _providers.ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }

        foreach (var provider in snapshot)
        {
            if (await provider.ExistsAsync(path, ct))
                return true;
        }

        return false;
    }

    public async Task<IVirtualDirectory?> GetDirectoryAsync(VirtualPath path, CancellationToken ct = default)
    {
        if (path.ModuleName != _moduleName)
            return null;

        List<IVirtualFileProvider> snapshot;
        _lock.EnterReadLock();
        try
        {
            snapshot = _providers.ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }

        foreach (var provider in snapshot)
        {
            var dir = await provider.GetDirectoryAsync(path, ct);
            if (dir != null)
                return dir;
        }

        return null;
    }

    public async Task<bool> DirectoryExistsAsync(VirtualPath path, CancellationToken ct = default)
    {
        if (path.ModuleName != _moduleName)
            return false;

        List<IVirtualFileProvider> snapshot;
        _lock.EnterReadLock();
        try
        {
            snapshot = _providers.ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }

        foreach (var provider in snapshot)
        {
            if (await provider.DirectoryExistsAsync(path, ct))
                return true;
        }

        return false;
    }

    public IFileChangeToken Watch(VirtualPath path)
    {
        if (path.ModuleName != _moduleName)
            return new FileChangeToken();

        List<IVirtualFileProvider> snapshot;
        _lock.EnterReadLock();
        try
        {
            snapshot = _providers.ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }

        return snapshot.FirstOrDefault()?.Watch(path) ?? new FileChangeToken();
    }
}