using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace CrestCreates.DependencyBoundaries.Tests;

internal sealed record EvaluatedProjectInputs(
    string Source,
    string Configuration,
    string TargetFramework,
    IReadOnlyList<string> CompileFiles,
    string DefineConstants)
{
    /// <summary>Set when the evaluation could not be performed; the guard turns this into a TI900.</summary>
    public string? UnavailableReason { get; init; }

    public bool IsAvailable => UnavailableReason is null;
}

/// <summary>
/// Reads the Compile items and preprocessor symbols from MSBuild itself, so conditions, imports and property
/// functions are evaluated by MSBuild instead of a partial re-implementation here.
///
/// The evaluated answer is cached only when the dependency surface can be proven complete: every Compile item in
/// the project and in its resolvable imports must be a literal path, and every import must resolve without
/// property expressions. A <c>$(...)</c> Compile item or an unresolvable import may point anywhere, so those
/// projects skip the persistent cache and call MSBuild again - skipping the cache, never the guard. The cached
/// key covers the project directory, every imported build file, the project's own file list and the search roots
/// of literal out-of-directory globs, so a warm read can never hide a new or removed linked file.
/// </summary>
internal static class TenantIdentityEvaluatedInputs
{
    private static readonly string[] AutoImportedPropertyFiles =
    [
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Build.Aot.props",
        "Directory.Packages.props",
    ];

    public static EvaluatedProjectInputs Read(string repoRoot, string projectFile, string configuration)
    {
        var projectDirectory = Path.GetDirectoryName(projectFile)!;
        var dependencies = DependencyProbe.Collect(repoRoot, projectFile, projectDirectory);

        if (!dependencies.IsFullyUnderstood)
        {
            // Cannot prove the input set: evaluate every time instead of trusting a possibly stale snapshot.
            var (fresh, freshError) = Evaluate(projectFile, configuration);
            return fresh ?? new EvaluatedProjectInputs("msbuild-failed", configuration, string.Empty, [], string.Empty)
            {
                UnavailableReason = freshError ?? "dotnet msbuild produced no evaluable output",
            };
        }

        var cacheKey = CacheKey(repoRoot, projectFile, projectDirectory, configuration, dependencies);
        var cachePath = Path.Combine(projectDirectory, "obj", $"crestcreates-tenant-inputs.{configuration}.json");

        if (TryReadCache(cachePath, cacheKey) is { } cached && cached.CompileFiles.All(File.Exists))
        {
            return cached;
        }

        var (evaluated, error) = Evaluate(projectFile, configuration);
        if (evaluated is null)
        {
            return new EvaluatedProjectInputs("msbuild-failed", configuration, string.Empty, [], string.Empty)
            {
                UnavailableReason = error ?? "dotnet msbuild produced no evaluable output",
            };
        }

        WriteCache(cachePath, cacheKey, evaluated);
        return evaluated;
    }

