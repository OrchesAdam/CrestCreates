using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.VirtualFileSystem.Models;
using CrestCreates.VirtualFileSystem.Providers;

namespace CrestCreates.VirtualFileSystem.Services;

public class VirtualFileSystem : IVirtualFileSystem
{
    private readonly Dictionary<string, ModuleResourceProvider> _moduleProviders = new(StringComparer.OrdinalIgnoreCase);
    private readonly IVirtualFileProvider _fallbackProvider;

    public VirtualFileSystem()
    {
        _fallbackProvider = new NullProvider();
    }

    public void RegisterModule(string moduleName, IVirtualFileProvider provider)
    {
        if (!_moduleProviders.TryGetValue(moduleName, out var moduleProvider))
        {
            moduleProvider = new ModuleResourceProvider(moduleName);
            _moduleProviders[moduleName] = moduleProvider;
        }

        moduleProvider.AddProvider(provider);
    }

    public async Task<IVirtualFile?> GetFileAsync(string fullPath, CancellationToken ct = default)
    {
        var virtualPath = VirtualPath.Parse(fullPath);
        if (virtualPath == null)
            return null;

        return await GetFileAsync(virtualPath.Value, ct);
    }

    public async Task<IVirtualFile?> GetFileAsync(VirtualPath path, CancellationToken ct = default)
    {
        if (_moduleProviders.TryGetValue(path.ModuleName, out var provider))
        {
            return await provider.GetFileAsync(path, ct);
        }

        return await _fallbackProvider.GetFileAsync(path, ct);
    }

    public async Task<IEnumerable<IVirtualFile>> GetFilesAsync(
        string moduleName,
        string directory,
        string searchPattern = "*",
        bool recursive = false,
        CancellationToken ct = default)
    {
        var basePath = VirtualPath.Create(moduleName, directory);
        if (_moduleProviders.TryGetValue(moduleName, out var provider))
        {
            return await provider.GetFilesAsync(basePath, searchPattern, recursive, ct);
        }

        return Array.Empty<IVirtualFile>();
    }

    public async Task<bool> ExistsAsync(string fullPath, CancellationToken ct = default)
    {
        var virtualPath = VirtualPath.Parse(fullPath);
        if (virtualPath == null)
            return false;

        return await ExistsAsync(virtualPath.Value, ct);
    }

    public async Task<bool> ExistsAsync(VirtualPath path, CancellationToken ct = default)
    {
        if (_moduleProviders.TryGetValue(path.ModuleName, out var provider))
        {
            return await provider.ExistsAsync(path, ct);
        }

        return false;
    }

    public IEnumerable<string> GetRegisteredModules()
    {
        return _moduleProviders.Keys;
    }

    private class NullProvider : IVirtualFileProvider
    {
        public string ProviderName => "Null";
        public VirtualResourceType ResourceType => 0;

        public Task<IVirtualFile?> GetFileAsync(VirtualPath path, CancellationToken ct = default)
            => Task.FromResult<IVirtualFile?>(null);

        public Task<IEnumerable<IVirtualFile>> GetFilesAsync(VirtualPath directory, string searchPattern, bool recursive, CancellationToken ct = default)
            => Task.FromResult<IEnumerable<IVirtualFile>>(Array.Empty<IVirtualFile>());

        public Task<bool> ExistsAsync(VirtualPath path, CancellationToken ct = default)
            => Task.FromResult(false);
    }
}