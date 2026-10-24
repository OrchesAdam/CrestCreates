namespace CrestCreates.DependencyBoundaries.Tests;

/// <summary>
/// Resolves paths written in MSBuild metadata. Project files use both separators interchangeably and may
/// contain MSBuild property expressions; feeding a backslash path straight into the Linux filesystem keeps the
/// backslash as an ordinary character and the resulting path can never match, which would silently disable a
/// reference guard.
/// </summary>
internal static class ArchitecturePath
{
    public static string? TryResolve(string baseDirectory, string? include)
    {
        if (string.IsNullOrWhiteSpace(include) || include.Contains("$(", StringComparison.Ordinal))
        {
            return null;
        }

        var normalized = include.Replace('\\', '/').Trim();
        return Path.GetFullPath(Path.Combine(baseDirectory, normalized));
    }

    public static string Normalize(string path) => path.Replace('\\', '/');

    public static string Relative(string root, string path) =>
        Normalize(Path.GetRelativePath(root, path)).TrimStart('.').TrimStart('/');

    public static bool IsUnder(string relativePath, string relativePrefix) =>
        string.Equals(relativePath, relativePrefix, StringComparison.Ordinal)
        || relativePath.StartsWith(relativePrefix + "/", StringComparison.Ordinal);
}
