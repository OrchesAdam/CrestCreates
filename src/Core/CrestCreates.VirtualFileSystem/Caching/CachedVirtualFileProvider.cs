using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.VirtualFileSystem.Models;
using CrestCreates.VirtualFileSystem.Providers;

namespace CrestCreates.VirtualFileSystem.Caching;

public class CachedVirtualFileProvider : IVirtualFileProvider
{
    private readonly IVirtualFileProvider _inner;
    private readonly TimeSpan _cacheDuration;
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public string ProviderName => _inner.ProviderName;
    public VirtualResourceType ResourceType => _inner.ResourceType;

    public CachedVirtualFileProvider(IVirtualFileProvider inner, TimeSpan? cacheDuration = null)
    {
        _inner = inner;
        _cacheDuration = cacheDuration ?? TimeSpan.FromMinutes(5);
    }

    public async Task<IVirtualFile?> GetFileAsync(VirtualPath path, CancellationToken ct = default)
    {
        var key = path.FullPath;

        if (TryGetFromCache(key, out var cached))
            return cached;

        var file = await _inner.GetFileAsync(path, ct);
        if (file != null)
            AddToCache(key, file);

        return file;
    }

    public async Task<IEnumerable<IVirtualFile>> GetFilesAsync(
        VirtualPath directory,
        string searchPattern = "*",
        bool recursive = false,
        CancellationToken ct = default)
    {
        var key = $"{directory.FullPath}|{searchPattern}|{recursive}";

        if (TryGetListFromCache(key, out var cached))
            return cached;

        var files = await _inner.GetFilesAsync(directory, searchPattern, recursive, ct);
        AddListToCache(key, files);

        return files;
    }

    public async Task<bool> ExistsAsync(VirtualPath path, CancellationToken ct = default)
    {
        var key = $"exists:{path.FullPath}";

        if (TryGetBoolFromCache(key, out var cached))
            return cached;

        var exists = await _inner.ExistsAsync(path, ct);
        AddBoolToCache(key, exists);

        return exists;
    }

    public async Task<IVirtualDirectory?> GetDirectoryAsync(VirtualPath path, CancellationToken ct = default)
    {
        var key = $"dir:{path.FullPath}";

        if (TryGetDirectoryFromCache(key, out var cached))
            return cached;

        var dir = await _inner.GetDirectoryAsync(path, ct);
        if (dir != null)
            AddDirectoryToCache(key, dir);

        return dir;
    }

    public async Task<bool> DirectoryExistsAsync(VirtualPath path, CancellationToken ct = default)
    {
        var key = $"direxists:{path.FullPath}";

        if (TryGetBoolFromCache(key, out var cached))
            return cached;

        var exists = await _inner.DirectoryExistsAsync(path, ct);
        AddBoolToCache(key, exists);

        return exists;
    }

    public IFileChangeToken Watch(VirtualPath path) => _inner.Watch(path);

    public void Invalidate(string? fullPath = null)
    {
        lock (_lock)
        {
            if (fullPath == null)
            {
                _cache.Clear();
            }
            else
            {
                _cache.Remove(fullPath);
                _cache.Remove($"exists:{fullPath}");
                _cache.Remove($"dir:{fullPath}");
                _cache.Remove($"direxists:{fullPath}");
            }
        }
    }

    private bool TryGetFromCache(string key, out IVirtualFile? value)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired)
            {
                value = (IVirtualFile?)entry.Value;
                return true;
            }

            value = null;
            return false;
        }
    }

    private bool TryGetListFromCache(string key, out IEnumerable<IVirtualFile> value)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired)
            {
                value = (IEnumerable<IVirtualFile>)entry.Value!;
                return true;
            }

            value = Array.Empty<IVirtualFile>();
            return false;
        }
    }

    private bool TryGetBoolFromCache(string key, out bool value)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired)
            {
                value = (bool)entry.Value!;
                return true;
            }

            value = false;
            return false;
        }
    }

    private bool TryGetDirectoryFromCache(string key, out IVirtualDirectory? value)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired)
            {
                value = (IVirtualDirectory?)entry.Value;
                return true;
            }

            value = null;
            return false;
        }
    }

    private void AddToCache(string key, IVirtualFile file) => AddRaw(key, file);
    private void AddListToCache(string key, IEnumerable<IVirtualFile> files) => AddRaw(key, files);
    private void AddBoolToCache(string key, bool value) => AddRaw(key, value);
    private void AddDirectoryToCache(string key, IVirtualDirectory dir) => AddRaw(key, dir);

    private void AddRaw(string key, object value)
    {
        lock (_lock)
        {
            _cache[key] = new CacheEntry(value, DateTimeOffset.UtcNow + _cacheDuration);
        }
    }

    private class CacheEntry
    {
        public object Value { get; }
        public DateTimeOffset ExpiresAt { get; }
        public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;

        public CacheEntry(object value, DateTimeOffset expiresAt)
        {
            Value = value;
            ExpiresAt = expiresAt;
        }
    }
}
