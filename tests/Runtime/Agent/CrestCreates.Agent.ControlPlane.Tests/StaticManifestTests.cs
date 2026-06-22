using Xunit;
using CrestCreates.Agent.ControlPlane.Abstractions;
using FluentAssertions;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Tests for the static tool manifest provider.
/// Verifies: deterministic manifest, no runtime discovery, AOT safety,
/// all 32 tools declared, correct categories and permissions.
/// </summary>
public class StaticManifestTests : AgentControlPlaneTestBase
{
    private readonly StaticAgentToolManifestProvider _provider = new();

    [Fact]
    public void GetAllTools_Returns_32_Tools()
    {
        var tools = _provider.GetAllTools();
        tools.Should().HaveCount(32);
    }

    [Fact]
    public void GetAllTools_All_Have_NonEmpty_Name_And_Description()
    {
        var tools = _provider.GetAllTools();
        foreach (var tool in tools)
        {
            tool.Name.Should().NotBeNullOrEmpty($"tool must have a name");
            tool.Description.Should().NotBeNullOrEmpty($"tool '{tool.Name}' must have a description");
        }
    }

    [Fact]
    public void GetAllTools_Names_Are_Unique()
    {
        var tools = _provider.GetAllTools();
        var names = tools.Select(t => t.Name).ToList();
        names.Distinct().Count().Should().Be(names.Count, "all tool names must be unique");
    }

    [Fact]
    public void GetAllTools_None_Mutate_Runtime_Registry()
    {
        var tools = _provider.GetAllTools();
        foreach (var tool in tools)
        {
            tool.MutatesRuntimeRegistry.Should().BeFalse(
                $"tool '{tool.Name}' must not mutate runtime registry — Agent cannot execute runtime handlers");
        }
    }

    [Theory]
    [InlineData("CreateDescriptorDraft", false)]
    [InlineData("UpdateDescriptorDraft", false)]
    [InlineData("CancelDescriptorDraft", false)]
    [InlineData("ApplyFixProposalToDraft", false)]
    [InlineData("SubmitActivationRequest", false)]
    [InlineData("CancelActivationRequest", false)]
    [InlineData("GetDescriptorByRef", true)]
    [InlineData("SearchDescriptors", true)]
    [InlineData("GetTopologySummary", true)]
    [InlineData("ListAgentTools", true)]
    public void Tool_IsReadOnly_Matches_Expectation(string toolName, bool expectedReadOnly)
    {
        var tool = _provider.GetToolByName(toolName);
        tool.Should().NotBeNull($"tool '{toolName}' should exist in manifest");
        tool!.IsReadOnly.Should().Be(expectedReadOnly);
    }

    [Fact]
    public void GetToolByName_Returns_Null_For_Unknown_Tool()
    {
        _provider.GetToolByName("NonExistentTool").Should().BeNull();
    }

    [Fact]
    public void Manifest_Tools_Cover_All_Categories()
    {
        var tools = _provider.GetAllTools();
        var categories = tools.Select(t => t.Category).Distinct().ToList();

        categories.Should().Contain(AgentToolCategory.Context);
        categories.Should().Contain(AgentToolCategory.Draft);
        categories.Should().Contain(AgentToolCategory.Review);
        categories.Should().Contain(AgentToolCategory.ReviewReport);
        categories.Should().Contain(AgentToolCategory.FixProposal);
        categories.Should().Contain(AgentToolCategory.PackagePreview);
        categories.Should().Contain(AgentToolCategory.ActivationHandoff);
        categories.Should().Contain(AgentToolCategory.Manifest);
    }

    [Fact]
    public void Manifest_Tools_Have_Correct_Category_Counts()
    {
        var tools = _provider.GetAllTools();
        tools.Count(t => t.Category == AgentToolCategory.Context).Should().Be(6);
        tools.Count(t => t.Category == AgentToolCategory.Draft).Should().Be(6);
        tools.Count(t => t.Category == AgentToolCategory.Review).Should().Be(5);
        tools.Count(t => t.Category == AgentToolCategory.ReviewReport).Should().Be(2);
        tools.Count(t => t.Category == AgentToolCategory.FixProposal).Should().Be(4);
        tools.Count(t => t.Category == AgentToolCategory.PackagePreview).Should().Be(4);
        tools.Count(t => t.Category == AgentToolCategory.ActivationHandoff).Should().Be(3);
        tools.Count(t => t.Category == AgentToolCategory.Manifest).Should().Be(2);
    }

    [Fact]
    public void All_Tools_Allow_All_Actor_Kinds()
    {
        var tools = _provider.GetAllTools();
        var expectedActors = new[]
        {
            AgentToolActorKind.Human,
            AgentToolActorKind.Agent,
            AgentToolActorKind.System,
            AgentToolActorKind.Import,
            AgentToolActorKind.Generator
        };

        foreach (var tool in tools)
        {
            tool.AllowedActors.Should().BeEquivalentTo(expectedActors,
                $"tool '{tool.Name}' should allow all actor kinds");
        }
    }

    [Fact]
    public void Manifest_Tools_Have_Permission_Requirements()
    {
        var tools = _provider.GetAllTools();
        // Manifest tools have no permissions, all others must have at least one
        var nonManifestTools = tools.Where(t => t.Category != AgentToolCategory.Manifest);
        foreach (var tool in nonManifestTools)
        {
            tool.Permissions.Should().NotBeEmpty(
                $"tool '{tool.Name}' must declare at least one permission requirement");
        }
    }

    [Fact]
    public void Manifest_Tools_Have_No_Permission_Requirements()
    {
        var tools = _provider.GetAllTools();
        var manifestTools = tools.Where(t => t.Category == AgentToolCategory.Manifest);
        foreach (var tool in manifestTools)
        {
            tool.Permissions.Should().BeEmpty(
                $"manifest tool '{tool.Name}' should not require permissions");
        }
    }

    [Fact]
    public void GetAllTools_Is_Deterministic_Across_Calls()
    {
        var first = _provider.GetAllTools();
        var second = _provider.GetAllTools();
        first.Select(t => t.Name).Should().BeEquivalentTo(second.Select(t => t.Name),
            o => o.WithStrictOrdering(),
            "manifest must be deterministic across calls");
    }
}
