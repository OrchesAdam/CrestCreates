using System.Text;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

/// <summary>
/// #131 TenantId architecture guard. TenantId is the tenant context primary key; a tenant name stays
/// display, log, DTO and directory-lookup text and never becomes identity, claim, authorization, filtering,
/// cache-partition or connection material. Acceptance matrix TI-P01…TI-M02 from design §4.3.
/// </summary>
public class TenantIdentityArchitectureTests
{
    private static readonly Lazy<TenantIdentityAnalysis> s_repository = new(() =>
    {
        var analyzer = new TenantIdentityAnalyzer();
        analyzer.AnalyzeRepository(FindRepositoryRoot());
        return analyzer.Analysis;
    });

    // ------------------------------------------------------------ real repository gate

    [Fact]
    public void TI01_RealRepository_NoTenantNameReachesIdentitySlot()
    {
        var analysis = s_repository.Value;
        Assert.True(
            !analysis.Violations.Any(),
            "Tenant name values reached tenant identity slots:" + Environment.NewLine + Report(analysis.Violations));
    }

    [Fact]
    public void TI02_RealRepository_EverySensitiveNodeWasEnumeratedAndBound()
    {
        var analysis = s_repository.Value;
        Assert.True(
            !analysis.CoverageFailures.Any(),
            "TenantId guard coverage failures (TI900):" + Environment.NewLine + Report(analysis.CoverageFailures));
    }

