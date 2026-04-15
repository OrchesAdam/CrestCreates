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
    private readonly string _moduleName;

    public string ProviderName => "Physical";
    public VirtualResourceType ResourceType => VirtualResourceType.Physical;

    public PhysicalFileProvider(string moduleName, string basePath)
    {
        _moduleName = moduleName.ToLowerInvariant();
        _basePath = basePath;
    }

    public async Task<IVirtualFile?> GetFileAsync(VirtualPath path, CancellationToken ct = default)
    {
        if (path.ModuleName != _moduleName)
            return null;

        var physicalPath = Path.Combine(_basePath, path.RelativePath);
        if (!File.Exists(physicalPath))
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

        var baseDir = Path.Combine(_basePath, directory.RelativePath);
        if (!Directory.Exists(baseDir))
            return Array.Empty<IVirtualFile>();

        var files = Directory.GetFiles(baseDir, searchPattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
        var result = new List<IVirtualFile>();

        foreach (var file in files)
        {
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

        var physicalPath = Path.Combine(_basePath, path.RelativePath);
        return Task.FromResult(File.Exists(physicalPath));
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