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
    private readonly List<IVirtualFileProvider> _providers = new();

    public string ProviderName => "Module";
    public VirtualResourceType ResourceType => 0; // Composite

    public ModuleResourceProvider(string moduleName)
    {
        _moduleName = moduleName.ToLowerInvariant();
    }

    public void AddProvider(IVirtualFileProvider provider)
    {
        _providers.Add(provider);
    }

    public async Task<IVirtualFile?> GetFileAsync(VirtualPath path, CancellationToken ct = default)
    {
        if (path.ModuleName != _moduleName)
            return null;

        // Try each provider in order
        foreach (var provider in _providers)
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

        var allFiles = new Dictionary<string, IVirtualFile>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in _providers)
        {
            var files = await provider.GetFilesAsync(directory, searchPattern, recursive, ct);
            foreach (var file in files)
            {
                // Deduplicate by virtual path
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

        foreach (var provider in _providers)
        {
            if (await provider.ExistsAsync(path, ct))
                return true;
        }

        return false;
    }
}