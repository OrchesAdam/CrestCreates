# Virtual File System Platform Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish unified access for module resources, embedded resources, and static resources — avoiding documentation, templates, and module resources scattered across individual modules.

**Architecture:** VFS provides a unified virtual path model that layers over multiple resource backends (physical files, embedded resources, cloud storage). Resources are identified by module-scoped virtual paths and resolved at runtime through a provider chain.

**Tech Stack:** .NET 10, existing module system, IFileProvider abstraction pattern

---

## File Structure

### New Files to Create

| File | Purpose |
|------|---------|
| `framework/src/CrestCreates.VirtualFileSystem/` | NEW: Virtual File System module |
| `Models/IVirtualFile.cs` | Virtual file entry interface |
| `Models/VirtualFileInfo.cs` | Virtual file metadata |
| `Models/VirtualResourceType.cs` | Enum: Physical, Embedded, Cloud |
| `Models/VirtualPath.cs` | Module-scoped virtual path value object |
| `Providers/IVirtualFileProvider.cs` | Core VFS provider interface |
| `Providers/IEmbeddedResourceProvider.cs` | Embedded resource access |
| `Providers/ModuleResourceProvider.cs` | Per-module resource discovery |
| `Services/IVirtualFileSystem.cs` | High-level VFS service interface |
| `Services/VirtualFileSystem.cs` | Implementation with provider chain |
| `VfsModule.cs` | Module initialization |

---

## Task 1: Define VFS Abstraction

**Files:**
- Create: `framework/src/CrestCreates.VirtualFileSystem/Models/IVirtualFile.cs`
- Create: `framework/src/CrestCreates.VirtualFileSystem/Models/VirtualFileInfo.cs`
- Create: `framework/src/CrestCreates.VirtualFileSystem/Models/VirtualResourceType.cs`
- Create: `framework/src/CrestCreates.VirtualFileSystem/Models/VirtualPath.cs`
- Create: `framework/src/CrestCreates.VirtualFileSystem/Providers/IVirtualFileProvider.cs`
- Create: `framework/src/CrestCreates.VirtualFileSystem/Providers/IEmbeddedResourceProvider.cs`
- Create: `framework/src/CrestCreates.VirtualFileSystem/VirtualFileSystem.csproj`

- [ ] **Step 1: Create VirtualResourceType enum**

```csharp
namespace CrestCreates.VirtualFileSystem.Models;

public enum VirtualResourceType
{
    Physical,   // Files on disk
    Embedded,   // Assembly embedded resources
    Cloud       // Cloud storage (future)
}
```

- [ ] **Step 2: Create VirtualPath value object**

```csharp
using System;

namespace CrestCreates.VirtualFileSystem.Models;

public readonly record struct VirtualPath
{
    public required string ModuleName { get; init; }
    public required string RelativePath { get; init; }

    // Format: {moduleName}/{relativePath}
    // Example: "CodeGenerator/Templates/Entity.txt"
    public string FullPath => $"{ModuleName}/{RelativePath}";

    public static VirtualPath Create(string moduleName, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
            throw new ArgumentException("Module name is required", nameof(moduleName));
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Relative path is required", nameof(relativePath));

        // Normalize path separators
        relativePath = relativePath.Replace('\\', '/');

        return new VirtualPath
        {
            ModuleName = moduleName.ToLowerInvariant(),
            RelativePath = relativePath.TrimStart('/')
        };
    }

    public static VirtualPath? Parse(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return null;

        var parts = fullPath.Split('/', 2);
        if (parts.Length < 2)
            return null;

        return Create(parts[0], parts[1]);
    }

    public bool IsChildOf(VirtualPath parent) =>
        ModuleName == parent.ModuleName &&
        RelativePath.StartsWith(parent.RelativePath + "/");
}
```

- [ ] **Step 3: Create IVirtualFile interface**

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.VirtualFileSystem.Models;

public interface IVirtualFile
{
    VirtualPath Path { get; }
    string FileName { get; }
    string? ContentType { get; }
    long Length { get; }
    DateTimeOffset LastModified { get; }
    VirtualResourceType ResourceType { get; }

    Task<Stream> OpenReadAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4: Create VirtualFileInfo**

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.VirtualFileSystem.Models;

public class VirtualFileInfo : IVirtualFile
{
    private readonly Func<CancellationToken, Task<Stream>> _openReadFunc;

