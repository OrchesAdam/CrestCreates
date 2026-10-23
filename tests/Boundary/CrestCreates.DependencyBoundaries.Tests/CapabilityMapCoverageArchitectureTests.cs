using System.Text.RegularExpressions;
using Xunit;

namespace CrestCreates.DependencyBoundaries.Tests;

/// <summary>
/// #131 design §5: the capability map is the single navigable project index. A new production project
/// cannot land without a record, and the map cannot keep stale or duplicated entries.
/// </summary>
public class CapabilityMapCoverageArchitectureTests
{
    private const string MapRelativePath = "docs/Feature/CapabilityMap.md";
    private const string ArchiveRoot = "99_RecycleBin/issue-131-capability-map";

    private static readonly string[] DecisionIds =
    [
        "D-01", "D-02", "D-03", "D-04", "D-05", "D-06",
        "D-07", "D-08", "D-09", "D-10", "D-11", "D-12",
    ];

    [Fact]
    public void ProjectIndex_MatchesActiveSrcProjectsExactly()
    {
        var root = FindRepositoryRoot();
        var rows = ReadIndexRows(root, "project-index");
        var paths = rows.Select(Columns).Select(columns => columns[0]).ToArray();

        Assert.True(
            paths.Distinct(StringComparer.Ordinal).Count() == paths.Length,
            "the project index contains duplicate paths: "
            + string.Join(", ", paths.GroupBy(path => path).Where(group => group.Count() > 1).Select(group => group.Key)));

        // Design §5 compares the index against every active src project, including shipped test
        // infrastructure libraries such as CrestCreates.Testing; the identity guard applies its own
        // test-project filter separately.
        var active = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains("/bin/", StringComparison.Ordinal)
                           && !path.Contains("/obj/", StringComparison.Ordinal)
                           && !path.Contains("99_RecycleBin", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var indexed = paths.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        Assert.Equal(active, indexed);

        foreach (var path in paths)
        {
            Assert.True(File.Exists(Path.Combine(root, path)), $"indexed project does not exist: {path}");
        }
    }

    [Fact]
    public void EveryIndexRow_CarriesLayerRecordAndNavigation()
    {
        var root = FindRepositoryRoot();
        foreach (var row in ReadIndexRows(root, "project-index"))
        {
            var columns = Columns(row);
            Assert.Equal(4, columns.Length);
            Assert.StartsWith("src/", columns[0]);
            Assert.False(string.IsNullOrWhiteSpace(columns[1]), $"row '{columns[0]}' has no layer");
            Assert.False(string.IsNullOrWhiteSpace(columns[2]), $"row '{columns[0]}' has no record");
            Assert.False(columns[2].StartsWith("—", StringComparison.Ordinal), $"row '{columns[0]}' has an empty record");
        }
    }

    [Fact]
    public void ScopeAdjudications_CoverTwelveEntriesAndFifteenProjects()
    {
        var root = FindRepositoryRoot();
        var map = ReadMap(root);
        var adjudicated = ReadIndexRows(root, "project-index")
            .Select(Columns)
            .Where(columns => columns[2].StartsWith("D-", StringComparison.Ordinal))
            .ToArray();

        // 8 kept projects stay in the active index; the other 7 are recorded in the archived index.
        Assert.Equal(8, adjudicated.Length);
        Assert.Equal(
            ["D-07", "D-07", "D-08", "D-09", "D-10", "D-11", "D-11", "D-12"],
            adjudicated.Select(columns => columns[2].Split(' ')[0]).Order(StringComparer.Ordinal).ToArray());

        foreach (var decision in DecisionIds)
        {
            Assert.Contains($"### {decision}", map);
        }

        var archived = ReadIndexRows(root, "archived-index").Select(Columns).ToArray();
        Assert.Equal(8, archived.Length);
        Assert.Equal(7, archived.Count(columns => columns[0].StartsWith("src/", StringComparison.Ordinal)));
    }

    [Fact]
    public void ArchivedIndex_MatchesRecycleBinContent()
    {
        var root = FindRepositoryRoot();
        var rows = ReadIndexRows(root, "archived-index");
        Assert.Equal(8, rows.Count);

        var archivedOnDisk = Directory
            .EnumerateFiles(Path.Combine(root, ArchiveRoot), "*.csproj", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Select(path => path[(ArchiveRoot.Length + 1)..])
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var indexedOriginal = rows
            .Select(Columns)
            .Select(columns => columns[0])
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(archivedOnDisk, indexedOriginal);

        foreach (var columns in rows.Select(Columns))
        {
            var recycleBinPath = Regex.Match(columns[1], @"\((?<target>[^)]+)\)").Groups["target"].Value;
            recycleBinPath = recycleBinPath.StartsWith("../../", StringComparison.Ordinal)
                ? recycleBinPath["../../".Length..]
                : recycleBinPath;
            Assert.True(File.Exists(Path.Combine(root, recycleBinPath)), $"archived project is not locatable: {columns[0]}");
            Assert.StartsWith("D-0", columns[2]);
        }
    }

    [Fact]
    public void MapLinks_AllResolve()
    {
        var root = FindRepositoryRoot();
        var mapDirectory = Path.GetDirectoryName(Path.Combine(root, MapRelativePath))!;
        var unresolved = new List<string>();

        foreach (var match in Regex.Matches(ReadMap(root), @"\]\((?<target>[^)#][^)]*)\)").Cast<Match>())
        {
            var target = match.Groups["target"].Value.Split('#')[0];
            if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var resolved = Path.GetFullPath(Path.Combine(mapDirectory, target));
            if (!Directory.Exists(resolved) && !File.Exists(resolved))
            {
                unresolved.Add(target);
            }
        }

        Assert.True(unresolved.Count == 0, "broken map links: " + string.Join(", ", unresolved));
    }

    private static IReadOnlyList<string> ReadIndexRows(string root, string marker)
    {
        var map = ReadMap(root);
        var start = map.IndexOf($"<!-- capability-map:{marker}:start -->", StringComparison.Ordinal);
        var end = map.IndexOf($"<!-- capability-map:{marker}:end -->", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"missing capability map region: {marker}");

        return map[start..end]
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("|", StringComparison.Ordinal))
            .Where(line => !Regex.IsMatch(line, @"^\|\s*-+\s*\|"))
            .Where(line => !line.Contains("Project path", StringComparison.Ordinal)
                           && !line.Contains("Archived project", StringComparison.Ordinal))
            .ToArray();
    }

    private static string[] Columns(string row) =>
        row.Trim('|').Split('|').Select(value => value.Trim()).ToArray();

    private static string ReadMap(string root) =>
        File.ReadAllText(Path.Combine(root, MapRelativePath));

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
