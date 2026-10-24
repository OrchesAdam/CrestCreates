using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CrestCreates.DependencyBoundaries.Tests;

/// <summary>
/// Resolves the Compile items and preprocessor symbols the guard must actually analyse. The repository
/// keeps its Compile surface in the csproj files: a default glob over the project directory, explicit
/// <c>Compile Remove</c> entries and explicit <c>Compile Include</c> entries that may link sources from
/// outside the project directory (for example the shared CrudGeneration folder, which has no project of
/// its own). Directory.Build.* and the shared build targets do not add Compile items beyond generated
/// output, and no props/targets file defines additional constants; the calibration test compares this
/// derivation with the evaluated MSBuild values so that assumption cannot drift silently.
/// </summary>
internal static class TenantIdentityCompileInputs
{
    public static IReadOnlyList<string> ResolveCompileFiles(XDocument document, string projectDirectory)
    {
        var removes = CompileItems(document, "Remove").ToArray();
        var files = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var relative = NormalizeRelative(Path.GetRelativePath(projectDirectory, file));
            if (IsIntermediatePath(relative))
            {
                continue;
            }

            if (removes.Any(pattern => GlobMatches(relative, pattern)))
            {
                continue;
            }

            files.Add(Path.GetFullPath(file));
        }

        foreach (var include in CompileItems(document, "Include"))
        {
            foreach (var file in ExpandInclude(projectDirectory, include))
            {
                var relative = NormalizeRelative(Path.GetRelativePath(projectDirectory, file));
                if (IsIntermediatePath(relative)
                    || removes.Any(pattern => GlobMatches(relative, pattern)))
                {
                    continue;
                }

                files.Add(Path.GetFullPath(file));
            }
        }

        return files.ToArray();
    }

    /// <summary>
    /// SDK default constants for the configuration and target framework actually being analysed, plus the
    /// constants declared by the project itself.
    /// </summary>
    public static string DeriveDefineConstants(string configuration, string targetFramework, string? projectDefines)
    {
        var defines = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string value)
        {
            if (seen.Add(value))
            {
                defines.Add(value);
            }
        }

        Add("TRACE");
        if (string.Equals(configuration, "Release", StringComparison.OrdinalIgnoreCase))
        {
            Add("RELEASE");
        }
        else
        {
            Add("DEBUG");
        }

        var tfm = targetFramework ?? string.Empty;
        if (tfm.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
        {
            var version = tfm["netstandard".Length..];
            Add("NETSTANDARD");
            Add($"NETSTANDARD{NormalizeTfm(version)}");
            foreach (var current in StandardVersions(version))
            {
                Add($"NETSTANDARD{current}_OR_GREATER");
            }
        }
        else if (tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase)
                 && char.IsDigit(tfm.Length > 3 ? tfm[3] : 'x'))
        {
            var version = tfm[3..];
            Add("NET");
            Add($"NET{NormalizeTfm(version)}");
            Add("NETCOREAPP");
            foreach (var current in ModernVersions(version))
            {
                Add($"NET{current}_OR_GREATER");
            }

            foreach (var current in new[] { "1_0", "1_1", "2_0", "2_1", "2_2", "3_0", "3_1" })
            {
                Add($"NETCOREAPP{current}_OR_GREATER");
            }
        }

        foreach (var define in (projectDefines ?? string.Empty)
                     .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Add(define);
        }

        return string.Join(";", defines);
    }

    public static bool GlobMatches(string relativePath, string pattern)
    {
        var normalizedPattern = NormalizeRelative(pattern);
        var regex = "^" + Regex.Escape(normalizedPattern)
            .Replace("\\*\\*", "\u0000", StringComparison.Ordinal)
            .Replace("\\*", "[^/]*", StringComparison.Ordinal)
            .Replace("\\?", "[^/]", StringComparison.Ordinal)
            .Replace("\u0000", ".*", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(relativePath, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static string NormalizeRelative(string path) => path.Replace('\\', '/').TrimStart('/');

    /// <summary>
    /// Intermediate and output roots may also be the first path segment, so a leading-slash search would
    /// silently treat every generated file under obj/ as authored source.
    /// </summary>
    public static bool IsIntermediatePath(string relativePath) =>
        relativePath.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
        || relativePath.StartsWith("obj/", StringComparison.OrdinalIgnoreCase)
        || relativePath.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
        || relativePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> CompileItems(XDocument document, string attribute) =>
        document.Descendants("Compile")
            .Select(element => element.Attribute(attribute)?.Value)
            .OfType<string>()
            .Where(value => !value.Contains("$(", StringComparison.Ordinal))
            .Select(NormalizeRelative);

    private static IEnumerable<string> ExpandInclude(string projectDirectory, string include)
    {
        var absolute = Path.GetFullPath(Path.Combine(projectDirectory, include));
        if (!include.Contains('*') && !include.Contains('?'))
        {
            return File.Exists(absolute) ? [absolute] : [];
        }

        var wildcardIndex = include.IndexOfAny(['*', '?']);
        var separatorIndex = include.LastIndexOf('/', wildcardIndex);
        var searchRoot = Path.GetFullPath(Path.Combine(projectDirectory, separatorIndex < 0 ? "." : include[..separatorIndex]));
        var pattern = separatorIndex < 0 ? include : include[(separatorIndex + 1)..];
        if (!Directory.Exists(searchRoot))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(searchRoot, "*", SearchOption.AllDirectories)
            .Where(file => GlobMatches(NormalizeRelative(Path.GetRelativePath(searchRoot, file)), pattern))
            .ToArray();
    }

    private static string NormalizeTfm(string version) => version.Replace('.', '_');

    /// <summary>net5.0+ SDK chain: NET5_0_OR_GREATER … NET{major}_0_OR_GREATER.</summary>
    private static IEnumerable<string> ModernVersions(string version)
    {
        var parts = version.Split('.', 2);
        if (!int.TryParse(parts[0], out var major) || major < 5)
        {
            yield break;
        }

        for (var current = 5; current <= major; current++)
        {
            yield return $"{current}_0";
        }
    }

    /// <summary>netstandard chain: 1_0 … 1_6 then the declared 2.x version.</summary>
    private static IEnumerable<string> StandardVersions(string version)
    {
        var parts = version.Split('.', 2);
        if (!int.TryParse(parts[0], out var major))
        {
            yield break;
        }

        for (var minor = 0; minor <= 6; minor++)
        {
            yield return $"1_{minor}";
        }

        for (var current = 2; current <= major; current++)
        {
            var top = current == major && parts.Length > 1 && int.TryParse(parts[1], out var declared) ? declared : 0;
            for (var minor = 0; minor <= top; minor++)
            {
                yield return $"{current}_{minor}";
            }
        }
    }
}