    [Fact]
    public void TI03_MainChain_ExitsReadTheAuthoritativeIdSource()
    {
        var violations = TenantIdentityAuthoritativeLedger.Evaluate(s_repository.Value).ToArray();
        Assert.True(
            violations.Length == 0,
            "Locked tenant identity sites no longer read the authoritative id source:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void TI_C01a_Enumeration_CoversEveryActiveProductionProjectAndFile()
    {
        var analysis = s_repository.Value;
        var root = FindRepositoryRoot();

        var projectFiles = TenantIdentityProjectInventory.EnumerateProjectFiles(root);
        Assert.True(
            projectFiles.Count > 100,
            $"expected the active src+samples project set to be enumerated, found {projectFiles.Count}");
        Assert.Equal(projectFiles.Count, analysis.ProjectsEnumerated);

        var expectedFiles = projectFiles
            .Select(project => Path.GetDirectoryName(project)!)
            .Distinct(StringComparer.Ordinal)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Count(IsAuthoredSource);
        Assert.Equal(expectedFiles, analysis.FilesEnumerated);

        Assert.Contains(
            analysis.CompiledProjects,
            project => project.StartsWith("src/framework/infrastructure/crestcreates.multitenancy/", StringComparison.Ordinal));
    }

    [Fact]
    public void TI_C01b_PositiveControl_EachIdentityPortIsActuallyObserved()
    {
        var observed = s_repository.Value.Exits
            .GroupBy(exit => exit.PortLabel, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        foreach (var port in new[]
                 {
                     "ICurrentTenant.ChangeAsync(tenantId)",
                     "ICurrentTenant.SetTenantId(tenantId)",
                     "TenantResolutionResult.Success(tenantId, tenantName, ..) slot 0",
                     "TenantCacheKeyContributor.GetTenantCacheKey(tenantId, ..)",
                     "Claim(tenantid|tenant_id) value slot",
                     "entity tenant filter comparison",
                     "tenant identity member write",
                     "tenant identity member initializer",
                 })
        {
            Assert.True(
                observed.TryGetValue(port, out var count) && count > 0,
                $"identity port '{port}' produced no observations: the scan or the port table was removed");
        }

        Assert.True(
            observed["TenantResolutionResult.Success(tenantId, tenantName, ..) slot 0"] >= 5,
            "every tenant resolver must bind the resolution success slot");
    }

    [Fact]
    public void TI_C01c_UnboundSensitivePort_FailsClosedInsteadOfReportingClean()
    {
        const string sample = """
            using System.Threading.Tasks;

            namespace CrestCreates.Samples.MissingContract;

            public sealed class Resolver
            {
                public async Task SwitchAsync(UnregisteredTenantHandle currentTenant, string tenantId) =>
                    await currentTenant.ChangeAsync(tenantId);
            }
            """;

        var coverage = TenantIdentitySyntheticFixture.AnalyzeWithUnboundContracts(("Missing.cs", sample)).CoverageFailures.ToArray();
        Assert.Contains(coverage, diagnostic => diagnostic.Rule == TenantIdentityRule.TI900);
    }

    [Fact]
    public void TI_C01d_MissingBuildInputs_FailsClosed()
    {
        var project = new TenantProjectInput(
            Project: "synthetic/unbuilt.csproj",
            ProjectDirectory: Path.GetTempPath(),
            AssemblyName: "CrestCreates.Unbuilt",
            IsWebSdk: false,
            ImplicitUsings: "disable",
            Nullable: "enable",
            OutputType: "Library",
            Sources: [new TenantSourceFile("synthetic/a.cs", "synthetic/a.cs", "namespace CrestCreates.X { public interface ICurrentTenant { System.Threading.Tasks.Task SwitchAsync(string tenantId); } }")],
            GeneratedSources: [],
            References: [],
            ReferenceError: "no build output");

        var analyzer = new TenantIdentityAnalyzer();
        analyzer.AnalyzeProject(project);
        Assert.Contains(analyzer.Analysis.CoverageFailures, diagnostic => diagnostic.Rule == TenantIdentityRule.TI900);
    }

    [Fact]
    public void TI_C01e_GateCoverageReport_IsNonEmptyAndAuditable()
    {
        var analysis = s_repository.Value;
        Assert.True(analysis.CompiledProjects.Count > 50, "the gate must compile real projects, not run empty");
        Assert.True(analysis.IndexedHelperMembers > 500, "first-party helpers must be indexed for summaries");
        Assert.True(analysis.Exits.Count > 500, "identity exits must be observed across the repository");

        var builder = new StringBuilder();
        builder.AppendLine($"projects enumerated    : {analysis.ProjectsEnumerated}");
        builder.AppendLine($"files enumerated       : {analysis.FilesEnumerated}");
        builder.AppendLine($"candidate files        : {analysis.FilesWithSensitiveCandidates}");
        builder.AppendLine($"compiled projects      : {analysis.CompiledProjects.Count}");
        builder.AppendLine($"indexed helper members : {analysis.IndexedHelperMembers}");
        builder.AppendLine($"identity exit sites    : {analysis.Exits.Count}");
        builder.AppendLine($"diagnostics            : {analysis.Diagnostics.Count}");
        foreach (var group in analysis.Exits.GroupBy(exit => exit.PortLabel).OrderByDescending(group => group.Count()))
        {
            builder.AppendLine($"  exit {group.Count(),4}  {group.Key}");
        }

        foreach (var diagnostic in analysis.Diagnostics)
        {
            builder.AppendLine("  " + diagnostic);
        }

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "issue131-gate-report.txt"), builder.ToString());
    }

    // ------------------------------------------------------------ allowed flows (TI-P)

    [Fact]
    public void TI_P01_DisplayLogDtoAndComments_AreNotReported()
    {
        const string sample = """
            using System;
            using CrestCreates.Domain.Permission;
            using CrestCreates.MultiTenancy.Abstract;

            namespace CrestCreates.Samples.Display;

            // TenantName here is prose: the guard must not read comments as identity flows.
            public sealed class DisplayOnly
            {
                public string Describe(Tenant directory, string tenantName, ITenantInfo info)
                {
                    var heading = $"tenant {directory.Name} / {directory.DisplayName} / {tenantName}";
                    Console.WriteLine("TenantName is only display text here");
                    return heading + info.Name + new Widget { Name = "widget" }.Name;
                }
            }

            public sealed class Widget
            {
                public string Name { get; set; } = string.Empty;
            }
            """;

        Assert.Empty(TenantIdentitySyntheticFixture.Violations(("Display.cs", sample)));
        Assert.Empty(TenantIdentitySyntheticFixture.CoverageFailures(("Display.cs", sample)));
    }

    [Fact]
    public void TI_P02_NameLookupThenDirectoryIdAndIdPlusNamePassthrough_AreAllowed()
    {
        const string sample = """
            using CrestCreates.Caching;
            using CrestCreates.Domain.Permission;
            using CrestCreates.EventBus;
            using CrestCreates.MultiTenancy.Abstract;

            namespace CrestCreates.Samples.Lookup;

            public sealed class DirectoryResolution
            {
                private readonly ITenantEventContextAccessor _accessor = null!;
                private readonly TenantCacheKeyContributor _cache = null!;

                public TenantResolutionResult Resolve(string rawValue, Tenant directory)
                {
                    var lookupKey = rawValue.Trim().ToLowerInvariant();
                    var resolution = TenantResolutionResult.Success(directory.Id.ToString(), directory.Name, null, lookupKey);

                    // id and name keep their separate roles: id into identity, name into display and value parts.
                    _accessor.SetTenantContext(resolution.TenantId, resolution.TenantName);
                    _cache.GetTenantCacheKey(resolution.TenantId, "Audit", resolution.TenantName);
                    return resolution;
                }
            }
            """;

        Assert.Empty(TenantIdentitySyntheticFixture.Violations(("Lookup.cs", sample)));
    }

    // ------------------------------------------------------------ forbidden flows (TI-N)

    [Fact]
    public void TI_N01_NameReachesContextResolutionOrEventSlot_IsReported()
    {
        const string sample = """
            using System;
            using System.Threading.Tasks;
            using CrestCreates.Domain.Permission;
            using CrestCreates.EventBus;
            using CrestCreates.MultiTenancy.Abstract;

            namespace CrestCreates.Samples.Context;

            public sealed class NameIntoContext
            {
                public async Task Direct(ICurrentTenant currentTenant, Tenant directory) =>
                    await currentTenant.ChangeAsync(directory.Name);

                public async Task Alias(ICurrentTenant currentTenant, Tenant directory)
                {
                    var tenantId = directory.Name;
                    await currentTenant.ChangeAsync(tenantId);
                }

                public async Task Trimmed(ICurrentTenant currentTenant, Tenant directory) =>
                    await currentTenant.ChangeAsync(directory.Name.Trim());

                public async Task Interpolated(ICurrentTenant currentTenant, Tenant directory) =>
                    await currentTenant.ChangeAsync($"{directory.DisplayName}");

                public async Task Coalesced(ICurrentTenant currentTenant, string? tenantName) =>
                    await currentTenant.ChangeAsync(currentTenant.Id ?? tenantName);

                public TenantResolutionResult ResolutionSlot(Tenant directory) =>
                    TenantResolutionResult.Success(directory.Name, directory.Name, null, "Header");

                public void EventSlot(ITenantEventContextAccessor accessor, TenantEventContext context) =>
                    accessor.SetTenantContext(context.TenantName);
            }
            """;

        var violations = TenantIdentitySyntheticFixture.Violations(("Context.cs", sample));
        Assert.Equal(8, violations.Count);
        Assert.Equal(7, violations.Count(violation => violation.Rule == TenantIdentityRule.TI001));
        Assert.Equal(5, violations.Count(violation => violation.Exit.Contains("ChangeAsync", StringComparison.Ordinal)));
        Assert.Contains(violations, violation => violation.Rule == TenantIdentityRule.TI003
                                                && violation.Exit.Contains("identity-named local", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Exit.Contains("Success", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Exit.Contains("SetTenantContext", StringComparison.Ordinal));
    }

    [Fact]
    public void TI_N02_NameReachesClaimsCacheFilterOrPartition_IsReported()
    {
        const string sample = """
            using System.Linq;
            using System.Security.Claims;
            using CrestCreates.Caching;
            using CrestCreates.DataFilter.Entities;
            using CrestCreates.Domain.Permission;

            namespace CrestCreates.Samples.Isolation;

            public sealed class NameIntoIsolation
            {
                public Claim[] Claims(Tenant directory) =>
                    [new Claim("tenantid", directory.Name), new Claim("tenant_id", directory.NormalizedName)];

                public string Cache(TenantCacheKeyContributor contributor, Tenant directory) =>
                    contributor.GetTenantCacheKey(directory.Name, "Audit");

                public string Permission(TenantCacheKeyContributor contributor, string tenantName) =>
                    contributor.GetPermissionCacheKey(tenantName, "Role", "admin");

                public Row[] Filter(Row[] rows, Tenant directory) =>
                    rows.Where(row => row.TenantId == directory.Name).ToArray();

                public void Write(IMultiTenant entity, Tenant directory) => entity.TenantId = directory.DisplayName;

                public string AuditDisplayOnly(Tenant directory) => directory.DisplayName;
            }

            public sealed class Row : IMultiTenant
            {
                public string? TenantId { get; set; }
            }
            """;

        var violations = TenantIdentitySyntheticFixture.Violations(("Isolation.cs", sample));
        Assert.Equal(6, violations.Count);

        // Identity claims are TI001; authorization scope, cache prefix, entity filter and entity writes are TI002.
        Assert.Equal(2, violations.Count(violation => violation.Exit.Contains("Claim", StringComparison.Ordinal)
                                                      && violation.Rule == TenantIdentityRule.TI001));
        Assert.Equal(4, violations.Count(violation => violation.Rule == TenantIdentityRule.TI002));
        Assert.Contains(violations, violation => violation.Exit.Contains("GetTenantCacheKey", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Exit.Contains("GetPermissionCacheKey", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Exit.Contains("filter comparison", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Exit.Contains("member write", StringComparison.Ordinal));
    }

    [Fact]
    public void TI_N03_NameMasqueradingAsIdentity_IsReportedThroughAliasesAndNamedArguments()
    {
        const string masquerade = """
            using System.Threading.Tasks;
            using CrestCreates.Domain.Permission;
            using CrestCreates.MultiTenancy.Abstract;

            namespace CrestCreates.Samples.Masquerade;

            public sealed class NameAsIdentity : ITenantContext
            {
                private readonly Tenant _directory = null!;

                public string? CurrentTenantId => _directory.Name;

                public string ReadTenantId() => _directory.NormalizedName;

                public ITenantInfo Wrap(Tenant directory) => new TenantInfo(directory.Name, directory.Name, null);

                private string TenantNameOf(Tenant directory) => directory.Name;

                public async Task ViaHelper(ICurrentTenant currentTenant, Tenant directory) =>
                    await currentTenant.ChangeAsync(TenantNameOf(directory));

                public async Task NamedArgument(ICurrentTenant currentTenant, Tenant directory) =>
                    await currentTenant.ChangeAsync(tenantId: directory.DisplayName);
            }
            """;

        const string aliased = """
            using System.Threading.Tasks;
            using TenantEntity = CrestCreates.Domain.Permission.Tenant;
            using CrestCreates.MultiTenancy.Abstract;

            namespace CrestCreates.Samples.Aliased;

            public sealed class FullyQualified
            {
                public async Task Direct(ICurrentTenant currentTenant, TenantEntity directory) =>
                    await currentTenant.ChangeAsync(TenantExtensions.ReadName(directory));
            }

            public static class TenantExtensions
            {
                public static string ReadName(TenantEntity directory) => directory.Name;
            }
            """;

        var violations = TenantIdentitySyntheticFixture.Violations(("Masquerade.cs", masquerade), ("Aliased.cs", aliased));

        Assert.Contains(violations, violation => violation.Rule == TenantIdentityRule.TI003
                                                && violation.Exit.Contains("CurrentTenantId", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Rule == TenantIdentityRule.TI003
                                                && violation.Exit.Contains("ReadTenantId", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Rule == TenantIdentityRule.TI003
                                                && violation.Exit.Contains("constructor id slot", StringComparison.Ordinal));
        Assert.Equal(5, violations.Count(violation => violation.RelativePath.EndsWith("Masquerade.cs", StringComparison.Ordinal)));
        Assert.Equal(3, violations.Count(violation => violation.RelativePath.EndsWith("Masquerade.cs", StringComparison.Ordinal)
                                                 && violation.Rule == TenantIdentityRule.TI003));
        Assert.Contains(violations, violation => violation.RelativePath.EndsWith("Aliased.cs", StringComparison.Ordinal)
                                              && violation.Rule == TenantIdentityRule.TI001);
    }

    // ------------------------------------------------------------ mutations (TI-M)

    [Fact]
    public void TI_M01_ResolverReturningNameInsteadOfId_IsCaughtOnRealSource()
    {
        var (violations, ledger) = Mutate(
            "src/Framework/Infrastructure/CrestCreates.MultiTenancy",
            "Resolvers/HeaderTenantResolver.cs",
            text => text.Replace(
                "TenantResolutionResult.Success(tenant.Id.ToString(), tenant.Name,",
                "TenantResolutionResult.Success(tenant.Name, tenant.Name,"));

        Assert.Contains(violations, violation =>
            violation.Rule == TenantIdentityRule.TI001
            && violation.RelativePath.EndsWith("HeaderTenantResolver.cs", StringComparison.Ordinal)
            && violation.Exit.Contains("Success", StringComparison.Ordinal));
        Assert.Contains(ledger, violation => violation.Contains("HeaderTenantResolver.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void TI_M02_ClaimSwappedToUserName_IsCaughtByTheAuthoritativeLedger()
    {
        var (_, ledger) = Mutate(
            "src/Framework/Infrastructure/CrestCreates.Infrastructure",
            "Authorization/IdentityClaimsBuilder.cs",
            text => text.Replace(
                "new(\"tenantid\", context.TenantId ?? string.Empty),",
                "new(\"tenantid\", context.UserName),"));

        Assert.Contains(ledger, violation => violation.Contains("IdentityClaimsBuilder.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void TI_M02_AuditCachePartitionSwappedToTenantName_IsCaught()
    {
        var (violations, ledger) = Mutate(
            "src/Framework/Infrastructure/CrestCreates.Authorization",
            "AuditTenantContextResolver.cs",
            text => text.Replace(
                "GetTenantCacheKey(ResolveTenantIdOrNull(), \"Audit\")",
                "GetTenantCacheKey(_currentTenant.Tenant?.Name, \"Audit\")"));

        Assert.Contains(violations, violation =>
            violation.Rule == TenantIdentityRule.TI002
            && violation.RelativePath.EndsWith("AuditTenantContextResolver.cs", StringComparison.Ordinal));
        Assert.Contains(ledger, violation => violation.Contains("AuditTenantContextResolver.cs", StringComparison.Ordinal));
    }

    private static (IReadOnlyList<TenantIdentityDiagnostic> Violations, IReadOnlyList<string> Ledger) Mutate(
        string projectRelativeDirectory,
        string fileRelativeToProject,
        Func<string, string> mutate)
    {
        var root = FindRepositoryRoot();
        var sourceProject = Path.Combine(root, projectRelativeDirectory);
        var scratchRoot = Path.Combine(Path.GetTempPath(), $"issue131-mutation-{Guid.NewGuid():N}");
        var scratchProject = Path.Combine(scratchRoot, projectRelativeDirectory);

        Directory.CreateDirectory(scratchProject);
        CopyTree(sourceProject, scratchProject);

        try
        {
            var target = Path.Combine(scratchProject, fileRelativeToProject);
            var original = File.ReadAllText(target);
            var mutated = mutate(original);
            Assert.NotEqual(original, mutated);
            File.WriteAllText(target, mutated);

            var project = TenantIdentityProjectInventory.Load(
                scratchRoot,
                Path.Combine(scratchProject, Path.GetFileName(sourceProject) + ".csproj"));
            Assert.True(project.ReferenceError is null, $"mutation copy lost build inputs: {project.ReferenceError}");

            var analyzer = new TenantIdentityAnalyzer();
            analyzer.AnalyzeProject(project);
            var analysis = analyzer.Analysis;

            var onMutatedFile = analysis.Diagnostics
                .Where(diagnostic => diagnostic.Rule == TenantIdentityRule.TI900
                                     && diagnostic.RelativePath.EndsWith(fileRelativeToProject, StringComparison.Ordinal))
                .ToArray();
            Assert.True(
                onMutatedFile.Length == 0,
                "the mutation must stay compilable so the guard, not the compiler, catches it: "
                + string.Join(Environment.NewLine, onMutatedFile.Select(diagnostic => diagnostic.ToString())));

            return ([.. analysis.Violations], [.. TenantIdentityAuthoritativeLedger.Evaluate(analysis)
                .Where(violation => violation.Contains(fileRelativeToProject, StringComparison.OrdinalIgnoreCase))]);
        }
        finally
        {
            Directory.Delete(scratchRoot, recursive: true);
        }
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static bool IsAuthoredSource(string file)
    {
        var normalized = file.Replace('\\', '/').ToLowerInvariant();
        return !normalized.Contains("/bin/") && !normalized.Contains("/obj/") && !normalized.Contains("/99_recyclebin/");
    }

    private static string Report(IEnumerable<TenantIdentityDiagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic => "  " + diagnostic));

    internal static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CrestCreates.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
