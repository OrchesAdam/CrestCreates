using System.Xml.Linq;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

public class DependencyBoundaryTests
{
    [Fact]
    public void CoreProjects_DoNotReferenceUpperLayers()
    {
        AssertNoDirectProjectReferences(
            "src/Core",
            "Core projects must not reference upper layers.",
            new[] { "src/Framework", "src/Metadata", "src/Runtime", "src/Persistence", "src/Platform" });
    }

    [Fact]
    public void MetadataAbstractions_DoesNotReferenceUpperLayers()
    {
        AssertNoDirectProjectReferences(
            "src/Metadata/CrestCreates.Metadata.Abstractions",
            "Metadata.Abstractions must remain descriptor contracts only.",
            new[] { "src/Runtime", "src/Framework", "src/Persistence", "src/Platform" });
    }

    [Fact]
    public void RuntimeProjects_DoNotReferenceFrameworkApiWebOrPlatform()
    {
        AssertNoDirectProjectReferences(
            "src/Runtime",
            "Runtime projects must not reference API/Web framework packages or Platform composition.",
            new[] { "src/Framework/CrestCreates.DynamicApi", "src/Framework/CrestCreates.OpenApi", "src/Framework/CrestCreates.AspNetCore", "src/Framework/CrestCreates.Web", "src/Platform" });
    }

    [Fact]
    public void RuntimeProjects_DoNotReferenceConcreteBusinessOrmProviders()
    {
        AssertNoDirectProjectReferences(
            "src/Runtime",
            "Runtime projects must not reference concrete business ORM providers.",
            new[] { "src/Persistence/CrestCreates.Data.FreeSql", "src/Persistence/CrestCreates.Data.SqlSugar" });
    }

    [Fact]
    public void PersistenceProjects_DoNotReferenceRuntimeWorkflowAgentOrHumanTask()
    {
        AssertNoDirectProjectReferences(
            "src/Persistence",
            "Persistence projects must not own runtime store contracts.",
            new[] { "src/Runtime/CrestCreates.Workflow", "src/Runtime/CrestCreates.Agent", "src/Runtime/CrestCreates.HumanTask" });
    }

    [Fact]
    public void ToolingProjects_DoNotReferenceConcreteRuntimeImplementations()
    {
        AssertNoDirectProjectReferences(
            "src/Tooling",
            "Tooling may reference abstractions but must not reference concrete runtime implementations.",
            new[]
            {
                "src/Runtime/CrestCreates.Capability.Runtime",
                "src/Runtime/CrestCreates.Workflow.Runtime",
                "src/Runtime/CrestCreates.HumanTask.Runtime",
                "src/Runtime/CrestCreates.EventBus.Local",
                "src/Runtime/CrestCreates.EventBus.Local.Channel",
                "src/Runtime/CrestCreates.EventBus.Kafka",
                "src/Runtime/CrestCreates.EventBus.RabbitMQ",
                "src/Runtime/CrestCreates.Audit.Runtime"
            },
            allowMissingRoot: true);
    }

    [Fact]
    public void PlatformProjects_AreAllowedToComposeFrameworkRuntimeAndPersistence()
    {
        var repoRoot = FindRepoRoot();
        var platformRoot = repoRoot.Combine("src/Platform");

        Assert.True(platformRoot.Exists, "Platform root should exist when Platform projects are part of the layout.");
    }

    private static void AssertNoDirectProjectReferences(
        string projectRootRelativePath,
        string reason,
        IReadOnlyCollection<string> forbiddenFragments,
        bool allowMissingRoot = false)
    {
        var repoRoot = FindRepoRoot();
        var projectRoot = repoRoot.Combine(projectRootRelativePath);
        if (!projectRoot.Exists && allowMissingRoot)
        {
            return;
        }

        Assert.True(projectRoot.Exists, $"Project root not found: {projectRootRelativePath}");

        var violations = Directory
            .EnumerateFiles(projectRoot.FullName, "*.csproj", SearchOption.AllDirectories)
            .SelectMany(project => ReadProjectReferences(project)
                .Select(reference => new
                {
                    Project = Path.GetRelativePath(repoRoot.FullName, project),
                    Reference = Normalize(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, reference))),
                }))
            .Where(edge => forbiddenFragments.Any(fragment => edge.Reference.Contains(Normalize(fragment), StringComparison.OrdinalIgnoreCase)))
            .Select(edge => $"{edge.Project} -> {Path.GetRelativePath(repoRoot.FullName, edge.Reference)}")
            .ToArray();

        Assert.True(
            violations.Length == 0,
            reason + Environment.NewLine + "Forbidden project references:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<string> ReadProjectReferences(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        return document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))!;
    }

    private static DirectoryInfo FindRepoRoot()
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

    private static string Normalize(string value) =>
        value.Replace('\\', '/');
}

internal static class DirectoryInfoExtensions
{
    public static DirectoryInfo Combine(this DirectoryInfo directory, string relativePath) =>
        new(Path.Combine(directory.FullName, relativePath));
}