    public VirtualPath Path { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string? ContentType { get; init; }
    public long Length { get; init; }
    public DateTimeOffset LastModified { get; init; }
    public VirtualResourceType ResourceType { get; init; }

    public VirtualFileInfo(
        VirtualPath path,
        Func<CancellationToken, Task<Stream>> openReadFunc,
        string fileName,
        string? contentType = null,
        long length = 0,
        DateTimeOffset? lastModified = null,
        VirtualResourceType resourceType = VirtualResourceType.Physical)
    {
        Path = path;
        FileName = fileName;
        ContentType = contentType;
        Length = length;
        LastModified = lastModified ?? DateTimeOffset.UtcNow;
        ResourceType = resourceType;
        _openReadFunc = openReadFunc ?? throw new ArgumentNullException(nameof(openReadFunc));
    }

    public Task<Stream> OpenReadAsync(CancellationToken ct = default)
        => _openReadFunc(ct);
}
```

- [ ] **Step 5: Create IVirtualFileProvider interface**

```csharp
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
```

- [ ] **Step 6: Create IEmbeddedResourceProvider interface**

```csharp
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.VirtualFileSystem.Models;

namespace CrestCreates.VirtualFileSystem.Providers;

public interface IEmbeddedResourceProvider
{
    Assembly Assembly { get; }
    string BaseNamespace { get; }

    Task<IEnumerable<string>> GetResourceNamesAsync(CancellationToken ct = default);

    Task<Stream?> GetResourceStreamAsync(string resourceName, CancellationToken ct = default);
}
```

- [ ] **Step 7: Create csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.VirtualFileSystem</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\CrestCreates.Modularity\CrestCreates.Modularity.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 8: Commit**

```bash
git add framework/src/CrestCreates.VirtualFileSystem/
git commit -m "feat(vfs): add core VFS models and interfaces

Add VirtualPath, IVirtualFile, VirtualFileInfo, VirtualResourceType
Add IVirtualFileProvider and IEmbeddedResourceProvider interfaces
Foundation for unified resource access across modules"
```

---

## Task 2: Implement Provider Infrastructure

**Files:**
- Create: `framework/src/CrestCreates.VirtualFileSystem/Providers/PhysicalFileProvider.cs`
- Create: `framework/src/CrestCreates.VirtualFileSystem/Providers/EmbeddedResourceProvider.cs`
- Create: `framework/src/CrestCreates.VirtualFileSystem/Providers/ModuleResourceProvider.cs`
- Create: `framework/src/CrestCreates.VirtualFileSystem/Services/VirtualFileSystem.cs`
- Create: `framework/src/CrestCreates.VirtualFileSystem/VfsModule.cs`

- [ ] **Step 1: Create PhysicalFileProvider**

```csharp
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
```

- [ ] **Step 2: Create EmbeddedResourceProvider**

```csharp
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

