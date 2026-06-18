using System.Xml.Linq;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

public class DependencyBoundaryTests
{
    [Fact]
    public void RuntimeProjects_DoNotReferenceFrameworkApiOrConcreteOrmProviders()
    {
        var forbidden = new[]
        {
            "CrestCreates.DynamicApi",
            "CrestCreates.Application",
            "CrestCreates.Data.FreeSql",
            "CrestCreates.Data.SqlSugar"
        };

        AssertNoDirectProjectReferences("src/Runtime", forbidden);
    }

    [Fact]
    public void MetadataAbstractions_DoesNotReferenceRuntimeOrPersistence()
    {
        AssertNoDirectProjectReferences(
            "src/Metadata/CrestCreates.Metadata.Abstractions",
            new[] { "src/Runtime", "src/Persistence", "CrestCreates.Capability.Runtime", "CrestCreates.Workflow.Runtime" });
    }

    [Fact]
    public void CoreAbstractions_HasNoUpperLayerReferences()
    {
        AssertNoDirectProjectReferences(
            "src/Core/CrestCreates.Core.Abstractions",
            new[] { "src/Metadata", "src/Framework", "src/Runtime", "src/Persistence", "src/Platform", "src/Integrations", "AspNetCore" });
    }

    private static void AssertNoDirectProjectReferences(string projectRootRelativePath, IReadOnlyCollection<string> forbiddenFragments)
    {
        var repoRoot = FindRepoRoot();
        var projectRoot = repoRoot.Combine(projectRootRelativePath);
        var violations = Directory
            .EnumerateFiles(projectRoot.FullName, "*.csproj", SearchOption.AllDirectories)
            .SelectMany(project => ReadProjectReferences(project)
                .Select(reference => new
                {
                    Project = Path.GetRelativePath(repoRoot.FullName, project),
                    Reference = Normalize(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, reference)))
                }))
            .Where(edge => forbiddenFragments.Any(fragment => edge.Reference.Contains(Normalize(fragment), StringComparison.OrdinalIgnoreCase)))
            .Select(edge => $"{edge.Project} -> {Path.GetRelativePath(repoRoot.FullName, edge.Reference)}")
            .ToArray();

        Assert.True(violations.Length == 0, "Forbidden project references:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
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
