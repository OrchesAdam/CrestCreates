using System.Xml.Linq;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

/// <summary>
/// #131 D-12: the dynamic DLL plugin loader is an isolated opt-in experiment. Its execution surface needs
/// runtime assembly loading, so it is JIT-only and its lifecycle is not delivered; the formal chain must not
/// depend on it, and the capability map must keep saying so.
/// </summary>
public class PluginSystemIsolationArchitectureTests
{
    private const string PluginSystemProject = "src/Integrations/CrestCreates.PluginSystem";

    [Fact]
    public void ProductionChain_HasNoPluginSystemDependency()
    {
        var root = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (var project in ProductionProjects(root))
        {
            var relative = Relative(root, project);
            if (relative.StartsWith(PluginSystemProject, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var reference in XDocument.Load(project).Descendants("ProjectReference")
                         .Select(element => element.Attribute("Include")?.Value)
                         .OfType<string>())
            {
                var resolved = Relative(root, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, reference)));
                if (resolved.StartsWith(PluginSystemProject, StringComparison.Ordinal))
                {
                    violations.Add($"{relative} -> {resolved}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "PluginSystem is an opt-in experiment; a production project now depends on it:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void PluginSystem_StillOwnsTheDynamicLoadingSurface()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, PluginSystemProject, "Services/PluginManager.cs"));

        foreach (var marker in new[] { "AssemblyLoadContext", "GetTypes(", "ActivatorUtilities" })
        {
            Assert.True(
                source.Contains(marker, StringComparison.Ordinal),
                $"the JIT-only declaration must stay honest: PluginManager no longer uses {marker}");
        }
    }

    [Fact]
    public void CapabilityMap_RecordsPluginSystemAsBoundedExperiment()
    {
        var root = FindRepositoryRoot();
        var map = File.ReadAllText(Path.Combine(root, "docs/Feature/CapabilityMap.md"));
        var entry = map[map.IndexOf("### D-12", StringComparison.Ordinal)..];

        Assert.Contains("experimental", entry, StringComparison.Ordinal);
        Assert.Contains("JIT-only", entry, StringComparison.Ordinal);
        Assert.Contains("未验证", entry, StringComparison.Ordinal);
        Assert.Contains("限界保留", entry, StringComparison.Ordinal);
    }

    private static IEnumerable<string> ProductionProjects(string root) =>
        new[] { "src", "samples" }
            .SelectMany(directory =>
            {
                var path = Path.Combine(root, directory);
                return Directory.Exists(path)
                    ? Directory.EnumerateFiles(path, "*.csproj", SearchOption.AllDirectories)
                    : [];
            })
            .Where(path =>
            {
                var normalized = Relative(root, path).Replace('\\', '/').ToLowerInvariant();
                return !normalized.Contains("/bin/") && !normalized.Contains("/obj/") && !normalized.Contains("99_recyclebin");
            });

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

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
