using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.VirtualFileSystem.Models;

namespace CrestCreates.VirtualFileSystem.Providers;

public class PhysicalFileProvider : IVirtualFileProvider
{
    private readonly string _basePath;
    private readonly string _normalizedBasePath;
    private readonly string _moduleName;

    public string ProviderName => "Physical";
    public VirtualResourceType ResourceType => VirtualResourceType.Physical;

    public PhysicalFileProvider(string moduleName, string basePath)
    {
        _moduleName = moduleName.ToLowerInvariant();
        _basePath = basePath;
        _normalizedBasePath = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public async Task<IVirtualFile?> GetFileAsync(VirtualPath path, CancellationToken ct = default)
    {
        if (path.ModuleName != _moduleName)
            return null;

        var physicalPath = ResolvePhysicalPath(path.RelativePath);
        if (physicalPath == null || !File.Exists(physicalPath))
            return null;

        var fileInfo = new FileInfo(physicalPath);
        return await Task.FromResult(new VirtualFileInfo(
            path: path,
            openReadFunc: _ => Task.FromResult<Stream>(File.OpenRead(physicalPath)),
            fileName: fileInfo.Name,
            contentType: GetContentType(fileInfo.Extension),
            length: fileInfo.Length,
            lastModified: fileInfo.LastWriteTimeUtc,
            resourceType: VirtualResourceType.Physical
        ));
    }

    public async Task<IEnumerable<IVirtualFile>> GetFilesAsync(
        VirtualPath directory,
        string searchPattern = "*",
        bool recursive = false,
        CancellationToken ct = default)
    {
        if (directory.ModuleName != _moduleName)
            return Array.Empty<IVirtualFile>();

        var baseDir = ResolvePhysicalPath(directory.RelativePath);
        if (baseDir == null || !Directory.Exists(baseDir))
            return Array.Empty<IVirtualFile>();

        var files = Directory.GetFiles(baseDir, searchPattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
        var result = new List<IVirtualFile>();

        foreach (var file in files)
        {
            var fullPath = Path.GetFullPath(file);
            if (!IsUnderBasePath(fullPath))
                continue;

            var fi = new FileInfo(file);
            var relativePath = Path.GetRelativePath(_basePath, file).Replace('\\', '/');
            var virtualPath = VirtualPath.Create(_moduleName, relativePath);

            result.Add(new VirtualFileInfo(
                path: virtualPath,
                openReadFunc: _ => Task.FromResult<Stream>(File.OpenRead(file)),
                fileName: fi.Name,
                contentType: GetContentType(fi.Extension),
                length: fi.Length,
                lastModified: fi.LastWriteTimeUtc,
                resourceType: VirtualResourceType.Physical
            ));
        }

        return await Task.FromResult(result);
    }

    public Task<bool> ExistsAsync(VirtualPath path, CancellationToken ct = default)
    {
        if (path.ModuleName != _moduleName)
            return Task.FromResult(false);

        var physicalPath = ResolvePhysicalPath(path.RelativePath);
        return Task.FromResult(physicalPath != null && File.Exists(physicalPath));
    }

    public Task<IVirtualDirectory?> GetDirectoryAsync(VirtualPath path, CancellationToken ct = default)
    {
        if (path.ModuleName != _moduleName)
            return Task.FromResult<IVirtualDirectory?>(null);

        var physicalPath = ResolvePhysicalPath(path.RelativePath);
        if (physicalPath == null || !Directory.Exists(physicalPath))
            return Task.FromResult<IVirtualDirectory?>(null);

        var dirInfo = new DirectoryInfo(physicalPath);
        var directory = new VirtualDirectoryInfo(
            path: path,
            name: dirInfo.Name,
            exists: true,
            lastModified: dirInfo.LastWriteTimeUtc,
            getFiles: innerCt => GetFilesAsync(path, "*", false, innerCt),
            getDirectories: innerCt => GetSubDirectoriesAsync(path, physicalPath, innerCt));

        return Task.FromResult<IVirtualDirectory?>(directory);
    }

    public Task<bool> DirectoryExistsAsync(VirtualPath path, CancellationToken ct = default)
    {
        if (path.ModuleName != _moduleName)
            return Task.FromResult(false);

        var physicalPath = ResolvePhysicalPath(path.RelativePath);
        return Task.FromResult(physicalPath != null && Directory.Exists(physicalPath));
    }

    public IFileChangeToken Watch(VirtualPath path)
    {
        var token = new FileChangeToken();

        if (path.ModuleName != _moduleName)
            return token;

        var physicalPath = ResolvePhysicalPath(path.RelativePath);
        if (physicalPath == null)
            return token;

        var watchPath = File.Exists(physicalPath)
            ? Path.GetDirectoryName(physicalPath) ?? _basePath
            : Directory.Exists(physicalPath)
                ? physicalPath
                : _basePath;

        if (!Directory.Exists(watchPath))
            return token;

        var watcher = new FileSystemWatcher(watchPath)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };

        var fileName = Path.GetFileName(physicalPath);
        var isFile = File.Exists(physicalPath);

        FileSystemEventHandler handler = (_, _) => token.NotifyChanged();
        RenamedEventHandler renamedHandler = (_, _) => token.NotifyChanged();

        if (isFile && !string.IsNullOrEmpty(fileName))
        {
            watcher.Filter = fileName;
        }

        watcher.Changed += handler;
        watcher.Created += handler;
        watcher.Deleted += handler;
        watcher.Renamed += renamedHandler;

        return token;
    }

    private async Task<IEnumerable<IVirtualDirectory>> GetSubDirectoriesAsync(VirtualPath parentPath, string physicalPath, CancellationToken ct)
    {
        var result = new List<IVirtualDirectory>();

        foreach (var dir in Directory.GetDirectories(physicalPath))
        {
            var fullPath = Path.GetFullPath(dir);
            if (!IsUnderBasePath(fullPath))
                continue;

            var relativePath = Path.GetRelativePath(_basePath, dir).Replace('\\', '/');
            var virtualPath = VirtualPath.Create(_moduleName, relativePath);
            var dirInfo = new DirectoryInfo(dir);

            result.Add(new VirtualDirectoryInfo(
                path: virtualPath,
                name: dirInfo.Name,
                exists: true,
                lastModified: dirInfo.LastWriteTimeUtc,
                getFiles: innerCt => GetFilesAsync(virtualPath, "*", false, innerCt),
                getDirectories: innerCt => GetSubDirectoriesAsync(virtualPath, dir, innerCt)));
        }

        return await Task.FromResult(result);
    }

    private string? ResolvePhysicalPath(string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_basePath, relativePath));
        return IsUnderBasePath(fullPath) ? fullPath : null;
    }

    private bool IsUnderBasePath(string fullPath)
    {
        var normalized = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalized.StartsWith(_normalizedBasePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, _normalizedBasePath, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".txt" => "text/plain",
        ".json" => "application/json",
        ".xml" => "application/xml",
        ".html" => "text/html",
        ".css" => "text/css",
        ".js" => "application/javascript",
        ".cs" => "text/plain",
        ".md" => "text/markdown",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };
}