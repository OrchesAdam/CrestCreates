using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

/// <summary>
/// Guards the migrate-and-seed closure invariant: a host that enables the OpenIddict server
/// also registers <c>HostOpenIddictDataSeeder</c> (an <c>IDataSeeder</c>), whose scope lookup
/// needs the OpenIddict schema. When the closure omits <c>OpenIddictDbContext</c>, seeding
/// fails on a fresh database with Npgsql 42P01 "relation \"OpenIddictScopes\" does not exist"
/// during host startup — the defect that kept the integration suite out of CI (I-18).
/// </summary>
public sealed class OpenIddictSeedClosureArchitectureTests
{
    private const string ClosureDeclaration = "typeof(OpenIddictDbContext)";

    /// <summary>
    /// Both entries register <c>HostOpenIddictDataSeeder</c>: the direct extension and the platform
    /// composition (<c>AddCrestWeb</c>) that calls it when OpenIddict is enabled.
    /// </summary>
    private static readonly string[] SeederEntries =
    [
        "AddOpenIddictServer(",
        "AddCrestWeb(",
    ];

    /// <summary>
    /// The assembly that defines the server registration is a provider, not a consumer of the closure.
    /// </summary>
    private static readonly string[] ProviderRoots =
    [
        "src/Framework/Web/CrestCreates.AspNetCore.Authentication.OpenIddict",
    ];

    private static readonly string[] ExcludedDirectoryNames =
    [
        "obj",
        "bin",
        "99_RecycleBin",
    ];

    [Fact]
    public void Hosts_RegisteringOpenIddictSeeder_DeclareOpenIddictDbContextInTheirMigrateClosure()
    {
        var repoRoot = FindRepoRoot();
        var closuresByHostRoot = CollectClosuresByHostRoot(repoRoot);
        var violations = new List<string>();

        foreach (var file in EnumerateActiveSourceFiles(repoRoot))
        {
            var hostRoot = ResolveHostRoot(repoRoot, file);
            if (hostRoot is null || ProviderRoots.Contains(hostRoot, StringComparer.Ordinal))
            {
                continue;
            }

            if (!EnablesOpenIddictSeeder(file))
            {
                continue;
            }

            if (!closuresByHostRoot.Contains(hostRoot, StringComparer.Ordinal))
            {
                violations.Add($"{ArchitecturePath.Relative(repoRoot, file)} registers the OpenIddict data seeder, "
                    + $"but no source under '{hostRoot}' declares {ClosureDeclaration} in its "
                    + "HostMigrationAndSeedRunner closure.");
            }
        }

        Assert.True(
            violations.Count == 0,
            "OpenIddict hosts are missing their schema from the migrate-and-seed closure:" + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Coverage control: the guard must keep observing real hosts. If path shapes change so that no host
    /// is matched, the first test would pass vacuously.
    /// </summary>
    [Fact]
    public void Scan_Still_ObservesEveryKnownOpenIddictHost()
    {
        var repoRoot = FindRepoRoot();
        var observed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in EnumerateActiveSourceFiles(repoRoot))
        {
            var hostRoot = ResolveHostRoot(repoRoot, file);
            if (hostRoot is null || !EnablesOpenIddictSeeder(file))
            {
                continue;
            }

            observed.Add(hostRoot);
        }

        foreach (var expected in new[]
                 {
                     "samples/LibraryManagement",
                     "samples/SaaSHelpdesk",
                     "src/Platform/CrestCreates.Web",
                 })
        {
            Assert.True(
                observed.Contains(expected, StringComparer.Ordinal),
                $"The OpenIddict closure guard no longer observes '{expected}'. Observed: "
                + string.Join(", ", observed.OrderBy(x => x)) + ".");
        }
    }

    private static bool EnablesOpenIddictSeeder(string filePath) =>
        SeederEntries.Any(entry => CodeText(filePath).Contains(entry, StringComparison.Ordinal));

    /// <summary>
    /// Comment-only lines are prose about the API, not uses of it: matching them would report hosts
    /// that never register the seeder (the provider extension documents 'AddOpenIddictServer()' in a
    /// doc comment). Only compilable lines are considered.
    /// </summary>
    private static string CodeText(string filePath) => string.Join(
        "\n",
        File.ReadLines(filePath)
            .Select(line => line.TrimStart())
            .Where(line => !line.StartsWith("//", StringComparison.Ordinal)
                           && !line.StartsWith("*", StringComparison.Ordinal)
                           && !line.StartsWith("/*", StringComparison.Ordinal)));

    private static HashSet<string> CollectClosuresByHostRoot(string repoRoot)
    {
        var roots = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in EnumerateActiveSourceFiles(repoRoot))
        {
            if (!CodeText(file).Contains(ClosureDeclaration, StringComparison.Ordinal))
            {
                continue;
            }

            // A closure registered by a sample's persistence module serves every project of that sample.
            var hostRoot = ResolveHostRoot(repoRoot, file);
            if (hostRoot is not null)
            {
                roots.Add(hostRoot);
            }
        }

        return roots;
    }

    /// <summary>
    /// Resolves the unit that owns the seeding decision: for samples the sample directory
    /// (<c>samples/&lt;Name&gt;</c>), otherwise the project directory containing the file.
    /// </summary>
    private static string? ResolveHostRoot(string repoRoot, string filePath)
    {
        var relative = ArchitecturePath.Relative(repoRoot, filePath);
        var segments = relative.Split('/');

        if (segments.Length > 1 && segments[0] == "samples")
        {
            return string.Join('/', segments.Take(2));
        }

        if (!relative.StartsWith("src", StringComparison.Ordinal))
        {
            return null;
        }

        // The .csproj name is not derived from the directory name (directories such as
        // 'CrestCreates.Web' carry a dot in their own name), so locate the owning project by
        // searching upwards for a directory that contains any project file.
        for (var depth = segments.Length - 1; depth >= 1; depth--)
        {
            var candidate = string.Join('/', segments.Take(depth));
            var directory = Path.Combine(repoRoot, Path.Combine(candidate.Split('/')));
            if (Directory.EnumerateFiles(directory, "*.csproj").Any())
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateActiveSourceFiles(string repoRoot)
    {
        return Directory.EnumerateFiles(repoRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !ExcludedDirectoryNames.Any(
                excluded => ArchitecturePath.Relative(repoRoot, file).Split('/').Contains(excluded, StringComparer.Ordinal)))
            .Where(file =>
            {
                var relative = ArchitecturePath.Relative(repoRoot, file);
                return relative.StartsWith("src", StringComparison.Ordinal)
                       || relative.StartsWith("samples", StringComparison.Ordinal);
            });
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
