using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.VirtualFileSystem.Models;
using IVirtualFileProvider = CrestCreates.VirtualFileSystem.Providers.IVirtualFileProvider;
using EmbeddedResourceProvider = CrestCreates.VirtualFileSystem.Providers.EmbeddedResourceProvider;

namespace CrestCreates.CodeGenerator;

/// <summary>
/// Virtual file provider for CodeGenerator embedded templates.
/// Exposes templates from CrestCreates.CodeGenerator.Templates namespace via VFS.
/// </summary>
public class CodeGeneratorResourceProvider : IVirtualFileProvider
{
    private readonly EmbeddedResourceProvider _embeddedProvider;

    public string ProviderName => "CodeGenerator";
    public VirtualResourceType ResourceType => VirtualResourceType.Embedded;
    public Assembly Assembly => _embeddedProvider.Assembly;
    public string BaseNamespace => _embeddedProvider.BaseNamespace;

    public CodeGeneratorResourceProvider()
    {
        _embeddedProvider = new EmbeddedResourceProvider(
            "CodeGenerator",
            typeof(CodeGeneratorResourceProvider).Assembly,
            "CrestCreates.CodeGenerator.Templates"
        );
    }

    public Task<IVirtualFile?> GetFileAsync(VirtualPath path, CancellationToken ct = default)
        => _embeddedProvider.GetFileAsync(path, ct);

    public Task<IEnumerable<IVirtualFile>> GetFilesAsync(
        VirtualPath directory,
        string searchPattern = "*",
        bool recursive = false,
        CancellationToken ct = default)
        => _embeddedProvider.GetFilesAsync(directory, searchPattern, recursive, ct);

    public Task<bool> ExistsAsync(VirtualPath path, CancellationToken ct = default)
        => _embeddedProvider.ExistsAsync(path, ct);

    /// <summary>
    /// Gets all template resource names available in this provider.
    /// </summary>
    public Task<IEnumerable<string>> GetTemplateNamesAsync(CancellationToken ct = default)
        => _embeddedProvider.GetResourceNamesAsync(ct);
}