    private static (EvaluatedProjectInputs? Evaluated, string? Error) Evaluate(string projectFile, string configuration)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(projectFile)!,
        };
        start.ArgumentList.Add("msbuild");
        start.ArgumentList.Add(projectFile);
        start.ArgumentList.Add("-nologo");
        start.ArgumentList.Add($"-p:Configuration={configuration}");
        start.ArgumentList.Add("-t:AddImplicitDefineConstants");
        start.ArgumentList.Add("-getItem:Compile");
        start.ArgumentList.Add("-getProperty:DefineConstants");
        start.ArgumentList.Add("-getProperty:TargetFramework");

        using var process = Process.Start(start);
        if (process is null)
        {
            return (null, "could not start dotnet msbuild");
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(180_000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            return (null, "dotnet msbuild timed out");
        }

        if (process.ExitCode != 0)
        {
            return (null, $"dotnet msbuild exited {process.ExitCode}: {Trim(stderr)}");
        }

        try
        {
            using var document = JsonDocument.Parse(stdout);
            var root = document.RootElement;
            var files = root.GetProperty("Items").GetProperty("Compile")
                .EnumerateArray()
                .Select(item => item.GetProperty("FullPath").GetString())
                .OfType<string>()
                .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            return (new EvaluatedProjectInputs(
                "msbuild",
                configuration,
                root.GetProperty("Properties").GetProperty("TargetFramework").GetString() ?? string.Empty,
                files,
                root.GetProperty("Properties").GetProperty("DefineConstants").GetString() ?? string.Empty), null);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return (null, $"could not parse dotnet msbuild output: {exception.Message}");
        }
    }

    private static string CacheKey(
        string repoRoot,
        string projectFile,
        string projectDirectory,
        string configuration,
        DependencyProbe.Result dependencies)
    {
        var builder = new StringBuilder();
        builder.Append(Path.GetFullPath(projectDirectory)).Append('\n');
        builder.Append(configuration).Append('\n');

        foreach (var file in dependencies.BuildFiles)
        {
            builder.Append(file).Append(':').Append(File.ReadAllText(file)).Append('\n');
        }

        foreach (var file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(projectDirectory, file);
            if (relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            builder.Append(relative).Append(':').Append(new FileInfo(file).Length).Append('\n');
        }

        foreach (var root in dependencies.ExternalRoots.OrderBy(path => path, StringComparer.Ordinal))
        {
            AppendTreeSignature(builder, root);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void AppendTreeSignature(StringBuilder builder, string root)
    {
        var rootInfo = new DirectoryInfo(root);
        builder.Append("root:").Append(rootInfo.FullName).Append(':').Append(rootInfo.LastWriteTimeUtc.Ticks).Append('\n');

        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            builder.Append("dir:").Append(Path.GetRelativePath(root, directory)).Append(':')
                .Append(Directory.GetLastWriteTimeUtc(directory).Ticks).Append('\n');
        }

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            builder.Append("file:").Append(Path.GetRelativePath(root, file)).Append(':')
                .Append(new FileInfo(file).Length).Append(':')
                .Append(File.GetLastWriteTimeUtc(file).Ticks).Append('\n');
        }
    }

    private static EvaluatedProjectInputs? TryReadCache(string cachePath, string cacheKey)
    {
        if (!File.Exists(cachePath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(cachePath));
            var root = document.RootElement;
            if (root.GetProperty("key").GetString() != cacheKey)
            {
                return null;
            }

            return new EvaluatedProjectInputs(
                "msbuild-cache",
                root.GetProperty("configuration").GetString() ?? "Debug",
                root.GetProperty("targetFramework").GetString() ?? string.Empty,
                root.GetProperty("compileFiles").EnumerateArray().Select(item => item.GetString()!).ToArray(),
                root.GetProperty("defineConstants").GetString() ?? string.Empty);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static void WriteCache(string cachePath, string cacheKey, EvaluatedProjectInputs evaluated)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            var payload = new
            {
                key = cacheKey,
                configuration = evaluated.Configuration,
                targetFramework = evaluated.TargetFramework,
                defineConstants = evaluated.DefineConstants,
                compileFiles = evaluated.CompileFiles,
            };
            File.WriteAllText(cachePath, JsonSerializer.Serialize(payload));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A read-only tree still works; the next run simply re-evaluates.
            _ = exception;
        }
    }

    private static string Trim(string value)
    {
        var collapsed = value.ReplaceLineEndings(" ").Trim();
        return collapsed.Length <= 300 ? collapsed : collapsed[..300];
    }

    /// <summary>
    /// Walks the project and its resolvable imports (plus the auto-imported root build files), collecting the
    /// build files that contributed Compile items and the external search roots of literal globs. Any
    /// <c>$(...)</c> Compile item or unresolvable import marks the dependency set as not fully understood.
    /// </summary>
    internal static class DependencyProbe
    {
        internal sealed record Result(
            bool IsFullyUnderstood,
            IReadOnlyList<string> BuildFiles,
            IReadOnlyList<string> ExternalRoots);

        public static Result Collect(string repoRoot, string projectFile, string projectDirectory)
        {
            var buildFiles = new List<string>();
            var externalRoots = new List<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<(string File, int Depth)>();
            queue.Enqueue((Path.GetFullPath(projectFile), 0));
            foreach (var shared in AutoImportedPropertyFiles)
            {
                var path = Path.Combine(repoRoot, shared);
                if (File.Exists(path))
                {
                    queue.Enqueue((Path.GetFullPath(path), 1));
                }
            }

            var fullyUnderstood = true;
            while (queue.Count > 0)
            {
                var (file, depth) = queue.Dequeue();
                if (!visited.Add(file))
                {
                    continue;
                }

                XDocument document;
                try
                {
                    document = XDocument.Load(file);
                }
                catch (XmlException)
                {
                    fullyUnderstood = false;
                    continue;
                }

                buildFiles.Add(file);

                foreach (var element in document.Descendants("Compile"))
                {
                    foreach (var attribute in new[] { "Include", "Remove" })
                    {
                        var value = element.Attribute(attribute)?.Value;
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            continue;
                        }

                        var expanded = SubstituteKnownProperties(value, file, projectFile, projectDirectory);
                        if (expanded is null)
                        {
                            // The expansion may point at any directory; its roots cannot be proven from source.
                            fullyUnderstood = false;
                            continue;
                        }

                        var root = ExternalRootOf(expanded.Replace('\\', '/'), projectDirectory);
                        if (root is not null && !externalRoots.Contains(root, StringComparer.Ordinal))
                        {
                            externalRoots.Add(root);
                        }
                    }
                }

                if (depth >= 8)
                {
                    continue;
                }

                foreach (var import in document.Descendants("Import"))
                {
                    var value = import.Attribute("Project")?.Value;
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    var expanded = SubstituteKnownProperties(value, file, projectFile, projectDirectory);
                    if (expanded is null)
                    {
                        fullyUnderstood = false;
                        continue;
                    }

                    var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, expanded.Replace('\\', '/')));
                    if (File.Exists(resolved))
                    {
                        queue.Enqueue((resolved, depth + 1));
                    }
                    else
                    {
                        fullyUnderstood = false;
                    }
                }
            }

            return new Result(fullyUnderstood, buildFiles, externalRoots);
        }

        /// <summary>
        /// Substitutes the standard file-scoped MSBuild properties that only depend on the importing file. Any
        /// other property expression stays unresolved, and unresolved means "do not trust the cache".
        /// </summary>
        private static string? SubstituteKnownProperties(string value, string importingFile, string projectFile, string projectDirectory)
        {
            var expanded = value
                .Replace("$(MSBuildThisFileDirectory)", Path.GetDirectoryName(importingFile)! + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                .Replace("$(MSBuildThisFile)", Path.GetFileName(importingFile), StringComparison.Ordinal)
                .Replace("$(MSBuildProjectDirectory)", projectDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                .Replace("$(MSBuildProjectName)", Path.GetFileNameWithoutExtension(projectFile), StringComparison.Ordinal);

            return expanded.Contains("$(", StringComparison.Ordinal) ? null : expanded;
        }

        private static string? ExternalRootOf(string value, string projectDirectory)
        {
            var wildcard = value.IndexOfAny(['*', '?']);
            var directoryPart = wildcard < 0
                ? Path.GetDirectoryName(value)
                : value[..wildcard] is { } prefix && prefix.Contains('/', StringComparison.Ordinal)
                    ? prefix[..prefix.LastIndexOf('/')]
                    : string.Empty;

            if (string.IsNullOrEmpty(directoryPart))
            {
                return null;
            }

            var resolved = Path.GetFullPath(Path.Combine(projectDirectory, directoryPart));
            var projectRoot = Path.GetFullPath(projectDirectory);
            var outside = !resolved.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !string.Equals(resolved, projectRoot, StringComparison.Ordinal);
            return outside && Directory.Exists(resolved) ? resolved : null;
        }
    }
}
