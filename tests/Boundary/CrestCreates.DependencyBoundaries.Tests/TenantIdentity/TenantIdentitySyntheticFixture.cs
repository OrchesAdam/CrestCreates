using Microsoft.CodeAnalysis;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

/// <summary>
/// Synthetic tenant-identity samples. The samples bind against the real production contracts loaded from the
/// repository build outputs, so a pattern only passes because the guard understands the actual declarations —
/// no stub contract can drift away from the main chain.
/// </summary>
internal static class TenantIdentitySyntheticFixture
{
    private static readonly string[] ContractAssemblies =
    [
        "CrestCreates.MultiTenancy.Abstract",
        "CrestCreates.Domain",
        "CrestCreates.Caching",
        "CrestCreates.DataFilter",
        "CrestCreates.EventBus.Abstract",
    ];

    private static readonly Lazy<IReadOnlyList<MetadataReference>> s_references = new(() =>
    {
        var paths = new List<string>(((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? []);

        var root = FindRepositoryRoot();
        foreach (var assembly in ContractAssemblies)
        {
            var located = Directory
                .EnumerateFiles(Path.Combine(root, "src"), $"{assembly}.dll", SearchOption.AllDirectories)
                .Where(path => path.Contains("/bin/", StringComparison.Ordinal)
                               && !path.Contains("/obj/", StringComparison.Ordinal))
                .OrderBy(path => path.Length)
                .FirstOrDefault();

            Assert.NotNull(located);
            paths.Add(located!);
        }

        return paths
            .Distinct(StringComparer.Ordinal)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    });

    public static TenantIdentityAnalysis Analyze(params (string Path, string Source)[] samples) =>
        Run(samples, unboundContract: false);

    /// <summary>
    /// Samples whose receiver and directory types exist nowhere: the sensitive call cannot bind, which must
    /// surface as a TI900 coverage failure instead of an empty (green) result.
    /// </summary>
    public static TenantIdentityAnalysis AnalyzeWithUnboundContracts(params (string Path, string Source)[] samples) =>
        Run(samples, unboundContract: true);

    private static TenantIdentityAnalysis Run((string Path, string Source)[] samples, bool unboundContract)
    {
        var sources = samples
            .Select(sample => new TenantSourceFile($"synthetic/{sample.Path}", $"synthetic/{sample.Path}", sample.Source))
            .ToArray();

        var project = new TenantProjectInput(
            Project: unboundContract ? "synthetic/unbound.csproj" : "synthetic/tenantidentitysample.csproj",
            ProjectDirectory: Path.GetTempPath(),
            AssemblyName: unboundContract ? "CrestCreates.SyntheticUnbound" : "CrestCreates.SyntheticTenantSample",
            IsWebSdk: false,
            ImplicitUsings: "disable",
            Nullable: "enable",
            OutputType: "Library",
            Sources: sources,
            GeneratedSources: [],
            References: s_references.Value,
            ReferenceError: null);

        var analyzer = new TenantIdentityAnalyzer();
        analyzer.AnalyzeProject(project);
        return analyzer.Analysis;
    }

    public static IReadOnlyList<TenantIdentityDiagnostic> Violations(params (string Path, string Source)[] samples) =>
        [.. Analyze(samples).Violations];

    public static IReadOnlyList<TenantIdentityDiagnostic> CoverageFailures(params (string Path, string Source)[] samples) =>
        [.. Analyze(samples).CoverageFailures];

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CrestCreates.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
