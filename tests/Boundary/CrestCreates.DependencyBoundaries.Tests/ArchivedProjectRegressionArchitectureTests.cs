using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

/// <summary>
/// #131 design §5 step 4/5 and #132 cleanup exit: archived projects and retired audit contracts
/// must not re-enter active compilation, distribution, solution membership or module aggregation.
/// Anti-revival guards are guards, not consumers.
/// </summary>
public class ArchivedProjectRegressionArchitectureTests
{
    private const string ArchiveRoot = "99_RecycleBin/issue-131-capability-map";

    private static readonly string[] ArchivedProjects =
    [
        "src/Integrations/CrestCreates.Integration.ExternalApi",
        "src/Integrations/CrestCreates.Integration.LegacyDatabase",
        "src/Runtime/Agent/CrestCreates.Agent.Runtime",
        "src/Framework/Infrastructure/CrestCreates.Configuration",
        "src/Metadata/Draft/CrestCreates.Draft.Abstractions",
        "src/Metadata/Draft/CrestCreates.Draft",
        "src/Runtime/Eventing/CrestCreates.EventBus.EventStore",
        "tests/Metadata/Draft/CrestCreates.Draft.Tests",
    ];

    private static readonly string[] ArchivedNamespaces =
    [
        "CrestCreates.Integration.ExternalApi",
        "CrestCreates.Integration.LegacyDatabase",
        "CrestCreates.Agent.Runtime",
        "CrestCreates.Configuration.Services",
        "CrestCreates.Configuration.Modules",
        "CrestCreates.Draft.Abstractions",
        "CrestCreates.EventBus.EventStore",
    ];

    /// <summary>Only these files may still spell the archived identifiers, and only as guards or history.</summary>
    private static readonly string[] AllowedMentions =
    [
        "tests/Boundary/CrestCreates.DependencyBoundaries.Tests/ArchivedProjectRegressionArchitectureTests.cs",
        "tests/Boundary/CrestCreates.DependencyBoundaries.Tests/CapabilityMapCoverageArchitectureTests.cs",
        "tests/Boundary/CrestCreates.DependencyBoundaries.Tests/DependencyBoundaryTests.cs",
        "tests/Boundary/CrestCreates.DependencyBoundaries.Tests/RuntimePersistenceArchitectureTests.cs",
        "tests/Boundary/CrestCreates.DependencyBoundaries.Tests/TenantIdentity/TenantIdentityProjectInventory.cs",
        "tests/Boundary/CrestCreates.DependencyBoundaries.Tests/TenantIdentity/TenantIdentitySymbols.cs",
        "tests/Boundary/CrestCreates.DependencyBoundaries.Tests/TenantIdentity/TenantIdentityPorts.cs",
        "tests/Boundary/CrestCreates.DependencyBoundaries.Tests/TenantIdentity/TenantIdentityAnalyzer.cs",
        "tests/Boundary/CrestCreates.DependencyBoundaries.Tests/TenantIdentity/TenantIdentityFlow.cs",
        "tests/Boundary/CrestCreates.DependencyBoundaries.Tests/TenantIdentityArchitectureTests.cs",
    ];

