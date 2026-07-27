using System.Security.Cryptography;
using System.Text;

namespace CrestCreates.JsonContracts.Build.PackageTests.Infrastructure;

public static class PackageLayoutAssertions
{
    public static void AssertNoTaskAssemblies(string directory)
    {
        var dlls = Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f).Contains("CrestCreates.JsonContracts.BuildTasks"))
            .ToList();

        if (dlls.Count > 0)
            throw new InvalidOperationException($"Task assemblies leaked: {string.Join(", ", dlls)}");
    }

    public static void AssertBuildAssetsOnly(string packageDirectory)
    {
        var files = Directory.GetFiles(packageDirectory, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(packageDirectory, f))
            .ToList();

        var allowedPrefixes = new[] { "build/", "buildMultiTargeting/", "lib/", "tasks/" };
        var unexpected = files.Where(f => !allowedPrefixes.Any(p => f.StartsWith(p, StringComparison.OrdinalIgnoreCase))).ToList();

        if (unexpected.Count > 0)
            throw new InvalidOperationException($"Unexpected files in package: {string.Join(", ", unexpected)}");
    }
}
