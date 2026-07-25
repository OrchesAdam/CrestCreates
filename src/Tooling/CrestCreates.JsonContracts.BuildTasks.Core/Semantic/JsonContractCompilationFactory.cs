using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CrestCreates.JsonContracts.BuildTasks.Semantic;

public static class JsonContractCompilationFactory
{
    public static CSharpCompilation Create(
        string assemblyName,
        IEnumerable<(string Path, string Text)> sourceFiles,
        IEnumerable<string> referencePaths,
        string languageVersion,
        IEnumerable<string> defineConstants,
        string nullable,
        bool allowUnsafeBlocks)
    {
        return CreateCompilation(assemblyName, sourceFiles, referencePaths, languageVersion, defineConstants, nullable, allowUnsafeBlocks);
    }

    public static CSharpCompilation CreateCompilation(
        string assemblyName,
        IEnumerable<(string Path, string Text)> sourceFiles,
        IEnumerable<string> referencePaths,
        string languageVersion,
        IEnumerable<string> defineConstants,
        string nullable,
        bool allowUnsafeBlocks)
    {
        var sortedSources = sourceFiles
            .OrderBy(s => s.Path, StringComparer.Ordinal)
            .ToList();

        var parseOptions = CreateParseOptions(languageVersion, defineConstants, nullable);

        var syntaxTrees = new List<SyntaxTree>();
        foreach (var (path, text) in sortedSources)
        {
            var tree = CSharpSyntaxTree.ParseText(text, parseOptions, path);
            syntaxTrees.Add(tree);
        }

        var references = new List<MetadataReference>();
        foreach (var refPath in referencePaths.OrderBy(p => p, StringComparer.Ordinal))
        {
            references.Add(MetadataReference.CreateFromFile(refPath));
        }

        if (references.Count == 0)
        {
            foreach (var frameworkRef in GetDefaultReferences())
                references.Add(frameworkRef);
        }

        var specificDiags = ImmutableDictionary.CreateRange<string, ReportDiagnostic>(new[]
        {
            KeyValuePair.Create("CS0534", ReportDiagnostic.Suppress),
            KeyValuePair.Create("CS7036", ReportDiagnostic.Suppress),
            KeyValuePair.Create("CS0518", ReportDiagnostic.Suppress),
            KeyValuePair.Create("CS0433", ReportDiagnostic.Suppress),
        });

        var compilationOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            allowUnsafe: allowUnsafeBlocks,
            nullableContextOptions: ParseNullableContextOptions(nullable),
            specificDiagnosticOptions: specificDiags);

        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees.ToImmutableArray(),
            references.ToImmutableArray(),
            compilationOptions);
    }

    private static CSharpParseOptions CreateParseOptions(
        string languageVersion,
        IEnumerable<string> defineConstants,
        string nullable)
    {
        var langVer = languageVersion.Equals("latest", StringComparison.OrdinalIgnoreCase)
            ? LanguageVersion.Latest
            : languageVersion.Equals("preview", StringComparison.OrdinalIgnoreCase)
                ? LanguageVersion.Preview
                : LanguageVersion.Default;

        var symbols = defineConstants
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d.Trim())
            .ToList();

        return new CSharpParseOptions(langVer)
            .WithPreprocessorSymbols(symbols);
    }

    private static NullableContextOptions ParseNullableContextOptions(string nullable)
    {
        return nullable.Equals("enable", StringComparison.OrdinalIgnoreCase)
            ? NullableContextOptions.Enable
            : nullable.Equals("warnings", StringComparison.OrdinalIgnoreCase)
                ? NullableContextOptions.Warnings
                : nullable.Equals("disable", StringComparison.OrdinalIgnoreCase)
                    ? NullableContextOptions.Disable
                    : NullableContextOptions.Enable;
    }

    private static List<MetadataReference> GetDefaultReferences()
    {
        var refs = new List<MetadataReference>();
        var trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (trusted == null) return refs;

        foreach (var path in trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
            var name = Path.GetFileNameWithoutExtension(path);
            if (IsRelevantFrameworkAssembly(name))
            {
                try { refs.Add(MetadataReference.CreateFromFile(path)); }
                catch { }
            }
        }

        return refs;
    }

    private static bool IsRelevantFrameworkAssembly(string name)
    {
        return name.StartsWith("System.", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.", StringComparison.Ordinal)
            || name == "netstandard"
            || name == "mscorlib";
    }
}
