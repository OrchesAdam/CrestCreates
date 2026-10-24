using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
/// Reads the Compile items and preprocessor symbols from MSBuild itself. Deriving them from the csproj text
/// would have to re-implement condition evaluation, imports and property functions — and any item this guard
/// does not understand would silently be applied or dropped. The evaluated answer is cached per project and
/// configuration, keyed by the project inputs, so a warm run costs nothing while a changed build script or file
/// set is always re-evaluated.
/// </summary>
internal static class TenantIdentityEvaluatedInputs
{
    private static readonly string[] SharedPropertyFiles =
    [
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Build.Aot.props",
        "Directory.Packages.props",
    ];

    public static EvaluatedProjectInputs Read(string repoRoot, string projectFile, string configuration)
    {
        var projectDirectory = Path.GetDirectoryName(projectFile)!;
        var cacheKey = CacheKey(repoRoot, projectFile, projectDirectory, configuration);
        var cachePath = Path.Combine(projectDirectory, "obj", $"crestcreates-tenant-inputs.{configuration}.json");

        if (TryReadCache(cachePath, cacheKey) is { } cached)
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

    private static string CacheKey(string repoRoot, string projectFile, string projectDirectory, string configuration)
    {
        var builder = new StringBuilder();
        // The absolute project directory is part of the key: a copied tree (mutation/scratch controls) has the
        // same project text and file list but different absolute Compile paths.
        builder.Append(Path.GetFullPath(projectDirectory)).Append('\n');
        builder.Append(configuration).Append('\n');
        builder.Append(File.ReadAllText(projectFile)).Append('\n');

        foreach (var shared in SharedPropertyFiles)
        {
            var path = Path.Combine(repoRoot, shared);
            if (File.Exists(path))
            {
                builder.Append(shared).Append(':').Append(File.ReadAllText(path)).Append('\n');
            }
        }

        foreach (var script in Directory.EnumerateFiles(projectDirectory, "*.props", SearchOption.TopDirectoryOnly)
                     .Concat(Directory.EnumerateFiles(projectDirectory, "*.targets", SearchOption.TopDirectoryOnly))
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            builder.Append(Path.GetFileName(script)).Append(':').Append(File.ReadAllText(script)).Append('\n');
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

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
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
}
