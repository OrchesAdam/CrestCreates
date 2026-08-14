using System.Linq;
using System.Xml.Linq;
using CrestCreates.Agent.Memory.Persistence.Testing.Manifest;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

/// <summary>
/// Dependency edges for the Phase 9b+ durable Agent Memory provider. The
/// PostgreSQL production project may reference only Agent Memory Abstractions;
/// the runner-free shared kit references only Abstractions; the concrete Agent
/// Memory runtime never references the provider.
/// </summary>
public class DurableAgentMemoryPersistenceArchitectureTests
{
    private static readonly string[] ConcreteAgentMemoryProjects =
    [
        "src/Runtime/Agent/CrestCreates.Agent.Memory/CrestCreates.Agent.Memory.csproj",
        "src/Runtime/Agent/CrestCreates.Agent.Memory.ReadCore/CrestCreates.Agent.Memory.ReadCore.csproj",
        "src/Runtime/Agent/CrestCreates.Agent.Memory.Tools/CrestCreates.Agent.Memory.Tools.csproj",
        "src/Runtime/Agent/CrestCreates.Agent.Memory.Accountability/CrestCreates.Agent.Memory.Accountability.csproj"
    ];

    [Fact]
    public void PostgreSqlProvider_Should_ReferenceOnlyAgentMemoryAbstractions()
    {
        var repoRoot = DependencyBoundaryTestsHelpers.FindRepoRoot();
        var providerRoot = repoRoot.Combine("src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql");
        Assert.True(providerRoot.Exists, "PostgreSQL provider project root must exist.");

        var violations = Directory
            .EnumerateFiles(providerRoot.FullName, "*.csproj", SearchOption.TopDirectoryOnly)
            .SelectMany(project => DependencyBoundaryTestsHelpers.ReadProjectReferences(project)
                .Select(reference => new
                {
                    Project = Path.GetFileName(project),
                    Reference = DependencyBoundaryTestsHelpers.Normalize(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, reference)))
                }))
            .Where(edge => ConcreteAgentMemoryProjects.Any(
                fragment => edge.Reference.Contains(DependencyBoundaryTestsHelpers.Normalize(fragment), StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "PostgreSQL provider must reference only Agent.Memory.Abstractions, never a concrete Agent Memory runtime:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations.Select(v => v.Reference)));
    }

    [Fact]
    public void SharedAgentMemoryContractKit_Should_ReferenceOnlyAgentMemoryAbstractions()
    {
        var repoRoot = DependencyBoundaryTestsHelpers.FindRepoRoot();
        var kitRoot = repoRoot.Combine("tests/Shared/CrestCreates.Agent.Memory.Persistence.Testing");
        Assert.True(kitRoot.Exists, "Shared contract kit project root must exist.");

        var references = Directory
            .EnumerateFiles(kitRoot.FullName, "*.csproj", SearchOption.TopDirectoryOnly)
            .SelectMany(DependencyBoundaryTestsHelpers.ReadProjectReferences)
            .Select(reference => DependencyBoundaryTestsHelpers.Normalize(reference))
            .ToArray();

        Assert.True(
            references.Length == 1
            && references[0].EndsWith("CrestCreates.Agent.Memory.Abstractions.csproj", StringComparison.OrdinalIgnoreCase),
            "The runner-free contract kit may reference only Agent.Memory.Abstractions. Actual: "
            + string.Join(", ", references));
    }

    [Fact]
    public void AgentMemoryRuntime_Should_Not_ReferenceThePostgreSqlProvider()
    {
        AssertNoDirectProjectReferences(
            "src/Runtime/Agent/CrestCreates.Agent.Memory",
            "Agent Memory runtime must never reference the PostgreSQL persistence provider.",
            new[] { "src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql" });
    }

    [Fact]
    public void AgentMemoryAbstractions_Should_Not_ReferenceThePostgreSqlProvider()
    {
        AssertNoDirectProjectReferences(
            "src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions",
            "Agent Memory abstractions must never reference the PostgreSQL persistence provider.",
            new[] { "src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql" });
    }

    [Fact]
    public void PostgreSqlProvider_Should_Not_ReferenceFrameworkApiWebOrPlatform()
    {
        AssertNoDirectProjectReferences(
            "src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql",
            "PostgreSQL provider must remain a Runtime persistence participant — no Framework Api/Web or Platform composition.",
            new[]
            {
                "src/Framework/Api",
                "src/Framework/Web",
                "src/Platform"
            });
    }

    [Fact]
    public void PostgreSqlAgentMemoryStores_Should_HaveNoAccountabilityDependency()
    {
        var repoRoot = DependencyBoundaryTestsHelpers.FindRepoRoot();
        var storeRoot = repoRoot.Combine("src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql");
        Assert.True(storeRoot.Exists, "PostgreSQL provider project root must exist.");

        var forbidden = new[]
        {
            "IAgentMemoryAccountabilityProducer",
            "IAuditRecorder",
            "IAuditSink",
            "PublishCurationAsync",
            "PublishRecallAsync",
            "PublishSourceExpansionAsync"
        };

        var violations = Directory
            .EnumerateFiles(storeRoot.FullName, "PostgreSqlAgent*.cs", SearchOption.TopDirectoryOnly)
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, index)))
            .Where(item => forbidden.Any(token => item.line.Contains(token, StringComparison.Ordinal)))
            .Select(item => $"{Path.GetRelativePath(repoRoot.FullName, item.file)}:{item.index + 1}: {item.line.Trim()}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Agent Memory Store classes must not reference Accountability producer/recorder/sink semantics:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void DurableAgentMemoryDependencyBoundariesAndCanonicalSolutions_Should_Build()
    {
        var repoRoot = DependencyBoundaryTestsHelpers.FindRepoRoot();
        var requiredProjects = new[]
        {
            "src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/CrestCreates.Agent.Memory.Abstractions.csproj",
            "src/Runtime/Agent/CrestCreates.Agent.Memory/CrestCreates.Agent.Memory.csproj",
            "src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/CrestCreates.Runtime.Persistence.PostgreSql.csproj",
            "tests/Shared/CrestCreates.Agent.Memory.Persistence.Testing/CrestCreates.Agent.Memory.Persistence.Testing.csproj",
            "tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/CrestCreates.Agent.Memory.Tests.csproj",
            "tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj"
        };

        var solutions = new[]
        {
            repoRoot.Combine("CrestCreates.slnx"),
            repoRoot.Combine("solutions/CrestCreates.Runtime.slnx"),
            repoRoot.Combine("solutions/CrestCreates.All.slnx")
        };

        foreach (var solution in solutions)
        {
            Assert.True(File.Exists(solution.FullName), $"Solution not found: {solution.FullName}");
            var content = File.ReadAllText(solution.FullName);
            foreach (var project in requiredProjects)
            {
                Assert.True(
                    content.Contains(project, StringComparison.Ordinal),
                    $"Solution {solution.Name} must include {project} for the canonical build to remain green.");
            }
        }
    }

    [Fact]
    public void GlobalHookUsers_Should_All_BeInThePostgreSqlRuntimeCollection()
    {
        // Plan §13.1: the process-global test hooks (BlockFirstCommand,
        // BlockBeforeCommit, BlockAfterWritePoint, BlockAfterFirstCommand) are
        // consumed one-shot or scoped by the serializer. Any test file that
        // installs them MUST run inside PostgreSqlRuntimeCollection, otherwise
        // it can randomly consume another test's global hook. This guard scans
        // the owning test project and fails closed on any hook user that is not
        // collection-scoped.
        var repoRoot = DependencyBoundaryTestsHelpers.FindRepoRoot();
        var testsRoot = repoRoot.Combine("tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests");
        Assert.True(testsRoot.Exists, "PostgreSQL test project root must exist.");

        var hookTokens = new[]
        {
            "PostgreSqlRuntimeTestHooks.BlockFirstCommand",
            "PostgreSqlRuntimeTestHooks.BlockBeforeCommit",
            "PostgreSqlRuntimeTestHooks.BlockAfterWritePoint",
            "PostgreSqlRuntimeTestHooks.BlockAfterFirstCommand"
        };

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(testsRoot.FullName, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var content = File.ReadAllText(file);
            if (!hookTokens.Any(token => content.Contains(token, StringComparison.Ordinal)))
                continue;

            var relative = Path.GetRelativePath(repoRoot.FullName, file);
            var liveLines = content
                .Split('\n')
                .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal));
            var insideCollection = liveLines.Any(line =>
                line.Contains("[Collection(PostgreSqlRuntimeCollection.Name)]", StringComparison.Ordinal));
            if (!insideCollection)
            {
                violations.Add($"{relative} uses a process-global hook without [Collection(PostgreSqlRuntimeCollection.Name)]");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Every process-global hook user must run inside PostgreSqlRuntimeCollection:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static void AssertNoDirectProjectReferences(
        string projectRootRelativePath,
        string reason,
        IReadOnlyCollection<string> forbiddenFragments)
    {
        var repoRoot = DependencyBoundaryTestsHelpers.FindRepoRoot();
        var projectRoot = repoRoot.Combine(projectRootRelativePath);
        Assert.True(projectRoot.Exists, $"Project root not found: {projectRootRelativePath}");

        var violations = Directory
            .EnumerateFiles(projectRoot.FullName, "*.csproj", SearchOption.AllDirectories)
            .SelectMany(project => DependencyBoundaryTestsHelpers.ReadProjectReferences(project)
                .Select(reference => new
                {
                    Project = Path.GetRelativePath(repoRoot.FullName, project),
                    Reference = DependencyBoundaryTestsHelpers.Normalize(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, reference)))
                }))
            .Where(edge => forbiddenFragments.Any(fragment => edge.Reference.Contains(DependencyBoundaryTestsHelpers.Normalize(fragment), StringComparison.OrdinalIgnoreCase)))
            .Select(edge => $"{edge.Project} -> {Path.GetRelativePath(repoRoot.FullName, edge.Reference)}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            reason + Environment.NewLine + "Forbidden project references:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }
}

internal static class DependencyBoundaryTestsHelpers
{
    public static DirectoryInfo FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props")) &&
                Directory.Exists(Path.Combine(current.FullName, "solutions")))
            {
                return current;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("Repository root could not be found.");
    }

    public static IEnumerable<string> ReadProjectReferences(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        return document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))!;
    }

    public static string Normalize(string value) => value.Replace('\\', '/');
}
