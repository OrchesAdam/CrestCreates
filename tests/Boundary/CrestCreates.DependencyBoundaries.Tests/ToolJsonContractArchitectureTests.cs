namespace CrestCreates.DependencyBoundaries.Tests;

using Xunit;

public sealed class ToolJsonContractArchitectureTests
{
    [Fact]
    public void ToolContributors_DoNotDeclareDuplicateHandwrittenRootArrays()
    {
        var root = FindRepoRoot();
        var sourceRoot = Path.Combine(root, "src");
        var contributorFiles = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => IsProductionSourcePath(sourceRoot, path))
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains(": IAgentToolJsonContextContributor", StringComparison.Ordinal)
                    || source.Contains(": IMcpToolJsonContextContributor", StringComparison.Ordinal);
            })
            .ToArray();

        Assert.NotEmpty(contributorFiles);
        foreach (var path in contributorFiles)
        {
            var source = File.ReadAllText(path);
            Assert.Contains("RootManifest.BindingRootTypes", source, StringComparison.Ordinal);
            Assert.DoesNotContain("typeof(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("private static readonly Type[]", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new HashSet<Type>", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ToolContexts_DoNotHandwriteGeneratedBindingRoots()
    {
        var root = FindRepoRoot();
        var relativePaths = new[]
        {
            "src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/Json/AgentControlPlaneToolJsonSerializerContext.cs",
            "src/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Abstractions/Json/AgentMemoryToolJsonSerializerContext.cs",
            "src/Integrations/CrestCreates.Mcp.Memory/Json/McpMemoryJsonSerializerContext.cs",
        };

        foreach (var relativePath in relativePaths)
        {
            var source = File.ReadAllText(Path.Combine(root, relativePath));
            Assert.Contains("JsonContractSurface", source, StringComparison.Ordinal);
            Assert.DoesNotContain("JsonSerializable(typeof(", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RemainingToolJsonContexts_AreExplicitlyClassified()
    {
        var root = FindRepoRoot();
        var sourceRoot = Path.Combine(root, "src");
        var contributors = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => IsProductionSourcePath(sourceRoot, path))
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains(": IAgentToolJsonContextContributor", StringComparison.Ordinal)
                    || source.Contains(": IMcpToolJsonContextContributor", StringComparison.Ordinal);
            })
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "AgentMemoryToolJsonContextContributor.cs", "McpMemoryJsonContextContributor.cs" },
            contributors);
    }

    private static bool IsProductionSourcePath(string sourceRoot, string path)
    {
        var relativePath = Path.GetRelativePath(sourceRoot, path)
            .Replace(Path.DirectorySeparatorChar, '/');
        return !relativePath.Contains("/bin/", StringComparison.Ordinal)
            && !relativePath.Contains("/obj/", StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CrestCreates.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
