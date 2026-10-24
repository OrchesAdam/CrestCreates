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
        var violations = FindViolations(root);

        Assert.True(
            violations.Count == 0,
            "PluginSystem is an opt-in experiment; a production project now depends on it:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void BackslashAndForwardSlashReferences_AreEquivalent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"issue131-isolation-{Guid.NewGuid():N}");

        try
        {
            WriteProject(Path.Combine(root, "src/Consumer/Consumer.csproj"),
                @"..\Integrations\CrestCreates.PluginSystem\CrestCreates.PluginSystem.csproj");
            WriteProject(Path.Combine(root, "src/Other/Other.csproj"),
                "../Integrations/CrestCreates.PluginSystem/CrestCreates.PluginSystem.csproj");
            WriteProject(Path.Combine(root, "src/Integrations/CrestCreates.PluginSystem/CrestCreates.PluginSystem.csproj"),
                null);

            var violations = FindViolations(root);

            Assert.Equal(2, violations.Count);
            Assert.Contains(violations, violation => violation.Contains("src/Consumer/Consumer.csproj", StringComparison.Ordinal));
            Assert.Contains(violations, violation => violation.Contains("src/Other/Other.csproj", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>Every production ProjectReference must resolve with either separator form.</summary>
    internal static IReadOnlyList<string> FindViolations(string root)
    {
        var violations = new List<string>();
        foreach (var project in ProductionProjects(root))
        {
            var relative = ArchitecturePath.Relative(root, project);
            if (ArchitecturePath.IsUnder(relative, PluginSystemProject))
            {
                continue;
            }

            foreach (var reference in XDocument.Load(project).Descendants("ProjectReference")
                         .Select(element => element.Attribute("Include")?.Value)
                         .OfType<string>())
            {
                var resolved = ArchitecturePath.TryResolve(Path.GetDirectoryName(project)!, reference);
                if (resolved is null)
                {
                    continue;
                }

                var target = ArchitecturePath.Relative(root, resolved);
                if (ArchitecturePath.IsUnder(target, PluginSystemProject))
                {
                    violations.Add($"{relative} -> {target}");
                }
            }
        }

        return violations;
    }

    private static void WriteProject(string path, string? reference)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content = reference is null
            ? "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"
            : $"<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"{reference}\" /></ItemGroup></Project>";
        File.WriteAllText(path, content);
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
