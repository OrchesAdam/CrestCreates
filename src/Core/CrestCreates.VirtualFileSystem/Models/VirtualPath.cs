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
