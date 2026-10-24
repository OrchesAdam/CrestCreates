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
            .Sum(project => TenantIdentityProjectInventory.Load(root, project).Sources.Count);
        Assert.Equal(expectedFiles, analysis.FilesEnumerated);

        // Independent lower bound: the authored files that live inside project directories. Linked sources and
        // Compile Remove entries make the analysed set differ from it, never fall below it.
        var insideProjectDirectories = projectFiles
            .Select(project => Path.GetDirectoryName(project)!)
            .Distinct(StringComparer.Ordinal)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Count(IsAuthoredSource);
        Assert.True(
            analysis.FilesEnumerated >= insideProjectDirectories,
            $"analysed files ({analysis.FilesEnumerated}) fell below the in-directory authored set ({insideProjectDirectories})");

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
    public void TI_C01f_LinkedCompileInputsOutsideTheProjectDirectory_AreScanned()
    {
        var root = FindRepositoryRoot();
        var project = TenantIdentityProjectInventory.Load(
            root,
            Path.Combine(root, "src/Tooling/CrestCreates.CodeGenerator/CrestCreates.CodeGenerator.csproj"));
        var scanned = project.Sources.Select(source => source.RelativePath).ToHashSet(StringComparer.Ordinal);

        // The shared generation sources have no project of their own: they only enter a build through an
        // explicit <Compile Include> that points outside the project directory.
        var linked = new[] { "src/Tooling/CrestCreates.CrudGeneration", "src/Tooling/CrestCreates.HttpProjectionGeneration" }
            .SelectMany(directory => Directory.EnumerateFiles(Path.Combine(root, directory), "*.cs"))
            .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .ToArray();

        Assert.NotEmpty(linked);
        foreach (var file in linked)
        {
            Assert.Contains(file, scanned);
        }

        var jsonContracts = TenantIdentityProjectInventory.Load(
            root,
            Path.Combine(root, "src/Tooling/CrestCreates.JsonContracts.BuildTasks.Core/CrestCreates.JsonContracts.BuildTasks.Core.csproj"));
        Assert.Contains(
            jsonContracts.Sources,
            source => source.RelativePath.StartsWith("src/Tooling/CrestCreates.CrudGeneration/", StringComparison.Ordinal));
    }

    [Fact]
    public void TI_C01g_ExplicitCompileRemoveEntries_AreNotScanned()
    {
        var root = FindRepositoryRoot();
        var project = TenantIdentityProjectInventory.Load(
            root,
            Path.Combine(root, "src/Framework/Infrastructure/CrestCreates.Authorization.Abstractions/CrestCreates.Authorization.Abstractions.csproj"));

        Assert.DoesNotContain(
            project.Sources,
            source => source.RelativePath.EndsWith("Authorizations.Abstractions/IPermissionStore.cs".Replace("Authorizations", "Authorization"), StringComparison.Ordinal));
        Assert.DoesNotContain(
            project.Sources,
            source => source.RelativePath.EndsWith("/IPermissionStore.cs", StringComparison.Ordinal));
        Assert.Contains(project.Sources, source => source.RelativePath.EndsWith("/IPermissionReader.cs", StringComparison.Ordinal)
                                                  || source.RelativePath.Contains("Permission", StringComparison.Ordinal));
    }

    [Fact]
    public void TI_C01h_ActiveConditionalBranches_AreParsedWithProjectConstants()
    {
        var root = FindRepositoryRoot();
        var project = TenantIdentityProjectInventory.Load(
            root,
            Path.Combine(root, "src/Framework/Infrastructure/CrestCreates.Caching/CrestCreates.Caching.csproj"));
        Assert.Contains("DEBUG", project.DefineConstants.Split(';'), StringComparer.OrdinalIgnoreCase);

        const string conditional = """
            using System.Threading.Tasks;
            using CrestCreates.Domain.Permission;
            using CrestCreates.MultiTenancy.Abstract;

            namespace CrestCreates.Samples.Conditional;

            public sealed class DebugOnlySwitch
            {
                public async Task SwitchAsync(ICurrentTenant currentTenant, Tenant directory)
                {
            #if DEBUG
                    await currentTenant.ChangeAsync(directory.Name);
            #endif
                }
            }
            """;

        var violations = TenantIdentitySyntheticFixture.Violations(("Conditional.cs", conditional));
        Assert.Contains(violations, violation => violation.Rule == TenantIdentityRule.TI001);

        const string releaseOnly = """
            using System.Threading.Tasks;
            using CrestCreates.Domain.Permission;
            using CrestCreates.MultiTenancy.Abstract;

            namespace CrestCreates.Samples.Conditional;

            public sealed class ReleaseOnlySwitch
            {
                public async Task SwitchAsync(ICurrentTenant currentTenant, Tenant directory)
                {
            #if RELEASE
                    await currentTenant.ChangeAsync(directory.Name);
            #endif
                }
            }
            """;

        Assert.Empty(TenantIdentitySyntheticFixture.Violations(("Release.cs", releaseOnly)));
    }

    [Fact]
    public void TI_C01i_DerivedCompileInputs_MatchEvaluatedMsBuild()
    {
        var root = FindRepositoryRoot();
        var samples = new[]
        {
            "src/Tooling/CrestCreates.CodeGenerator/CrestCreates.CodeGenerator.csproj",
            "src/Framework/Infrastructure/CrestCreates.Caching/CrestCreates.Caching.csproj",
            "src/Framework/Api/CrestCreates.OpenApi/CrestCreates.OpenApi.csproj",
        };

        foreach (var relative in samples)
        {
            var projectFile = Path.Combine(root, relative);
            var evaluated = EvaluateWithMsBuild(projectFile);
            var derived = TenantIdentityProjectInventory.Load(root, projectFile);

            Assert.Equal(
                evaluated.DefineConstants.Order(StringComparer.OrdinalIgnoreCase),
                derived.DefineConstants.Split(';', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.OrdinalIgnoreCase));

            var evaluatedFiles = evaluated.CompileFiles
                .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
                .Order(StringComparer.Ordinal);
            var derivedFiles = derived.Sources
                .Select(source => source.RelativePath)
                .Order(StringComparer.Ordinal);
            Assert.Equal(evaluatedFiles, derivedFiles);
        }
    }

    private static (IReadOnlyList<string> CompileFiles, IReadOnlyList<string> DefineConstants) EvaluateWithMsBuild(string projectFile)
    {
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(projectFile)!,
        };
        start.ArgumentList.Add("msbuild");
        start.ArgumentList.Add(projectFile);
        start.ArgumentList.Add("-nologo");
        start.ArgumentList.Add("-p:Configuration=Debug");

        // The SDK adds the framework *_OR_GREATER constants in a target, so the evaluated value only
        // appears after it runs; without this target the comparison would ignore the real symbol set.
        start.ArgumentList.Add("-t:AddImplicitDefineConstants");
        start.ArgumentList.Add("-getItem:Compile");
        start.ArgumentList.Add("-getProperty:DefineConstants");

        using var process = System.Diagnostics.Process.Start(start)
            ?? throw new InvalidOperationException("could not start dotnet msbuild");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"dotnet msbuild failed for {projectFile}: {stderr}");

        using var document = System.Text.Json.JsonDocument.Parse(stdout);
        var root = document.RootElement;
        var defines = root.GetProperty("Properties").GetProperty("DefineConstants").GetString() ?? string.Empty;
        var files = root.GetProperty("Items").GetProperty("Compile")
            .EnumerateArray()
            .Select(item => item.GetProperty("FullPath").GetString()!)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return (files, defines.Split(';', StringSplitOptions.RemoveEmptyEntries));
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
            Configuration: "Debug",
            DefineConstants: "TRACE;DEBUG",
            Sources: [new TenantSourceFile("synthetic/a.cs", "synthetic/a.cs", "namespace CrestCreates.X { public interface ICurrentTenant { System.Threading.Tasks.Task SwitchAsync(string tenantId); } }")],
            GeneratedSources: [],
            References: [],
            ReferenceError: "no build output",
            EvaluationError: null);

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

    [Fact]
    public void TI_N04_UndecidableValueSurvivesLocalAlias()
    {
        const string sample = """
            using CrestCreates.MultiTenancy;
            using CrestCreates.MultiTenancy.Abstract;

            namespace CrestCreates.Samples.Undecidable;

            public sealed class UnknownSource
            {
                private readonly ICurrentTenant _currentTenant = null!;
                private readonly TenantIdentifierNormalizer _normalizer = null!;

                public void DirectControl(string raw) =>
                    _currentTenant.SetTenantId(_normalizer.NormalizeName(raw));

                public void ThroughLocal(string raw)
                {
                    var key = _normalizer.NormalizeName(raw);
                    _currentTenant.SetTenantId(key);
                }

                public void ThroughReassignment(string raw)
                {
                    string key = "seed";
                    key = _normalizer.NormalizeName(raw);
                    _currentTenant.SetTenantId(key);
                }
            }
            """;

        var coverage = TenantIdentitySyntheticFixture.Analyze(("Undecidable.cs", sample)).CoverageFailures;
        // An unknown external helper may carry a tenant name: the flag must survive locals and re-assignment
        // instead of silently becoming clean.
        Assert.Equal(3, coverage.Count(diagnostic => diagnostic.Rule == TenantIdentityRule.TI900));
    }

    [Fact]
    public void TI_N05_HelperLocalAssignment_PropagatesName()
    {
        const string sample = """
            using CrestCreates.Domain.Permission;
            using CrestCreates.MultiTenancy.Abstract;

            namespace CrestCreates.Samples.HelperAssignment;

            public sealed class HelperIndirection
            {
                private readonly ICurrentTenant _currentTenant = null!;

                public void ViaHelperParameter(ITenantInfo tenant) => _currentTenant.SetTenantId(Hide(tenant));

                public void ViaDirectoryEntity(Tenant directory) => _currentTenant.SetTenantId(HideDirectory(directory));

                public void CorrectHelper(Tenant directory) => _currentTenant.SetTenantId(IdOf(directory));

                private static string Hide(ITenantInfo tenant)
                {
                    string key = string.Empty;
                    key = tenant.Name;
                    return key;
                }

                private static string HideDirectory(Tenant directory)
                {
                    string key = string.Empty;
                    key = directory.DisplayName;
                    return key;
                }

                private static string IdOf(Tenant directory)
                {
                    var found = directory;
                    return found.Id.ToString();
                }
            }
            """;

        var violations = TenantIdentitySyntheticFixture.Violations(("Helper.cs", sample));
        Assert.Equal(2, violations.Count);
        Assert.All(violations, violation => Assert.Equal(TenantIdentityRule.TI001, violation.Rule));
        Assert.All(violations, violation => Assert.Contains("SetTenantId", violation.Exit, StringComparison.Ordinal));
    }

    [Fact]
    public void TI_N06_ReorderedNamedArguments_CannotBypassIdentitySlots()
    {
        const string sample = """
            using System.Security.Claims;
            using CrestCreates.Domain.Permission;
            using CrestCreates.MultiTenancy.Abstract;

            namespace CrestCreates.Samples.NamedArguments;

            public sealed class ReorderedArguments
            {
                private readonly ICurrentTenant _currentTenant = null!;

                public Claim ClaimReordered(Tenant directory) =>
                    new Claim(value: directory.Name, type: "tenant_id");

                public ITenantInfo TenantInfoReordered(Tenant directory) =>
                    new TenantInfo(name: directory.Id.ToString(), id: directory.Name);

                public void ChangeReordered(Tenant directory) => _currentTenant.Change(ClaimReordered(directory));
            }
            """;

        var violations = TenantIdentitySyntheticFixture.Violations(("Reordered.cs", sample));
        Assert.Contains(violations, violation => violation.Rule == TenantIdentityRule.TI001
                                                && violation.Exit.Contains("Claim", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Rule == TenantIdentityRule.TI003
                                                && violation.Exit.Contains("constructor id slot", StringComparison.Ordinal));
    }

    [Fact]
    public void TI_N07_TenantInfoIdWriteThroughObjectInitializer_IsReported()
    {
        const string sample = """
            using CrestCreates.Domain.Permission;
            using CrestCreates.MultiTenancy.Abstract;

            namespace CrestCreates.Samples.InfoIdWrite;

            public sealed class IdWrite
            {
                private readonly ICurrentTenant _currentTenant = null!;

                public void InitializeFromName(Tenant directory)
                {
                    var frame = new TenantInfo { Id = directory.Name, Name = "display" };
                    _currentTenant.Change(frame);
                }

                public void AssignFromName(Tenant directory, TenantInfo frame) => frame.Id = directory.DisplayName;

                public void Correct(Tenant directory, TenantInfo frame) => frame.Id = directory.Id.ToString();
            }
            """;

        var violations = TenantIdentitySyntheticFixture.Violations(("InfoId.cs", sample));
        Assert.Equal(2, violations.Count);
        Assert.All(violations, violation => Assert.Equal(TenantIdentityRule.TI003, violation.Rule));
    }

    [Fact]
    public void TI_C01j_ConditionalCompileRemove_IsHonouredByConfiguration()
    {
        // A Release-only Remove must not hide a file that the Debug build actually compiles.
        var root = Path.Combine(Path.GetTempPath(), $"issue131-conditional-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(
                Path.Combine(root, "Sample.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Remove="ConditionalTenantSwitch.cs" Condition="'$(Configuration)' == 'Release'" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "Always.cs"), "namespace Sample { public sealed class Always { } }");
            File.WriteAllText(
                Path.Combine(root, "ConditionalTenantSwitch.cs"),
                "namespace Sample { public sealed class ConditionalTenantSwitch { } }");

            var projectFile = Path.Combine(root, "Sample.csproj");
            var debug = TenantIdentityProjectInventory.Load(root, projectFile, configurationOverride: "Debug");
            var release = TenantIdentityProjectInventory.Load(root, projectFile, configurationOverride: "Release");

            Assert.Contains(debug.Sources, source => source.RelativePath.EndsWith("ConditionalTenantSwitch.cs", StringComparison.Ordinal));
            Assert.DoesNotContain(release.Sources, source => source.RelativePath.EndsWith("ConditionalTenantSwitch.cs", StringComparison.Ordinal));
            Assert.Contains("DEBUG", debug.DefineConstants.Split(';'), StringComparer.OrdinalIgnoreCase);
            Assert.Contains("RELEASE", release.DefineConstants.Split(';'), StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TI_N08_NonEmptyConstantFallback_KeepsItsOwnProvenance()
    {
        const string sample = """
            using System.Security.Claims;
            using CrestCreates.Infrastructure.Authorization;

            namespace CrestCreates.Samples.Fallback;

            public sealed class ClaimFallbacks
            {
                public Claim WithNameFallback(IdentityClaimsContext context) =>
                    new("tenantid", context.TenantId ?? "acme-east");

                public Claim WithEmptyFallback(IdentityClaimsContext context) =>
                    new("tenantid", context.TenantId ?? string.Empty);

                public Claim WithNullFallback(IdentityClaimsContext context) =>
                    new("tenantid", context.TenantId ?? null);
            }
            """;

        var analysis = TenantIdentitySyntheticFixture.Analyze(("Fallback.cs", sample));
        var claims = analysis.Exits.Where(exit => exit.PortLabel.Contains("Claim", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, claims.Length);

        var withName = claims.Single(claim => claim.ArgumentText.Contains("acme-east", StringComparison.Ordinal));
        Assert.Contains(withName.Origins, origin => origin.StartsWith("literal:", StringComparison.Ordinal));

        foreach (var neutral in claims.Where(claim => !claim.ArgumentText.Contains("acme-east", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain(neutral.Origins, origin => origin.StartsWith("literal:", StringComparison.Ordinal));
            Assert.Contains(neutral.Origins, origin => origin.EndsWith("IdentityClaimsContext.TenantId", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TI_N09_PassthroughHelper_KeepsTheCallerProvenance()
    {
        const string sample = """
            using System.Security.Claims;
            using CrestCreates.Infrastructure.Authorization;

            namespace CrestCreates.Samples.Passthrough;

            public sealed class PassthroughClaims
            {
                public Claim Direct(IdentityClaimsContext context) => new("tenantid", context.TenantId);

                public Claim ThroughLocal(IdentityClaimsContext context)
                {
                    var tenantId = context.TenantId;
                    return new Claim("tenantid", tenantId);
                }

                public Claim ThroughHelper(IdentityClaimsContext context) => new("tenantid", Identity(context.TenantId));

                private static string? Identity(string? value) => value;
            }
            """;

        var analysis = TenantIdentitySyntheticFixture.Analyze(("Passthrough.cs", sample));
        var claims = analysis.Exits.Where(exit => exit.PortLabel.Contains("Claim", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, claims.Length);
        Assert.All(claims, claim => Assert.Equal(["CrestCreates.Infrastructure.Authorization.IdentityClaimsContext.TenantId"], claim.Origins));
    }

    [Fact]
    public void TI_C01k_WarmCache_TracksLinkedGlobChanges()
    {
        // The cache key must cover the search roots of Compile Include entries that point outside the project
        // directory; otherwise a warm read keeps an obsolete Compile list after a linked file is added.
        var root = Path.Combine(Path.GetTempPath(), $"issue131-warm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "Owned"));
        Directory.CreateDirectory(Path.Combine(root, "Shared"));
        try
        {
            var projectFile = Path.Combine(root, "Owned", "Sample.csproj");
            File.WriteAllText(
                projectFile,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="../Shared/*.cs" Link="Shared/%(Filename)%(Extension)" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "Owned", "Owned.cs"), "namespace Sample { public sealed class Owned { } }");
            File.WriteAllText(Path.Combine(root, "Shared", "Initial.cs"), "namespace Sample { public sealed class Initial { } }");

            var first = TenantIdentityProjectInventory.Load(root, projectFile);
            Assert.Contains(first.Sources, source => source.RelativePath.EndsWith("Shared/Initial.cs", StringComparison.Ordinal));
            Assert.DoesNotContain(first.Sources, source => source.RelativePath.EndsWith("NewLinked.cs", StringComparison.Ordinal));

            // Add a new file to the linked root without touching the project or its directory.
            File.WriteAllText(
                Path.Combine(root, "Shared", "NewLinked.cs"),
                """
                using System.Threading.Tasks;
                using CrestCreates.MultiTenancy.Abstract;

                namespace Sample;

                public sealed class NewLinked
                {
                    public Task Run(ICurrentTenant currentTenant, string tenantName) => currentTenant.SetTenantId(tenantName);
                }
                """);

            var warm = TenantIdentityProjectInventory.Load(root, projectFile);
            Assert.Contains(warm.Sources, source => source.RelativePath.EndsWith("NewLinked.cs", StringComparison.Ordinal));

            // Deleting the linked file must not leave a stale entry behind either.
            File.Delete(Path.Combine(root, "Shared", "Initial.cs"));
            var afterDelete = TenantIdentityProjectInventory.Load(root, projectFile);
            Assert.DoesNotContain(afterDelete.Sources, source => source.RelativePath.EndsWith("Shared/Initial.cs", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TI_C01m_PropertyExpandedAndImportedGlobs_StayFresh()
    {
        // Three equivalent ways to declare the same linked glob. Literal items and literal imports are cached but
        // their key covers the search root; a property-expanded item cannot be proven and must bypass the cache.
        foreach (var form in new[] { "literal", "import", "property", "import-property" })
        {
            var root = Path.Combine(Path.GetTempPath(), $"issue131-evaluated-{form}-{Guid.NewGuid():N}");
            var projectDirectory = Path.Combine(root, "Owned");
            Directory.CreateDirectory(projectDirectory);
            Directory.CreateDirectory(Path.Combine(root, "Shared"));
            try
            {
                var projectFile = Path.Combine(projectDirectory, "Sample.csproj");
                var item = form switch
                {
                    "literal" => "<Compile Include=\"../Shared/*.cs\" Link=\"Shared/%(Filename)%(Extension)\" />",
                    "property" => "<Compile Include=\"$(ReviewShared)/*.cs\" Link=\"Shared/%(Filename)%(Extension)\" />",
                    "import" => string.Empty,
                    _ => string.Empty,
                };

                if (form is "import" or "import-property")
                {
                    var propsBody = form == "import"
                        ? "<Compile Include=\"../Shared/*.cs\" Link=\"Shared/%(Filename)%(Extension)\" />"
                        : "<Compile Include=\"$(ReviewShared)/*.cs\" />";
                    File.WriteAllText(
                        Path.Combine(projectDirectory, "SharedInputs.props"),
                        $"<Project><PropertyGroup><ReviewShared>../Shared</ReviewShared></PropertyGroup><ItemGroup>{propsBody}</ItemGroup></Project>");
                    item = "<Import Project=\"SharedInputs.props\" />";
                }

                var items = form is "import" or "import-property"
                    ? string.Empty
                    : $"<ItemGroup>{item}</ItemGroup>";
                File.WriteAllText(
                    projectFile,
                    $"""
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup>
                        <TargetFramework>net10.0</TargetFramework>
                        <Nullable>enable</Nullable>
                        <ReviewShared>../Shared</ReviewShared>
                      </PropertyGroup>
                      {items}
                      {(form is "import" or "import-property" ? item : string.Empty)}
                    </Project>
                    """);
                File.WriteAllText(Path.Combine(projectDirectory, "Owned.cs"), "namespace Sample { public sealed class Owned { } }");

                var first = TenantIdentityEvaluatedInputs.Read(root, projectFile, "Debug");
                Assert.True(first.IsAvailable, $"{form}: first evaluation failed: {first.UnavailableReason}");
                Assert.DoesNotContain(first.CompileFiles, file => file.EndsWith("NewLinked.cs", StringComparison.Ordinal));

                // Warm read with no change: literal forms are served from cache, property forms re-evaluate.
                var warm = TenantIdentityEvaluatedInputs.Read(root, projectFile, "Debug");
                Assert.True(warm.IsAvailable, $"{form}: warm evaluation failed: {warm.UnavailableReason}");
                var cacheable = form is "literal" or "import";
                Assert.Equal(cacheable ? "msbuild-cache" : "msbuild", warm.Source);

                File.WriteAllText(
                    Path.Combine(root, "Shared", "NewLinked.cs"),
                    "namespace Sample { public sealed class NewLinked { } }");

                var afterAdd = TenantIdentityEvaluatedInputs.Read(root, projectFile, "Debug");
                Assert.Contains(afterAdd.CompileFiles, file => file.EndsWith("NewLinked.cs", StringComparison.Ordinal));

                File.Move(
                    Path.Combine(root, "Shared", "NewLinked.cs"),
                    Path.Combine(root, "Shared", "RenamedLinked.cs"));
                var afterRename = TenantIdentityEvaluatedInputs.Read(root, projectFile, "Debug");
                Assert.Contains(afterRename.CompileFiles, file => file.EndsWith("RenamedLinked.cs", StringComparison.Ordinal));
                Assert.DoesNotContain(afterRename.CompileFiles, file => file.EndsWith("NewLinked.cs", StringComparison.Ordinal));

                File.Delete(Path.Combine(root, "Shared", "RenamedLinked.cs"));
                var afterDelete = TenantIdentityEvaluatedInputs.Read(root, projectFile, "Debug");
                Assert.DoesNotContain(afterDelete.CompileFiles, file => file.EndsWith("RenamedLinked.cs", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void TI_C01l_FailedEvaluation_IsACoverageFailureEvenWithoutSources()
    {
        // A valid project with build output and authored sources, but MSBuild evaluation fails: "unknown inputs"
        // must never be treated as "no sensitive inputs".
        var root = Path.Combine(Path.GetTempPath(), $"issue131-evalfail-{Guid.NewGuid():N}");
        var projectDirectory = Path.Combine(root, "Sample");
        var binDirectory = Path.Combine(projectDirectory, "bin", "Debug", "net10.0");
        Directory.CreateDirectory(binDirectory);
        try
        {
            var projectFile = Path.Combine(projectDirectory, "Sample.csproj");
            File.WriteAllText(
                projectFile,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                  <Target Name="SimulateBrokenEvaluation" BeforeTargets="AddImplicitDefineConstants">
                    <Error Text="simulated evaluation failure" />
                  </Target>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(projectDirectory, "Canary.cs"),
                """
                using System.Threading.Tasks;
                using CrestCreates.MultiTenancy.Abstract;

                namespace Sample;

                public sealed class Canary
                {
                    public Task Run(ICurrentTenant currentTenant, string tenantName) => currentTenant.SetTenantId(tenantName);
                }
                """);
            File.Copy(typeof(object).Assembly.Location, Path.Combine(binDirectory, "Sample.dll"), overwrite: true);

            var project = TenantIdentityProjectInventory.Load(root, projectFile);
            Assert.True(project.EvaluationError is not null);
            Assert.Contains("could not evaluate", project.EvaluationError, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(project.Sources);

            var analyzer = new TenantIdentityAnalyzer();
            analyzer.AnalyzeProject(project);

            Assert.Contains(analyzer.Analysis.CoverageFailures, diagnostic =>
                diagnostic.Rule == TenantIdentityRule.TI900
                && diagnostic.Hint.Contains("could not evaluate", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TI_N10_CrossFileConstantFallback_KeepsItsValueProvenance()
    {
        const string constants = """
            namespace CrestCreates.Samples.Constants;

            internal static class TenantClaimDefaults
            {
                internal const string Fallback = "acme-east";
                internal const string Empty = "";
                internal static readonly string Mutable = "acme-west";
            }
            """;

        const string builder = """
            using System.Security.Claims;
            using CrestCreates.Infrastructure.Authorization;

            namespace CrestCreates.Samples.Constants;

            public sealed class ConstantClaims
            {
                public Claim NameConst(IdentityClaimsContext context) => new("tenantid", context.TenantId ?? TenantClaimDefaults.Fallback);

                public Claim EmptyConst(IdentityClaimsContext context) => new("tenantid", context.TenantId ?? TenantClaimDefaults.Empty);

                public Claim StaticReadOnly(IdentityClaimsContext context) => new("tenantid", context.TenantId ?? TenantClaimDefaults.Mutable);
            }
            """;

        var analysis = TenantIdentitySyntheticFixture.Analyze(("Constants.cs", constants), ("Builder.cs", builder));
        var claims = analysis.Exits.Where(exit => exit.PortLabel.Contains("Claim", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, claims.Length);

        var nameConst = claims.Single(claim => claim.ArgumentText.Contains("Fallback", StringComparison.Ordinal));
        Assert.Contains(nameConst.Origins, origin => origin.StartsWith("literal:", StringComparison.Ordinal));

        var emptyConst = claims.Single(claim => claim.ArgumentText.Contains("Empty", StringComparison.Ordinal));
        Assert.DoesNotContain(emptyConst.Origins, origin => origin.StartsWith("literal:", StringComparison.Ordinal));
        Assert.Contains(emptyConst.Origins, origin => origin.EndsWith("IdentityClaimsContext.TenantId", StringComparison.Ordinal));
    }

    [Fact]
    public void TI_N11_ThrowFallback_ProducesNoIdentitySource()
    {
        const string sample = """
            using System;
            using System.Security.Claims;
            using CrestCreates.Infrastructure.Authorization;

            namespace CrestCreates.Samples.ThrowFallback;

            public sealed class ThrowClaims
            {
                public Claim WithMessage(IdentityClaimsContext context) =>
                    new("tenantid", context.TenantId ?? throw new InvalidOperationException("missing tenant"));

                public Claim WithInterpolatedMessage(IdentityClaimsContext context) =>
                    new("tenantid", context.TenantId ?? throw new InvalidOperationException($"tenant {context.TenantId} missing"));

                public Claim WithEmptyMessage(IdentityClaimsContext context) =>
                    new("tenantid", context.TenantId ?? throw new InvalidOperationException(string.Empty));
            }
            """;

        var analysis = TenantIdentitySyntheticFixture.Analyze(("Throw.cs", sample));
        var claims = analysis.Exits.Where(exit => exit.PortLabel.Contains("Claim", StringComparison.Ordinal)).ToArray();
        Assert.Equal(3, claims.Length);
        Assert.All(claims, claim =>
        {
            Assert.DoesNotContain(claim.Origins, origin => origin.StartsWith("literal:", StringComparison.Ordinal));
            Assert.Equal(["CrestCreates.Infrastructure.Authorization.IdentityClaimsContext.TenantId"], claim.Origins);
        });
        Assert.Empty(analysis.Diagnostics);
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

    [Fact]
    public void TI_M03_ClaimFedThroughTenantIdAliasOfUserName_IsCaughtByTheLedger()
    {
        // The value is a user name, but the local is spelled tenantId: only the origin set can reject it, and
        // the ledger must see through the alias instead of trusting the identifier.
        var (_, ledger) = Mutate(
            "src/Framework/Infrastructure/CrestCreates.Infrastructure",
            "Authorization/IdentityClaimsBuilder.cs",
            text => text
                .Replace(
                    "        var claims = new List<Claim>",
                    "        var tenantId = context.UserName;\n        var claims = new List<Claim>",
                    StringComparison.Ordinal)
                .Replace("new(\"tenantid\", context.TenantId ?? string.Empty),", "new(\"tenantid\", tenantId ?? string.Empty),", StringComparison.Ordinal)
                .Replace("new(\"tenant_id\", context.TenantId ?? string.Empty),", "new(\"tenant_id\", tenantId ?? string.Empty),", StringComparison.Ordinal));

        Assert.True(
            ledger.Count(violation => violation.Contains("IdentityClaimsBuilder.cs", StringComparison.Ordinal)) >= 2,
            "both tenant claim rows must reject a value that came from UserName: "
            + string.Join(Environment.NewLine, ledger));
    }

    [Fact]
    public void TI_M04_DebugOnlyIdentityMisuseInRealProject_IsCaught()
    {
        // The build compiles the DEBUG branch, so the guard must analyse it. A file that only exists behind
        // #if DEBUG previously stayed invisible because the parse options carried no preprocessor symbols.
        var (_, cachingAnalysis) = AnalyzeScratch(
            "src/Framework/Infrastructure/CrestCreates.Caching",
            (scratchRoot, scratchProject) =>
            {
                File.WriteAllText(
                    Path.Combine(scratchProject, "DebugOnlyTenantSwitch.cs"),
                    """
                    using System.Threading.Tasks;
                    using CrestCreates.Domain.Permission;
                    using CrestCreates.MultiTenancy.Abstract;

                    namespace CrestCreates.Caching;

                    public sealed class DebugOnlyTenantSwitch
                    {
                        public async Task SwitchAsync(ICurrentTenant currentTenant, Tenant directory)
                        {
                    #if DEBUG
                            await currentTenant.ChangeAsync(directory.Name);
                    #endif
                        }
                    }
                    """);
            });

        Assert.Contains(cachingAnalysis.Violations, violation => violation.Rule == TenantIdentityRule.TI001
                                                               && violation.Exit.Contains("ChangeAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void TI_M05_IdentityMisuseInSourceLinkedInput_IsCaught()
    {
        // CrudGeneration has no project of its own: it only enters a build through a <Compile Include> that
        // points outside the project directory.
        var (codeGenerator, linkedAnalysis) = AnalyzeScratch(
            "src/Tooling/CrestCreates.CodeGenerator",
            (scratchRoot, _) =>
            {
                var linked = Path.Combine(scratchRoot, "src/Tooling/CrestCreates.CrudGeneration");
                Directory.CreateDirectory(linked);
                File.WriteAllText(
                    Path.Combine(linked, "LinkedTenantSwitch.cs"),
                    """
                    using System.Threading.Tasks;
                    using CrestCreates.Domain.Permission;
                    using CrestCreates.MultiTenancy.Abstract;

                    namespace CrestCreates.CrudGeneration;

                    public sealed class LinkedTenantSwitch
                    {
                        public async Task SwitchAsync(ICurrentTenant currentTenant, Tenant directory) =>
                            await currentTenant.ChangeAsync(directory.Name);
                    }
                    """);
            });

        // The tooling project does not reference the tenant contracts, so the sensitive call cannot bind there:
        // what matters is that the linked file is part of the analysed Compile set and produces a diagnostic
        // for the guard (TI001 when it binds, TI900 when it cannot) instead of being invisible.
        Assert.Contains(
            codeGenerator.Sources,
            source => source.RelativePath.EndsWith("CrudGeneration/LinkedTenantSwitch.cs", StringComparison.Ordinal));
        Assert.Contains(
            linkedAnalysis.Diagnostics,
            diagnostic => diagnostic.RelativePath.EndsWith("LinkedTenantSwitch.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void TI_M08_ConditionalRemoveInRealProject_StillScansTheDebugFile()
    {
        // The reviewer's control: a Release-only Remove plus a compiled identity misuse under Debug. Reading the
        // evaluated Compile set keeps the file visible; a hand-rolled item reader would have dropped it.
        var (_, fileManagementAnalysis) = AnalyzeScratch(
            "src/Framework/Modules/CrestCreates.FileManagement",
            (_, scratchProject) =>
            {
                var projectFile = Directory.EnumerateFiles(scratchProject, "*.csproj").Single();
                var text = File.ReadAllText(projectFile);
                File.WriteAllText(
                    projectFile,
                    text.Replace(
                        "</Project>",
                        "  <ItemGroup>\n"
                        + "    <Compile Remove=\"ConditionalTenantSwitch.cs\" Condition=\"'$(Configuration)' == 'Release'\" />\n"
                        + "  </ItemGroup>\n</Project>",
                        StringComparison.Ordinal));

                File.WriteAllText(
                    Path.Combine(scratchProject, "ConditionalTenantSwitch.cs"),
                    """
                    using System.Threading.Tasks;
                    using CrestCreates.Domain.Permission;
                    using CrestCreates.MultiTenancy.Abstract;

                    namespace CrestCreates.FileManagement;

                    public sealed class ConditionalTenantSwitch
                    {
                        public async Task SwitchAsync(ICurrentTenant currentTenant, Tenant directory) =>
                            await currentTenant.ChangeAsync(directory.Name);
                    }
                    """);
            });

        Assert.Contains(fileManagementAnalysis.Violations, violation =>
            violation.Rule == TenantIdentityRule.TI001
            && violation.RelativePath.EndsWith("ConditionalTenantSwitch.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void TI_M09_CrossFileConstFallbackInRealClaimsBuilder_IsRejected()
    {
        var (_, analysis) = AnalyzeScratch(
            "src/Framework/Infrastructure/CrestCreates.Infrastructure",
            (_, scratchProject) =>
            {
                Directory.CreateDirectory(Path.Combine(scratchProject, "Authorization"));
                File.WriteAllText(
                    Path.Combine(scratchProject, "Authorization/ReviewFallback.cs"),
                    """
                    namespace CrestCreates.Infrastructure.Authorization;

                    internal static class ReviewFallback
                    {
                        internal const string Fallback = "acme-east";
                    }
                    """);

                var builder = Path.Combine(scratchProject, "Authorization/IdentityClaimsBuilder.cs");
                var text = File.ReadAllText(builder);
                File.WriteAllText(
                    builder,
                    text.Replace("context.TenantId ?? string.Empty", "context.TenantId ?? ReviewFallback.Fallback", StringComparison.Ordinal));
            });

        var ledger = TenantIdentityAuthoritativeLedger.Evaluate(analysis)
            .Where(violation => violation.Contains("IdentityClaimsBuilder.cs", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            ledger.Length >= 2,
            "a non-empty constant declared in another file is still a non-authoritative source: "
            + string.Join(Environment.NewLine, ledger));
    }

    [Fact]
    public void TI_M10_ThrowFallbackInRealClaimsBuilder_StaysClean()
    {
        var (_, analysis) = AnalyzeScratch(
            "src/Framework/Infrastructure/CrestCreates.Infrastructure",
            (_, scratchProject) =>
            {
                var builder = Path.Combine(scratchProject, "Authorization/IdentityClaimsBuilder.cs");
                var text = File.ReadAllText(builder);
                File.WriteAllText(
                    builder,
                    text.Replace(
                        "context.TenantId ?? string.Empty",
                        "context.TenantId ?? throw new InvalidOperationException(\"missing tenant\")",
                        StringComparison.Ordinal));
            });

        // Only the claims rows belong to this scratch project; the remaining locked rows legitimately report
        // as missing because their projects are not part of the copy.
        var claimRows = TenantIdentityAuthoritativeLedger.Evaluate(analysis)
            .Where(violation => violation.Contains("IdentityClaimsBuilder.cs", StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(claimRows);
        Assert.Contains(
            analysis.Exits,
            exit => exit.RelativePath.EndsWith("IdentityClaimsBuilder.cs", StringComparison.Ordinal)
                    && exit.PortLabel.Contains("Claim", StringComparison.Ordinal));
        Assert.True(
            !analysis.Violations.Any(),
            "a fail-closed throw branch is not an identity source:"
            + Environment.NewLine + string.Join(Environment.NewLine, analysis.Violations.Select(violation => violation.ToString())));
    }

    /// <summary>
    /// Copies one project (with its build outputs) into a scratch tree, lets the caller arrange extra inputs,
    /// and runs the guard against the copy. Nothing is written back into the repository.
    /// </summary>
    private static (TenantProjectInput Input, TenantIdentityAnalysis Analysis) AnalyzeScratch(
        string projectRelativeDirectory,
        Action<string, string> arrange)
    {
        var root = FindRepositoryRoot();
        var sourceProject = Path.Combine(root, projectRelativeDirectory);
        var scratchRoot = Path.Combine(Path.GetTempPath(), $"issue131-scratch-{Guid.NewGuid():N}");
        var scratchProject = Path.Combine(scratchRoot, projectRelativeDirectory);

        Directory.CreateDirectory(scratchProject);
        CopyTree(sourceProject, scratchProject);

        // The scratch tree has to evaluate like the repository, otherwise MSBuild would see a project without
        // the shared TargetFramework/nullable/analyzer properties.
        foreach (var shared in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Build.Aot.props", "Directory.Packages.props" })
        {
            var path = Path.Combine(root, shared);
            if (File.Exists(path))
            {
                File.Copy(path, Path.Combine(scratchRoot, shared));
            }
        }

        try
        {
            arrange(scratchRoot, scratchProject);

            var project = TenantIdentityProjectInventory.Load(
                scratchRoot,
                Path.Combine(scratchProject, Path.GetFileName(sourceProject) + ".csproj"));
            Assert.True(project.ReferenceError is null, $"scratch copy lost build inputs: {project.ReferenceError}");

            var analyzer = new TenantIdentityAnalyzer();
            analyzer.AnalyzeProject(project);
            return (project, analyzer.Analysis);
        }
        finally
        {
            Directory.Delete(scratchRoot, recursive: true);
        }
    }

    [Fact]
    public void TI_M06_NonEmptyClaimFallback_IsRejectedByTheLedger()
    {
        var (_, ledger) = Mutate(
            "src/Framework/Infrastructure/CrestCreates.Infrastructure",
            "Authorization/IdentityClaimsBuilder.cs",
            text => text
                .Replace("new(\"tenantid\", context.TenantId ?? string.Empty),", "new(\"tenantid\", context.TenantId ?? \"acme-east\"),", StringComparison.Ordinal)
                .Replace("new(\"tenant_id\", context.TenantId ?? string.Empty),", "new(\"tenant_id\", context.TenantId ?? \"acme-east\"),", StringComparison.Ordinal));

        Assert.True(
            ledger.Count(violation => violation.Contains("IdentityClaimsBuilder.cs", StringComparison.Ordinal)) >= 2,
            "a non-empty constant fallback must not be erased by the authoritative branch: "
            + string.Join(Environment.NewLine, ledger));
    }

    [Fact]
    public void TI_M07_PassthroughHelperInRealClaimsBuilder_IsNotRejected()
    {
        // A pure passthrough helper is an equivalent rewrite: it must stay green, otherwise the gate blocks
        // legitimate refactoring of the main chain.
        var (violations, ledger) = Mutate(
            "src/Framework/Infrastructure/CrestCreates.Infrastructure",
            "Authorization/IdentityClaimsBuilder.cs",
            text => text
                .Replace(
                    "        var claims = new List<Claim>",
                    "        static string? Identity(string? value) => value;\n\n        var claims = new List<Claim>",
                    StringComparison.Ordinal)
                .Replace("new(\"tenantid\", context.TenantId ?? string.Empty),", "new(\"tenantid\", Identity(context.TenantId) ?? string.Empty),", StringComparison.Ordinal)
                .Replace("new(\"tenant_id\", context.TenantId ?? string.Empty),", "new(\"tenant_id\", Identity(context.TenantId) ?? string.Empty),", StringComparison.Ordinal));

        Assert.True(
            ledger.Count == 0,
            "a passthrough helper must keep the caller's provenance: "
            + string.Join(Environment.NewLine, ledger));
        Assert.True(
            violations.Count == 0,
            "a passthrough helper must not become a violation:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations.Select(violation => violation.ToString())));
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