    [Fact]
    public void ActiveProjectReferences_DoNotPointAtArchivedProjects()
    {
        var root = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (var project in ActiveProjectFiles(root))
        {
            var projectRelative = Relative(root, project);
            foreach (var reference in XDocument.Load(project).Descendants("ProjectReference")
                         .Select(element => element.Attribute("Include")?.Value)
                         .Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                var resolved = ArchitecturePath.TryResolve(Path.GetDirectoryName(project)!, reference);
                if (resolved is null)
                {
                    continue;
                }

                var target = ArchitecturePath.Relative(root, resolved);
                if (IsArchivedPath(target) || target.Contains("99_recyclebin/", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{projectRelative} -> {target}");
                }
            }
        }

        Assert.True(violations.Count == 0, "active projects still reference archived projects:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void SolutionMembership_HasNoArchivedProjectsOrRecycleBin()
    {
        var root = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (var solution in SolutionFiles(root))
        {
            foreach (var match in System.Text.RegularExpressions.Regex.Matches(
                         File.ReadAllText(solution), @"<(?:Project|File|Folder)\s+Path=""([^""]+)""")
                     .Cast<System.Text.RegularExpressions.Match>())
            {
                var resolved = ArchitecturePath.TryResolve(Path.GetDirectoryName(solution)!, match.Groups[1].Value);
                if (resolved is null)
                {
                    continue;
                }

                var target = ArchitecturePath.Relative(root, resolved);
                if (target.Contains("99_recyclebin/", StringComparison.OrdinalIgnoreCase) || IsArchivedPath(target))
                {
                    violations.Add($"{Relative(root, solution)} -> {match.Groups[1].Value}");
                }
            }
        }

        Assert.True(violations.Count == 0, "solutions still carry archived or recycle-bin inputs:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void BackslashReferences_AreResolvedBeforeComparison()
    {
        // MSBuild accepts both separators; a guard that only understands forward slashes would let a backslash
        // ProjectReference point back into an archived project on Linux.
        var root = Path.Combine(Path.GetTempPath(), $"issue131-archive-{Guid.NewGuid():N}");
        try
        {
            var consumer = Path.Combine(root, "src/Consumer/Consumer.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(consumer)!);
            File.WriteAllText(
                consumer,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup>"
                + "<ProjectReference Include=\"..\\Framework\\Infrastructure\\CrestCreates.Configuration\\CrestCreates.Configuration.csproj\" />"
                + "</ItemGroup></Project>");

            var resolved = ArchitecturePath.TryResolve(
                Path.GetDirectoryName(consumer)!,
                "..\\Framework\\Infrastructure\\CrestCreates.Configuration\\CrestCreates.Configuration.csproj");

            Assert.NotNull(resolved);
            var relative = ArchitecturePath.Relative(root, resolved!);
            Assert.Equal("src/Framework/Infrastructure/CrestCreates.Configuration/CrestCreates.Configuration.csproj", relative);
            Assert.True(IsArchivedPath(relative));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ActiveSources_DoNotUseArchivedNamespacesOrModules()
    {
        var root = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (var file in ActiveSourceFiles(root))
        {
            var relative = Relative(root, file);
            if (AllowedMentions.Any(allowed => relative.StartsWith(allowed, StringComparison.Ordinal))
                || relative.StartsWith("samples/AssetManagement/docs/", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (var @namespace in ArchivedNamespaces)
            {
                if (text.Contains(@namespace, StringComparison.Ordinal)
                    || text.Contains($"using {@namespace};", StringComparison.Ordinal))
                {
                    violations.Add($"{relative}: {@namespace}");
                }
            }

            if (relative.StartsWith("tests/", StringComparison.Ordinal))
            {
                continue;
            }

            if (text.Contains("ConfigurationModule", StringComparison.Ordinal)
                || text.Contains("EventStoreModule", StringComparison.Ordinal))
            {
                violations.Add($"{relative}: archived module name");
            }
        }

        Assert.True(violations.Count == 0, "active sources still use archived namespaces or modules:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Archive_KeepsEveryOriginalPathAndItsAdjudication()
    {
        var root = FindRepositoryRoot();
        foreach (var project in ArchivedProjects)
        {
            var directory = Path.Combine(root, ArchiveRoot, project);
            Assert.True(Directory.Exists(directory), $"archived project is missing: {project}");
            Assert.True(
                File.Exists(Path.Combine(directory, Path.GetFileName(project) + ".csproj")),
                $"archived project lost its csproj: {project}");
        }

        Assert.True(
            File.Exists(Path.Combine(root, ArchiveRoot, "src/Runtime/Eventing/CrestCreates.EventBus.Abstract/IEventStore.cs")),
            "the dead IEventStore contract file must stay archived rather than deleted");
        Assert.True(
            File.Exists(Path.Combine(root, ArchiveRoot, "README.md")),
            "the archive must carry its adjudication README");

        foreach (var file in new[]
                 {
                     "src/Framework/Ddd/CrestCreates.Application/AuditLog/AuditLogCleanupAppService.cs",
                     "src/Framework/Ddd/CrestCreates.Application.Contracts/Interfaces/IAuditLogCleanupAppService.cs",
                     "src/Framework/Ddd/CrestCreates.Application.Contracts/DTOs/AuditLog/CleanupAuditLogsDto.cs",
                     "tests/Framework/Web/CrestCreates.IntegrationTests/AuditLogIntegrationTests.cs",
                     "README.md"
                 })
        {
            Assert.True(File.Exists(Path.Combine(root, AuditArchiveRoot, file)), $"#132 archive lost {file}");
        }

        var readme = File.ReadAllText(Path.Combine(root, ArchiveRoot, "README.md"));
        foreach (var marker in new[] { "ExternalApi", "LegacyDatabase", "Agent.Runtime", "Configuration", "Draft", "EventStore", "IEventStore" })
        {
            Assert.Contains(marker, readme, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EventBusAbstractions_KeepTheirLiveContracts_ButNotTheArchivedStoreOnes()
    {
        var root = FindRepositoryRoot();
        var abstractions = Path.Combine(root, "src/Runtime/Eventing/CrestCreates.EventBus.Abstract");
        var files = Directory.EnumerateFiles(abstractions, "*.cs", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .ToArray();

        Assert.DoesNotContain("IEventStore.cs", files);
        Assert.Contains("ITenantEventContextAccessor.cs", files);
        Assert.Contains("TenantEventContextAccessor.cs", files);
    }

    private const string AuditArchiveRoot = "99_RecycleBin/issue-132-audit-retention";
    private static readonly string[] RetiredAuditTypes =
    [
        "AuditLogCleanupAppService", "IAuditLogCleanupAppService",
        "CleanupAuditLogsRequestDto", "CleanupAuditLogsResultDto"
    ];

    [Fact]
    public void ActiveBuildInputs_DoNotReviveRetiredAuditCleanup()
    {
        var root = FindRepositoryRoot();
        var violations = ActiveProjectFiles(root).Order(StringComparer.Ordinal)
            .SelectMany(project => AuditCleanupViolations(root, project)).ToArray();
        Assert.True(violations.Length == 0,
            "retired audit cleanup re-entered active build/distribution inputs:" + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("public class AuditLogCleanupAppService {}", "ARC132-TYPE")]
    [InlineData("public interface IAuditLogCleanupAppService {}", "ARC132-TYPE")]
    [InlineData("public record CleanupAuditLogsRequestDto;", "ARC132-TYPE")]
    [InlineData("public class CleanupAuditLogsResultDto {}", "ARC132-TYPE")]
    [InlineData("public interface IAuditLogRepository { System.Threading.Tasks.Task<int> DeleteOlderThanAsync(System.DateTime cutoff); }", "ARC132-METHOD")]
    [InlineData("public class EfCoreAuditLogRepository { public int DeleteOlderThanAsync() => 0; }", "ARC132-METHOD")]
    public void AuditCleanupGuard_RejectsRestoredDeclarations(string source, string diagnostic)
    {
        WithAuditMutationProject((root, project) =>
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(project)!, "Restored.cs"), source);
            Assert.Contains(AuditCleanupViolations(root, project), violation => violation.Contains(diagnostic, StringComparison.Ordinal));
        });
    }

    [Theory]
    [InlineData("Compile")]
    [InlineData("Content")]
    [InlineData("None")]
    public void AuditCleanupGuard_RejectsImportedArchiveInputs(string kind)
    {
        WithAuditMutationProject((root, project) =>
        {
            var archive = Path.Combine(root, AuditArchiveRoot, "Restored.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
            File.WriteAllText(archive, File.ReadAllText(Path.Combine(FindRepositoryRoot(), AuditArchiveRoot,
                "src/Framework/Ddd/CrestCreates.Application/AuditLog/AuditLogCleanupAppService.cs")));
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(project)!, "Archive.props"),
                $"<Project><PropertyGroup><RetiredRoot>../../{AuditArchiveRoot}</RetiredRoot></PropertyGroup>"
                + $"<ItemGroup><{kind} Include=\"$(RetiredRoot)/*.cs\" Link=\"Hidden.cs\" Pack=\"true\" PackagePath=\"contentFiles/cs/any\" /></ItemGroup></Project>");
            var violations = AuditCleanupViolations(root, project);
            Assert.Contains(violations, violation => violation.Contains($"ARC132-INPUT {kind}", StringComparison.Ordinal));
            if (kind == "Compile")
                Assert.Contains(violations, violation => violation.Contains("ARC132-TYPE", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void AuditCleanupGuard_IgnoresHistoryStringsAndRemovedSources()
    {
        WithAuditMutationProject((root, project) =>
        {
            var directory = Path.GetDirectoryName(project)!;
            File.WriteAllText(Path.Combine(directory, "History.cs"),
                "// class AuditLogCleanupAppService {}\npublic class History { public const string Text = \"DeleteOlderThanAsync CleanupAuditLogsResultDto\"; }");
            File.WriteAllText(Path.Combine(directory, "Removed.cs"), "public class AuditLogCleanupAppService {}");
            File.WriteAllText(Path.Combine(directory, "Archive.props"),
                "<Project><ItemGroup><Compile Remove=\"Removed.cs\" /></ItemGroup></Project>");
            Assert.Empty(AuditCleanupViolations(root, project));
        });
    }

    private static IReadOnlyList<string> AuditCleanupViolations(string root, string project)
    {
        var inputs = TenantIdentityEvaluatedInputs.ReadForArchiveGuard(project, "Debug");
        var owner = Relative(root, project);
        if (!inputs.IsAvailable)
            return [$"ARC132-COVERAGE {owner}: {inputs.UnavailableReason}"];

        var violations = new List<string>();
        foreach (var (kind, path) in inputs.DistributionInputs)
        {
            var relative = Path.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/');
            if (relative.StartsWith(AuditArchiveRoot + "/", StringComparison.OrdinalIgnoreCase))
                violations.Add($"ARC132-INPUT {kind} {owner} -> {relative}");
        }

        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)
            .WithPreprocessorSymbols(inputs.DefineConstants.Split(';', StringSplitOptions.RemoveEmptyEntries));
        foreach (var file in inputs.CompileFiles)
        {
            if (!File.Exists(file))
            {
                violations.Add($"ARC132-COVERAGE {owner}: missing Compile input {file}");
                continue;
            }

            var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(file), options).GetRoot();
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            foreach (var node in syntax.DescendantNodes())
            {
                var name = node switch
                {
                    BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
                    IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                    _ => null
                };
                if (name is not null && RetiredAuditTypes.Contains(name, StringComparer.Ordinal))
                    violations.Add($"ARC132-TYPE {owner} -> {relative}: {name}");

                if (node is MethodDeclarationSyntax method
                    && method.Identifier.ValueText == "DeleteOlderThanAsync"
                    && method.Ancestors().OfType<TypeDeclarationSyntax>().Any(type =>
                        type.Identifier.ValueText.Contains("AuditLogRepository", StringComparison.Ordinal)))
                    violations.Add($"ARC132-METHOD {owner} -> {relative}: DeleteOlderThanAsync");
            }
        }
        return violations;
    }

    private static void WithAuditMutationProject(Action<string, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), $"audit-exit-{Guid.NewGuid():N}");
        try
        {
            var project = Path.Combine(root, "src/Consumer/Consumer.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(project)!);
            File.WriteAllText(project,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>"
                + "<Import Project=\"Archive.props\" Condition=\"Exists('Archive.props')\" /></Project>");
            check(root, project);
        }
        finally
        {
            // Move temporary evidence out of the active fixture; no repository archive is altered.
            if (Directory.Exists(root))
                Directory.Move(root, root + ".completed");
        }
    }

    private static bool IsArchivedPath(string relativePath) =>
        ArchivedProjects.Any(project => string.Equals(relativePath, project, StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(project + "/", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> ActiveProjectFiles(string root) =>
        Enumerate("src").Concat(Enumerate("samples")).Concat(Enumerate("tests"))
            .Where(path => !path.Contains("/bin/", StringComparison.Ordinal)
                           && !path.Contains("/obj/", StringComparison.Ordinal)
                           && !Relative(root, path).Contains(ArchiveRoot.Replace('/', Path.DirectorySeparatorChar), StringComparison.Ordinal));

    private static IEnumerable<string> ActiveSourceFiles(string root) =>
        Enumerate("src").Concat(Enumerate("samples")).Concat(Enumerate("tests"))
            .Select(path => Path.GetDirectoryName(path)!)
            .Distinct(StringComparer.Ordinal)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(path =>
            {
                var normalized = Relative(root, path).Replace('\\', '/').ToLowerInvariant();
                return !normalized.Contains("/bin/") && !normalized.Contains("/obj/") && !normalized.Contains("99_recyclebin");
            });

    private static IEnumerable<string> SolutionFiles(string root) =>
        new[] { Path.Combine(root, "CrestCreates.slnx") }
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "solutions"), "*.slnx", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "samples"), "*.slnx", SearchOption.AllDirectories));

    private static IEnumerable<string> Enumerate(string directory)
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, directory);
        return Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*.csproj", SearchOption.AllDirectories)
            : [];
    }

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/').TrimStart('.').TrimStart('/');

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