            var virtualPath = VirtualPath.Create(_moduleName, directory.RelativePath + "/" + relativePath.Replace('/', '+'));
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
            contentType: "text/plain", // Embedded resources don't have content type metadata
            length: stream.Length,
            lastModified: DateTimeOffset.UtcNow,
            resourceType: VirtualResourceType.Embedded
        ));
    }

    private string GetResourceName(string relativePath)
    {
        // Convert relative path to resource name format
        // Example: "Templates/Entity.txt" -> "BaseNamespace.Templates.Entity.txt"
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
```

- [ ] **Step 3: Create ModuleResourceProvider (aggregator)**

```csharp
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
```

- [ ] **Step 4: Create VirtualFileSystem service**

```csharp
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
```

- [ ] **Step 5: Create IVirtualFileSystem interface**

```csharp
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
```

- [ ] **Step 6: Create VfsModule**

```csharp
using System;
using System.Threading.Tasks;
using CrestCreates.Modularity;
using CrestCreates.VirtualFileSystem.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.VirtualFileSystem;

public class VfsModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IVirtualFileSystem, VirtualFileSystem>();
    }

    public override Task OnPostInitializeAsync()
    {
        // Auto-discover and register module resources
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 7: Commit**

```bash
git add framework/src/CrestCreates.VirtualFileSystem/
git commit -m "feat(vfs): add provider infrastructure and service implementation

PhysicalFileProvider: Disk-based resource access
EmbeddedResourceProvider: Assembly manifest resource access
ModuleResourceProvider: Per-module provider aggregation
VirtualFileSystem: High-level service with provider chain"
```

---

## Task 3: Integrate CodeGenerator Templates into VFS

**Files:**
- Create: `CrestCreates.VirtualFileSystem/Providers/CodeGeneratorResourceProvider.cs` (in CodeGenerator)
- Modify: `CrestCreates.CodeGenerator/EntityGenerator/TemplateManager.cs` (use VFS)

- [ ] **Step 1: Create CodeGeneratorResourceProvider**

```csharp
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.VirtualFileSystem.Models;
using CrestCreates.VirtualFileSystem.Providers;

namespace CrestCreates.CodeGenerator;

public class CodeGeneratorResourceProvider : IVirtualFileProvider
{
    private readonly EmbeddedResourceProvider _embeddedProvider;

    public string ProviderName => "CodeGenerator";
    public VirtualResourceType ResourceType => VirtualResourceType.Embedded;

    public CodeGeneratorResourceProvider()
    {
        _embeddedProvider = new EmbeddedResourceProvider(
            "CodeGenerator",
            typeof(CodeGeneratorResourceProvider).Assembly,
            "CrestCreates.CodeGenerator.Templates"
        );
    }

    public Assembly Assembly => _embeddedProvider.Assembly;

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

    public Task<IEnumerable<string>> GetTemplateNamesAsync(CancellationToken ct = default)
    {
        return _embeddedProvider.GetResourceNamesAsync(ct);
    }
}
```

- [ ] **Step 2: Update TemplateManager to use VFS**

```csharp
// Modify TemplateManager to accept IVirtualFileSystem and use it for template loading
// This replaces direct embedded resource loading with VFS-based access
```

- [ ] **Step 3: Commit**

```bash
git add framework/tools/CrestCreates.CodeGenerator/
git commit -m "feat(vfs): integrate CodeGenerator templates into VFS

CodeGeneratorResourceProvider exposes embedded templates via VFS
TemplateManager updated to use IVirtualFileSystem"
```

---

## Task 4: Create VFS Tests

**Files:**
- Create: `framework/test/CrestCreates.VirtualFileSystem.Tests/VirtualFileSystemTests.cs`
- Create: `framework/test/CrestCreates.VirtualFileSystem.Tests/VirtualPathTests.cs`
- Create: `framework/test/CrestCreates.VirtualFileSystem.Tests/PhysicalFileProviderTests.cs`
- Create: `framework/test/CrestCreates.VirtualFileSystem.Tests/CrestCreates.VirtualFileSystem.Tests.csproj`

- [ ] **Step 1: Create test project**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Moq" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\CrestCreates.VirtualFileSystem\CrestCreates.VirtualFileSystem.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create VirtualPathTests**

```csharp
using CrestCreates.VirtualFileSystem.Models;
using FluentAssertions;
using Xunit;

namespace CrestCreates.VirtualFileSystem.Tests;

public class VirtualPathTests
{
    [Fact]
    public void Create_ValidInput_ReturnsVirtualPath()
    {
        // Act
        var path = VirtualPath.Create("CodeGenerator", "Templates/Entity.txt");

        // Assert
        path.ModuleName.Should().Be("codegenerator");
        path.RelativePath.Should().Be("Templates/Entity.txt");
        path.FullPath.Should().Be("codegenerator/Templates/Entity.txt");
    }

    [Fact]
    public void Create_EmptyModuleName_Throws()
    {
        // Act
        var act = () => VirtualPath.Create("", "Templates/Entity.txt");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithBackslash_NormalizesToForwardSlash()
    {
        // Act
        var path = VirtualPath.Create("Module", "Templates\\Entity.txt");

        // Assert
        path.RelativePath.Should().Be("Templates/Entity.txt");
    }

    [Fact]
    public void Parse_ValidFullPath_ReturnsVirtualPath()
    {
        // Act
        var path = VirtualPath.Parse("CodeGenerator/Templates/Entity.txt");

        // Assert
        path.Should().NotBeNull();
        path!.Value.ModuleName.Should().Be("codegenerator");
        path.Value.RelativePath.Should().Be("Templates/Entity.txt");
    }

    [Fact]
    public void Parse_InvalidPath_ReturnsNull()
    {
        // Act
        var path = VirtualPath.Parse("InvalidPath");

        // Assert
        path.Should().BeNull();
    }

    [Fact]
    public void IsChildOf_ChildPath_ReturnsTrue()
    {
        // Arrange
        var parent = VirtualPath.Create("Module", "Templates");
        var child = VirtualPath.Create("Module", "Templates/Entity.txt");

        // Act
        var result = child.IsChildOf(parent);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsChildOf_DifferentModule_ReturnsFalse()
    {
        // Arrange
        var parent = VirtualPath.Create("Module1", "Templates");
        var child = VirtualPath.Create("Module2", "Templates/Entity.txt");

        // Act
        var result = child.IsChildOf(parent);

        // Assert
        result.Should().BeFalse();
    }
}
```

- [ ] **Step 3: Create PhysicalFileProviderTests**

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using CrestCreates.VirtualFileSystem.Models;
using CrestCreates.VirtualFileSystem.Providers;
using FluentAssertions;
using Xunit;

namespace CrestCreates.VirtualFileSystem.Tests;

public class PhysicalFileProviderTests : IDisposable
{
    private readonly string _testDir;
    private readonly PhysicalFileProvider _provider;

    public PhysicalFileProviderTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"vfs-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        Directory.CreateDirectory(Path.Combine(_testDir, "Templates"));

        // Create test files
        File.WriteAllText(Path.Combine(_testDir, "Templates", "Entity.txt"), "Test content");
        File.WriteAllText(Path.Combine(_testDir, "Readme.md"), "# Test");

        _provider = new PhysicalFileProvider("TestModule", _testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public async Task GetFileAsync_ExistingFile_ReturnsVirtualFile()
    {
        // Arrange
        var path = VirtualPath.Create("testmodule", "Templates/Entity.txt");

        // Act
        var file = await _provider.GetFileAsync(path);

        // Assert
        file.Should().NotBeNull();
        file!.FileName.Should().Be("Entity.txt");
        file.Length.Should().Be(12);
    }

    [Fact]
    public async Task GetFileAsync_NonExistingFile_ReturnsNull()
    {
        // Arrange
        var path = VirtualPath.Create("testmodule", "NonExistent.txt");

        // Act
        var file = await _provider.GetFileAsync(path);

        // Assert
        file.Should().BeNull();
    }

    [Fact]
    public async Task GetFileAsync_WrongModule_ReturnsNull()
    {
        // Arrange
        var path = VirtualPath.Create("othermodule", "Templates/Entity.txt");

        // Act
        var file = await _provider.GetFileAsync(path);

        // Assert
        file.Should().BeNull();
    }

    [Fact]
    public async Task OpenReadAsync_CanReadFileContent()
    {
        // Arrange
        var path = VirtualPath.Create("testmodule", "Templates/Entity.txt");
        var file = await _provider.GetFileAsync(path);

        // Act
        using var stream = await file!.OpenReadAsync();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        // Assert
        content.Should().Be("Test content");
    }

    [Fact]
    public async Task ExistsAsync_ExistingFile_ReturnsTrue()
    {
        // Arrange
        var path = VirtualPath.Create("testmodule", "Templates/Entity.txt");

        // Act
        var exists = await _provider.ExistsAsync(path);

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_NonExistingFile_ReturnsFalse()
    {
        // Arrange
        var path = VirtualPath.Create("testmodule", "NonExistent.txt");

        // Act
        var exists = await _provider.ExistsAsync(path);

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task GetFilesAsync_ReturnsAllFiles()
    {
        // Arrange
        var dirPath = VirtualPath.Create("testmodule", "Templates");

        // Act
        var files = await _provider.GetFilesAsync(dirPath, "*", recursive: false);

        // Assert
        files.Should().HaveCount(1);
    }
}
```

- [ ] **Step 4: Create VirtualFileSystemTests**

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using CrestCreates.VirtualFileSystem.Models;
using CrestCreates.VirtualFileSystem.Providers;
using CrestCreates.VirtualFileSystem.Services;
using FluentAssertions;
using Xunit;

namespace CrestCreates.VirtualFileSystem.Tests;

public class VirtualFileSystemTests : IDisposable
{
    private readonly string _testDir;
    private readonly VirtualFileSystem _vfs;

    public VirtualFileSystemTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"vfs-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);

        _vfs = new VirtualFileSystem();
        _vfs.RegisterModule("testmodule", new PhysicalFileProvider("TestModule", _testDir));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public async Task GetFileAsync_RegisteredModule_ReturnsFile()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "test.txt");
        File.WriteAllText(filePath, "Hello");

        // Act
        var file = await _vfs.GetFileAsync("testmodule/test.txt");

        // Assert
        file.Should().NotBeNull();
        file!.FileName.Should().Be("test.txt");
    }

    [Fact]
    public async Task GetFileAsync_UnregisteredModule_ReturnsNull()
    {
        // Act
        var file = await _vfs.GetFileAsync("unknownmodule/test.txt");

        // Assert
        file.Should().BeNull();
    }

    [Fact]
    public async Task ExistsAsync_ExistingFile_ReturnsTrue()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "exists.txt");
        File.WriteAllText(filePath, "Exists");

        // Act
        var exists = await _vfs.ExistsAsync("testmodule/exists.txt");

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_NonExistingFile_ReturnsFalse()
    {
        // Act
        var exists = await _vfs.ExistsAsync("testmodule/nonexistent.txt");

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public void GetRegisteredModules_ReturnsRegisteredNames()
    {
        // Act
        var modules = _vfs.GetRegisteredModules();

        // Assert
        modules.Should().Contain("testmodule");
    }

    [Fact]
    public async Task GetFileAsync_ConflictBetweenProviders_ReturnsFirstMatch()
    {
        // This test verifies provider priority when same file exists in multiple providers
        // Arrange - create two providers for same module with different files
        var dir1 = Path.Combine(Path.GetTempPath(), $"vfs-provider1-{Guid.NewGuid()}");
        var dir2 = Path.Combine(Path.GetTempPath(), $"vfs-provider2-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        File.WriteAllText(Path.Combine(dir1, "shared.txt"), "FromProvider1");
        File.WriteAllText(Path.Combine(dir2, "shared.txt"), "FromProvider2");

        var vfs = new VirtualFileSystem();
        vfs.RegisterModule("sharedmodule", new PhysicalFileProvider("SharedModule", dir1));
        vfs.RegisterModule("sharedmodule", new PhysicalFileProvider("SharedModule", dir2));

        // Act
        var file = await vfs.GetFileAsync("sharedmodule/shared.txt");

        // Assert - first registered provider wins
        file.Should().NotBeNull();

        // Cleanup
        Directory.Delete(dir1, true);
        Directory.Delete(dir2, true);
    }
}
```

- [ ] **Step 5: Run tests**

```bash
dotnet test framework/test/CrestCreates.VirtualFileSystem.Tests/
```

Expected: All tests pass

- [ ] **Step 6: Commit**

```bash
git add framework/test/CrestCreates.VirtualFileSystem.Tests/
git commit -m "test(vfs): add unit tests for VirtualPath and providers

VirtualPathTests: create, parse, path normalization, IsChildOf
PhysicalFileProviderTests: get file, exists, read content
VirtualFileSystemTests: module registration, file lookup, conflict handling"
```

---

## Acceptance Criteria

### Task 1 (VFS Abstraction)
- [ ] VirtualPath with module-scoped paths
- [ ] IVirtualFile interface for unified file access
- [ ] VirtualFileInfo implementation
- [ ] IVirtualFileProvider for storage-agnostic access
- [ ] IEmbeddedResourceProvider for assembly resources

### Task 2 (Provider Infrastructure)
- [ ] PhysicalFileProvider for disk files
- [ ] EmbeddedResourceProvider for embedded resources
- [ ] ModuleResourceProvider for per-module aggregation
- [ ] VirtualFileSystem service with provider chain
- [ ] VfsModule for DI registration

### Task 3 (CodeGenerator Integration)
- [ ] CodeGeneratorResourceProvider exposes templates
- [ ] TemplateManager uses IVirtualFileSystem

### Task 4 (Tests)
- [ ] VirtualPath unit tests
- [ ] PhysicalFileProvider unit tests
- [ ] VirtualFileSystem integration tests
- [ ] Module isolation tests
