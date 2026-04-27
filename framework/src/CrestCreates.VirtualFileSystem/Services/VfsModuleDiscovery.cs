using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CrestCreates.VirtualFileSystem.Models;
using CrestCreates.VirtualFileSystem.Providers;

namespace CrestCreates.VirtualFileSystem.Services;

public class VfsModuleDiscovery
{
    private readonly IVirtualFileSystem _vfs;

    public VfsModuleDiscovery(IVirtualFileSystem vfs)
    {
        _vfs = vfs;
    }

    public void DiscoverAndRegister(IEnumerable<Assembly> assemblies)
    {
        foreach (var assembly in assemblies)
        {
            DiscoverEmbeddedResources(assembly);
            DiscoverPhysicalResources(assembly);
        }
    }

    private void DiscoverEmbeddedResources(Assembly assembly)
    {
        var resourceName = assembly.GetName().Name;
        if (string.IsNullOrEmpty(resourceName))
            return;

        var resourceNames = assembly.GetManifestResourceNames();
        if (resourceNames.Length == 0)
            return;

        var moduleName = ExtractModuleName(resourceName);
        var baseNamespace = resourceName;

        var provider = new EmbeddedResourceProvider(moduleName, assembly, baseNamespace);
        _vfs.RegisterModule(moduleName, provider);
    }

    private void DiscoverPhysicalResources(Assembly assembly)
    {
        var moduleName = assembly.GetName().Name;
        if (string.IsNullOrEmpty(moduleName))
            return;

        var moduleDir = FindModuleContentDirectory(assembly);
        if (moduleDir == null)
            return;

        var provider = new PhysicalFileProvider(ExtractModuleName(moduleName), moduleDir);
        _vfs.RegisterModule(ExtractModuleName(moduleName), provider);
    }

    private static string? FindModuleContentDirectory(Assembly assembly)
    {
        var assemblyLocation = assembly.Location;
        if (string.IsNullOrEmpty(assemblyLocation))
            return null;

        var baseDir = Path.GetDirectoryName(assemblyLocation);
        if (baseDir == null)
            return null;

        var contentDir = Path.Combine(baseDir, "Content");
        if (Directory.Exists(contentDir))
            return contentDir;

        var wwwrootDir = Path.Combine(baseDir, "wwwroot");
        if (Directory.Exists(wwwrootDir))
            return wwwrootDir;

        return null;
    }

    private static string ExtractModuleName(string assemblyName)
    {
        var lastDot = assemblyName.LastIndexOf('.');
        return lastDot >= 0 ? assemblyName.Substring(lastDot + 1) : assemblyName;
    }
}
