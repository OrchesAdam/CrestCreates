using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CrestCreates.JsonContracts.BuildTasks.Tests.Infrastructure;

public class JsonContractCompilationTestBase
{
    private static List<string>? _cachedReferences;

    protected internal CSharpCompilation CreateCompilation(
        string assemblyName,
        IEnumerable<(string Path, string Text)> sourceFiles,
        IEnumerable<string>? referencePaths = null,
        string languageVersion = "latest",
        IEnumerable<string>? defineConstants = null,
        string nullable = "enable",
        bool allowUnsafeBlocks = false)
    {
        var refs = referencePaths ?? GetDefaultReferences();
        return JsonContractCompilationFactory.Create(
            assemblyName,
            sourceFiles,
            refs,
            languageVersion,
            defineConstants ?? [],
            nullable,
            allowUnsafeBlocks);
    }

    protected static List<string> GetDefaultReferences()
    {
        if (_cachedReferences is not null)
            return _cachedReferences;

        var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddTrustedPlatformAssemblies(refs);
        AddRefAssemblies(refs);
        AddProjectOutputReferences(refs);

        var result = refs.ToList();
        result.Sort(StringComparer.Ordinal);
        _cachedReferences = result;
        return result;
    }

    private static void AddTrustedPlatformAssemblies(HashSet<string> refs)
    {
        var trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (trusted is null)
            return;

        foreach (var path in trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (IsRelevantRuntimeAssembly(fileName))
                refs.Add(path);
        }
    }

    private static bool IsRelevantRuntimeAssembly(string name)
    {
        return name.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
            || name.Equals("netstandard", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("CrestCreates", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddRefAssemblies(HashSet<string> refs)
    {
        var packDir = "/usr/lib/dotnet/packs/Microsoft.NETCore.App.Ref";
        if (!Directory.Exists(packDir))
            return;

        var latestVersion = Directory.GetDirectories(packDir)
            .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (latestVersion is null)
            return;

        var refDir = Path.Combine(latestVersion, "ref", "net10.0");
        if (!Directory.Exists(refDir))
            return;

        foreach (var file in Directory.GetFiles(refDir, "*.dll"))
            refs.Add(file);
    }

    private static void AddProjectOutputReferences(HashSet<string> refs)
    {
        var assemblyLocation = typeof(JsonContractCompilationTestBase).Assembly.Location;
        var binDir = Path.GetDirectoryName(assemblyLocation);
        if (binDir is null)
            return;

        foreach (var file in Directory.GetFiles(binDir, "*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.StartsWith("CrestCreates", StringComparison.OrdinalIgnoreCase))
                refs.Add(file);
        }
    }
}
