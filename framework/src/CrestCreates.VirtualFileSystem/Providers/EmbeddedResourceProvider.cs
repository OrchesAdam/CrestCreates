using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.VirtualFileSystem.Models;

namespace CrestCreates.VirtualFileSystem.Providers;

public class EmbeddedResourceProvider : IVirtualFileProvider, IEmbeddedResourceProvider
{
    private readonly string _moduleName;
    private readonly Assembly _assembly;
    private readonly string _baseNamespace;

    public string ProviderName => "Embedded";
    public VirtualResourceType ResourceType => VirtualResourceType.Embedded;
    public Assembly Assembly => _assembly;
    public string BaseNamespace => _baseNamespace;

    public EmbeddedResourceProvider(string moduleName, Assembly assembly, string baseNamespace)
    {
        _moduleName = moduleName.ToLowerInvariant();
        _assembly = assembly;
        _baseNamespace = baseNamespace;
    }

    public Task<IVirtualFile?> GetFileAsync(VirtualPath path, CancellationToken ct = default)
    {
        if (path.ModuleName != _moduleName)
            return Task.FromResult<IVirtualFile?>(null);

        var resourceName = GetResourceName(path.RelativePath);
        return GetFileByResourceNameAsync(path, resourceName);
    }

    public async Task<IEnumerable<IVirtualFile>> GetFilesAsync(
        VirtualPath directory,
        string searchPattern = "*",
        bool recursive = false,
        CancellationToken ct = default)
    {
        if (directory.ModuleName != _moduleName)
            return Array.Empty<IVirtualFile>();

        var allNames = await GetResourceNamesAsync(ct);
        var results = new List<IVirtualFile>();

        var basePrefix = GetResourceName(directory.RelativePath);
        foreach (var name in allNames)
        {
            if (!name.StartsWith(basePrefix))
                continue;

            var relativePath = name.Substring(basePrefix.Length).TrimStart('/');

            // Apply search pattern
            var fileName = Path.GetFileName(relativePath.Replace('/', '\\'));
            if (!MatchesPattern(fileName, searchPattern))
                continue;

            // Check if it's in a subdirectory (for non-recursive)
            var remainingPath = relativePath.Substring(fileName.Length).TrimStart('/');
            if (!recursive && remainingPath.Contains('/'))
                continue;

            var virtualPath = VirtualPath.Create(_moduleName, directory.RelativePath + "/" + relativePath);
            var file = await GetFileByResourceNameAsync(virtualPath, name);
            if (file != null)
                results.Add(file);
        }

        return results;
    }

    public Task<bool> ExistsAsync(VirtualPath path, CancellationToken ct = default)
    {
        if (path.ModuleName != _moduleName)
            return Task.FromResult(false);

        var resourceName = GetResourceName(path.RelativePath);
        return Task.FromResult(_assembly.GetManifestResourceInfo(resourceName) != null);
    }

    public Task<IEnumerable<string>> GetResourceNamesAsync(CancellationToken ct = default)
    {
        var names = _assembly.GetManifestResourceNames();
        return Task.FromResult<IEnumerable<string>>(names);
    }

    public Task<Stream?> GetResourceStreamAsync(string resourceName, CancellationToken ct = default)
    {
        var stream = _assembly.GetManifestResourceStream(resourceName);
        return Task.FromResult(stream);
    }

    private Task<IVirtualFile?> GetFileByResourceNameAsync(VirtualPath path, string resourceName)
    {
        var stream = _assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return Task.FromResult<IVirtualFile?>(null);

        return Task.FromResult<IVirtualFile?>(new VirtualFileInfo(
            path: path,
            openReadFunc: _ => Task.FromResult<Stream>(_assembly.GetManifestResourceStream(resourceName)!),
            fileName: path.RelativePath.Split('/').Last(),
            contentType: "text/plain",
            length: stream.Length,
            lastModified: DateTimeOffset.UtcNow,
            resourceType: VirtualResourceType.Embedded
        ));
    }

    private string GetResourceName(string relativePath)
    {
        var normalized = relativePath.Replace('/', '.').Replace('\\', '.');
        return string.IsNullOrEmpty(_baseNamespace)
            ? normalized
            : $"{_baseNamespace}.{normalized}";
    }

    private static bool MatchesPattern(string fileName, string pattern)
    {
        if (pattern == "*")
            return true;

        if (pattern.StartsWith("*."))
        {
            var ext = pattern.Substring(1);
            return fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase);
        }

        return fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }
}