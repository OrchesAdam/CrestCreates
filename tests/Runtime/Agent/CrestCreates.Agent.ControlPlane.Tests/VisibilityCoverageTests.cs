using CrestCreates.Agent.ControlPlane.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Validates the bidirectional coverage registry: every manifest tool has one coverage entry,
/// every coverage entry names an existing manifest tool, and there are no duplicates.
/// All 30 entries are complete; no migration guard remains.
/// Each entry has exactly one resource shape, and None applies only to manifest tools.
/// </summary>
public class VisibilityCoverageTests
{
    /// <summary>
    /// All tool names from the manifest provider (authoritative source of tool names).
    /// </summary>
    private static IReadOnlyList<string> ManifestToolNames =>
        new StaticAgentToolManifestProvider().GetAllTools().Select(t => t.Name).ToList().AsReadOnly();

    /// <summary>
    /// All tool names from the coverage registry.
    /// </summary>
    private static IReadOnlyList<string> CoverageToolNames =>
        AgentToolVisibilityCoverage.All.Select(e => e.ToolName).ToList().AsReadOnly();

    [Fact]
    public void ManifestNames_HaveNoDuplicates()
    {
        ManifestToolNames.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void CoverageNames_HaveNoDuplicates()
    {
        CoverageToolNames.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Coverage_Is_SetEqual_To_Manifest()
    {
        var manifestSet = ManifestToolNames.ToHashSet(StringComparer.Ordinal);
        var coverageSet = CoverageToolNames.ToHashSet(StringComparer.Ordinal);

        manifestSet.SetEquals(coverageSet).Should().BeTrue(
            "every manifest tool must have exactly one coverage entry and vice versa. " +
            $"Missing from coverage: [{string.Join(", ", manifestSet.Except(coverageSet))}]. " +
            $"Extra in coverage: [{string.Join(", ", coverageSet.Except(manifestSet))}]");
    }

    [Fact]
    public void None_Shape_Applies_Only_To_Manifest_Tools()
    {
        var noneTools = AgentToolVisibilityCoverage.All
            .Where(e => e.Shape == AgentToolResourceShape.None)
            .Select(e => e.ToolName)
            .ToList();

        noneTools.Should().BeEquivalentTo(
        [
            AgentToolName.ListAgentTools,
            AgentToolName.GetAgentToolDescriptor
        ], "only manifest tools have no descriptor data");
    }

    [Fact]
    public void All_Coverage_Entries_Have_Resource_Shape()
    {
        foreach (var entry in AgentToolVisibilityCoverage.All)
        {
            Enum.IsDefined(entry.Shape).Should().BeTrue(
                $"Tool '{entry.ToolName}' has an undefined resource shape");
        }
    }

    [Fact]
    public void Coverage_Contains_All_30_Manifest_Tools()
    {
        AgentToolVisibilityCoverage.All.Should().HaveCount(ManifestToolNames.Count,
            "coverage must have exactly one entry per manifest tool");
    }

    [Fact]
    public void Every_Shape_Is_Used_By_At_Least_One_Tool()
    {
        var allShapes = Enum.GetValues<AgentToolResourceShape>();
        var usedShapes = AgentToolVisibilityCoverage.All.Select(e => e.Shape).ToHashSet();

        foreach (var shape in allShapes)
        {
            usedShapes.Should().Contain(shape,
                $"resource shape '{shape}' must be assigned to at least one tool");
        }
    }
}
