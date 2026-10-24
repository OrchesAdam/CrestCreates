using System.Xml.Linq;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

/// <summary>
/// #131 design §5 step 4/5: archived projects stay in the recycle bin and must not re-enter active
/// compilation, solution membership, module aggregation or source usage. Anti-revival guards are guards,
/// not consumers.
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
