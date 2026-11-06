using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace CrestCreates.DependencyBoundaries.Tests;

internal sealed record TenantSourceFile(string Project, string RelativePath, string Text);

internal sealed record TenantProjectInput(
    string Project,
    string ProjectDirectory,
    string AssemblyName,
    bool IsWebSdk,
    string ImplicitUsings,
    string Nullable,
    string OutputType,
    string Configuration,
    string DefineConstants,
    IReadOnlyList<TenantSourceFile> Sources,
    IReadOnlyList<TenantSourceFile> GeneratedSources,
    IReadOnlyList<MetadataReference> References,
    string? ReferenceError,
    string? EvaluationError);

/// <summary>
/// Collects the real build inputs of every active production project so the guard analyses each
/// project inside its own reference closure instead of one fake assembly.
/// </summary>
internal static class TenantIdentityProjectInventory
{
    private static readonly StringComparer OrdinalPath = StringComparer.Ordinal;

    public static IReadOnlyList<string> EnumerateProjectFiles(string repoRoot)
    {
        var projects = new List<string>();
        foreach (var root in new[] { "src", "samples" })
        {
            var directory = Path.Combine(repoRoot, root);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories))
            {
                var normalized = Normalize(file);
                if (normalized.Contains("/bin/", StringComparison.Ordinal) || normalized.Contains("/obj/", StringComparison.Ordinal)
                    || normalized.Contains("/99_recyclebin/", StringComparison.Ordinal))
                {
                    continue;
                }

                if (IsTestProject(file))
                {
                    continue;
                }

                projects.Add(file);
            }
        }

        projects.Sort(OrdinalPath);
        return projects;
    }

    public static bool IsTestProject(string projectFile)
    {
        var normalized = Normalize(projectFile);
        if (normalized.Contains("/tests/", StringComparison.Ordinal))
        {
            return true;
        }

        var text = File.ReadAllText(projectFile);
        return text.Contains("<IsTestProject>true</IsTestProject>", StringComparison.OrdinalIgnoreCase)
            || text.Contains("<PackageReference Include=\"xunit", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Loads one project's real build inputs. Compile items and preprocessor symbols come from the evaluated
    /// MSBuild project (see <see cref="TenantIdentityEvaluatedInputs"/>), so conditions, imports and property
    /// functions are handled by MSBuild instead of by a partial re-implementation here.
    /// </summary>
    public static TenantProjectInput Load(string repoRoot, string projectFile, string? configurationOverride = null)
    {
        var projectRelative = Normalize(Path.GetRelativePath(repoRoot, projectFile));
        var projectDirectory = Path.GetDirectoryName(projectFile)!;
        var assemblyName = Path.GetFileNameWithoutExtension(projectFile);
        var sdk = XDocument.Load(projectFile).Root?.Attribute("Sdk")?.Value ?? string.Empty;
        var isWebSdk = sdk.Contains("Web", StringComparison.OrdinalIgnoreCase);

        var text = File.ReadAllText(projectFile);
        var configuredAssemblyName = MatchValue(text, "AssemblyName");
        var rootNamespace = MatchValue(text, "RootNamespace");
        if (configuredAssemblyName is { Length: > 0 })
        {
            assemblyName = configuredAssemblyName;
        }

        var implicitUsings = MatchValue(text, "ImplicitUsings") ?? "disable";
        var outputType = MatchValue(text, "OutputType") ?? "Library";
        var nullable = MatchValue(text, "Nullable") ?? (rootNamespace is null ? "enable" : "enable");

        var (references, referenceError, outputDirectory) = ResolveReferences(repoRoot, projectDirectory, assemblyName);
        var configuration = configurationOverride
            ?? (outputDirectory is null ? "Debug" : new DirectoryInfo(outputDirectory).Parent?.Name ?? "Debug");

        var evaluated = TenantIdentityEvaluatedInputs.Read(repoRoot, projectFile, configuration);
        // "could not obtain the inputs" and "inputs known but no symbol context" are different states: the
        // first hides everything and is always a coverage failure, the second only matters when the known
        // sources actually contain something to analyse.
        var evaluationError = evaluated.IsAvailable
            ? null
            : $"the guard could not evaluate the MSBuild Compile inputs for '{Path.GetFileName(projectFile)}': {evaluated.UnavailableReason}";

        var sources = evaluated.CompileFiles
            .Select(file => new TenantSourceFile(
                projectRelative,
                Path.GetRelativePath(repoRoot, file).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(file)))
            .OrderBy(source => source.RelativePath, OrdinalPath)
            .ToArray();

        var generated = outputDirectory is null ? [] : CollectGenerated(projectDirectory, outputDirectory);
        return new TenantProjectInput(
            projectRelative,
            projectDirectory,
            assemblyName,
            isWebSdk,
            implicitUsings,
            nullable,
            outputType,
            evaluated.Configuration,
            evaluated.DefineConstants,
            sources,
            generated,
            references,
            referenceError,
            evaluationError);
    }

    private static (IReadOnlyList<MetadataReference> References, string? Error, string? OutputDirectory) ResolveReferences(
        string repoRoot,
        string projectDirectory,
        string assemblyName)
    {
        var outputDirectory = FindOutputDirectory(projectDirectory, assemblyName);
        if (outputDirectory is null)
        {
            return (Array.Empty<MetadataReference>(), $"no build output for '{assemblyName}' under {Normalize(Path.GetRelativePath(repoRoot, projectDirectory))}/bin; run 'dotnet restore CrestCreates.slnx' and 'dotnet build CrestCreates.slnx' before this gate", null);
        }

        // Reference precedence: shared framework first, then the project's own build closure, then the
        // restored package compile list. Deduping by assembly simple name keeps old package copies such as
        // Microsoft.AspNetCore.Http.Abstractions 2.x out of the compilation; duplicates there produce
        // CS0433 and make every type in a file fail to bind.
        var paths = new List<string>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string dll)
        {
            var name = Path.GetFileNameWithoutExtension(dll);
            if (name.Length == 0 || !taken.Add(name))
            {
                return;
            }

            paths.Add(dll);
        }

        foreach (var dll in FrameworkReferences())
        {
            Add(dll);
        }

        foreach (var dll in Directory.EnumerateFiles(outputDirectory, "*.dll"))
        {
            var fileName = Path.GetFileName(dll);
            if (fileName.Equals($"{assemblyName}.dll", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Add(dll);
        }

        foreach (var dll in PackageCompileAssemblies(projectDirectory, assemblyName))
        {
            Add(dll);
        }

        var references = paths
            .Where(File.Exists)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();

        if (references.Length == 0)
        {
            return (Array.Empty<MetadataReference>(), $"build output {Normalize(outputDirectory)} contained no reference assemblies", null);
        }

        return (references, null, outputDirectory);
    }

    private static string? FindOutputDirectory(string projectDirectory, string assemblyName)
    {
        var binRoot = Path.Combine(projectDirectory, "bin");
        if (!Directory.Exists(binRoot))
        {
            return null;
        }

        var candidates = new List<(string Directory, int ConfigRank, int AssemblyCount, DateTime Written)>();
        foreach (var tfmDirectory in Directory.EnumerateDirectories(binRoot, "*", SearchOption.AllDirectories))
        {
            var expected = Path.Combine(tfmDirectory, $"{assemblyName}.dll");
            if (!File.Exists(expected))
            {
                continue;
            }

            var relative = Normalize(Path.GetRelativePath(binRoot, tfmDirectory));
            var configRank = relative.StartsWith("debug/", StringComparison.Ordinal) ? 0 : 1;
            var assemblies = Directory.EnumerateFiles(tfmDirectory, "*.dll").Count();
            candidates.Add((tfmDirectory, configRank, assemblies, File.GetLastWriteTimeUtc(expected)));
        }

        return candidates
            .OrderBy(candidate => candidate.ConfigRank)
            .ThenByDescending(candidate => candidate.AssemblyCount)
            .Select(candidate => candidate.Directory)
            .FirstOrDefault();
    }

    private static IEnumerable<string> FrameworkReferences()
    {
        var seen = new HashSet<string>(OrdinalPath);
        foreach (var path in ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
                    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
        {
            if (seen.Add(path))
            {
                yield return path;
            }
        }

        foreach (var path in SharedFrameworkAssemblies("Microsoft.AspNetCore.App"))
        {
            if (seen.Add(path))
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<string> SharedFrameworkAssemblies(string frameworkName)
    {
        var dotnetRoot = FindDotnetRoot();
        if (dotnetRoot is null)
        {
            return [];
        }

        var sharedRoot = Path.Combine(dotnetRoot, "shared", frameworkName);
        if (!Directory.Exists(sharedRoot))
        {
            return [];
        }

        var latestVersion = Directory.EnumerateDirectories(sharedRoot)
            .OrderByDescending(version => version, OrdinalPath)
            .FirstOrDefault();

        return latestVersion is null
            ? []
            : Directory.EnumerateFiles(latestVersion, "*.dll");
    }

    private static string? FindDotnetRoot()
    {
        var coreLib = typeof(object).Assembly.Location;
        if (string.IsNullOrEmpty(coreLib))
        {
            return null;
        }

        var versionDirectory = Path.GetDirectoryName(coreLib);
        var sharedRoot = versionDirectory is null ? null : Path.GetDirectoryName(versionDirectory);
        var dotnetRoot = sharedRoot is null ? null : Path.GetDirectoryName(sharedRoot);
        return dotnetRoot is not null && Directory.Exists(Path.Combine(dotnetRoot, "shared")) ? dotnetRoot : null;
    }

    /// <summary>
    /// Source-generator output is added to the guard compilation so authored code binds against the real
    /// generated declarations. It is never itself a scan target: only authored Compile sources are analysed.
    /// </summary>
    private static IReadOnlyList<TenantSourceFile> CollectGenerated(string projectDirectory, string outputDirectory)
    {
        // bin/Debug/net10.0 -> obj/Debug/net10.0
        var segments = Path.GetRelativePath(projectDirectory, outputDirectory).Split(Path.DirectorySeparatorChar);
        var objDirectory = segments.Length < 3
            ? projectDirectory
            : Path.Combine(projectDirectory, "obj", segments[^2], segments[^1]);
        if (!Directory.Exists(objDirectory))
        {
            return [];
        }

        var files = new List<TenantSourceFile>();
        foreach (var root in new[] { Path.Combine(objDirectory, "generated"), Path.Combine(objDirectory, "source-generators") })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                files.Add(new TenantSourceFile(
                    Normalize(Path.GetRelativePath(projectDirectory, file)),
                    Normalize(Path.GetRelativePath(Directory.GetCurrentDirectory() == string.Empty ? projectDirectory : FindRoot(), file)),
                    File.ReadAllText(file)));
            }
        }

        return files;
    }

    private static string? s_root;

    private static string FindRoot()
    {
        if (s_root is not null)
        {
            return s_root;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CrestCreates.slnx")))
        {
            directory = directory.Parent;
        }

        s_root = directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
        return s_root;
    }

    /// <summary>
    /// Transitive NuGet compile closure from the restored assets file. Library outputs do not copy every
    /// package assembly locally, so the build's own asset graph is the authoritative reference list.
    /// </summary>
    private static IEnumerable<string> PackageCompileAssemblies(string projectDirectory, string assemblyName)
    {
        var assetsPath = Path.Combine(projectDirectory, "obj", "project.assets.json");
        if (!File.Exists(assetsPath))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(File.OpenRead(assetsPath));
            var folders = document.RootElement.TryGetProperty("packageFolders", out var packageFolders)
                ? packageFolders.EnumerateObject().Select(property => property.Name).ToArray()
                : [];
            if (!document.RootElement.TryGetProperty("targets", out var targets))
            {
                return [];
            }

            var found = new List<string>();
            foreach (var framework in targets.EnumerateObject())
            {
                foreach (var package in framework.Value.EnumerateObject())
                {
                    if (!package.Value.TryGetProperty("compile", out var compile))
                    {
                        continue;
                    }

                    foreach (var asset in compile.EnumerateObject())
                    {
                        var relative = asset.Name.Replace("\\", "/");
                        if (relative.EndsWith("_._", StringComparison.Ordinal)
                            || !relative.EndsWith(".dll", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        foreach (var folder in folders)
                        {
                            var candidate = Path.Combine(
                                folder,
                                package.Name.Split('/')[0].ToLowerInvariant(),
                                package.Name.Split('/')[1],
                                relative);
                            if (File.Exists(candidate))
                            {
                                found.Add(candidate);
                                break;
                            }
                        }
                    }
                }
            }

            _ = assemblyName;
            return found;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string ConfigurationOf(string? outputDirectory) =>
        outputDirectory is null ? "Debug" : new DirectoryInfo(outputDirectory).Parent?.Name ?? "Debug";

    private static string? MatchValue(string projectText, string elementName)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            projectText,
            $"<{elementName}>([^<]*)</{elementName}>",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string Normalize(string path) =>
        path.Replace('\\', '/').ToLowerInvariant();
}
