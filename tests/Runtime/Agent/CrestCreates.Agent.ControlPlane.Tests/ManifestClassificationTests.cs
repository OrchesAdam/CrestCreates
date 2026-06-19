using Xunit;
using CrestCreates.Agent.ControlPlane.Abstractions;
using FluentAssertions;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Table-driven test that verifies every manifest tool classification
/// matches its actual side effects. This prevents a tool from being
/// incorrectly marked IsReadOnly when it persists state.
///
/// The authoritative source of truth for "does this tool mutate state?"
/// is the DefaultAgentControlPlaneToolService implementation. This test
/// encodes that knowledge as expected values and verifies the manifest
/// and its permission requirements align.
/// </summary>
public class ManifestClassificationTests
{
    private readonly IAgentToolManifestProvider _manifest = new StaticAgentToolManifestProvider();

    /// <summary>
    /// Expected classification for each tool. A tool is classified as mutating
    /// if the facade implementation persists state (stores, updates, creates,
    /// cancels, or changes status). Read-only tools perform pure queries or
    /// ephemeral computations only.
    /// </summary>
    private static readonly (string ToolName, bool ExpectedIsReadOnly, AgentToolCategory ExpectedCategory)[] ExpectedClassifications =
    [
        // Context / Read — all read-only
        (AgentToolName.BuildMetadataContextPack, true, AgentToolCategory.Context),
        (AgentToolName.BuildRuntimeScenarioContextPack, true, AgentToolCategory.Context),
        (AgentToolName.GetDescriptorByRef, true, AgentToolCategory.Context),
        (AgentToolName.SearchDescriptors, true, AgentToolCategory.Context),
        (AgentToolName.ListDescriptorRelationships, true, AgentToolCategory.Context),
        (AgentToolName.GetTopologySummary, true, AgentToolCategory.Context),

        // Draft — create/update/cancel mutate, get/list/compare are read-only
        (AgentToolName.CreateDescriptorDraft, false, AgentToolCategory.Draft),
        (AgentToolName.UpdateDescriptorDraft, false, AgentToolCategory.Draft),
        (AgentToolName.GetDescriptorDraft, true, AgentToolCategory.Draft),
        (AgentToolName.ListDescriptorDrafts, true, AgentToolCategory.Draft),
        (AgentToolName.CancelDescriptorDraft, false, AgentToolCategory.Draft),
        (AgentToolName.CompareDescriptorDraft, true, AgentToolCategory.Draft),

        // Review — ReviewDescriptorDraft persists result and changes draft status (mutating);
        // Validate/Get/List/Explain are read-only
        (AgentToolName.ValidateDescriptorDraft, true, AgentToolCategory.Review),
        (AgentToolName.ReviewDescriptorDraft, false, AgentToolCategory.Review),
        (AgentToolName.GetDraftReviewResult, true, AgentToolCategory.Review),
        (AgentToolName.ListDraftReviewResults, true, AgentToolCategory.Review),
        (AgentToolName.ExplainDiagnostics, true, AgentToolCategory.Review),

        // Fix Proposal — SuggestDescriptorDraftFixes creates and persists proposals (mutating);
        // ApplyFixProposalToDraft updates draft (mutating);
        // Get/List are read-only
        (AgentToolName.SuggestDescriptorDraftFixes, false, AgentToolCategory.FixProposal),
        (AgentToolName.GetFixProposal, true, AgentToolCategory.FixProposal),
        (AgentToolName.ListFixProposals, true, AgentToolCategory.FixProposal),
        (AgentToolName.ApplyFixProposalToDraft, false, AgentToolCategory.FixProposal),

        // Package Preview — PreviewDescriptorPackage and BuildPackageEvidencePreview persist state
        // referenced by activation handoff; BuildActivationReadinessPreview and GetPackagePreview are read-only
        (AgentToolName.PreviewDescriptorPackage, false, AgentToolCategory.PackagePreview),
        (AgentToolName.BuildPackageEvidencePreview, false, AgentToolCategory.PackagePreview),
        (AgentToolName.BuildActivationReadinessPreview, true, AgentToolCategory.PackagePreview),
        (AgentToolName.GetPackagePreview, true, AgentToolCategory.PackagePreview),

        // Activation Handoff — submit/cancel mutate; get is read-only
        // Note: even read-only activation tools are denied by ProductionDefaults
        // because the ActivationHandoff category is controlled separately.
        (AgentToolName.SubmitActivationRequest, false, AgentToolCategory.ActivationHandoff),
        (AgentToolName.GetActivationRequestStatus, true, AgentToolCategory.ActivationHandoff),
        (AgentToolName.CancelActivationRequest, false, AgentToolCategory.ActivationHandoff),

        // Manifest — all read-only
        (AgentToolName.ListAgentTools, true, AgentToolCategory.Manifest),
        (AgentToolName.GetAgentToolDescriptor, true, AgentToolCategory.Manifest)
    ];

    [Fact]
    public void Every_Manifest_Tool_Has_Expected_Classification()
    {
        var tools = _manifest.GetAllTools();
        var toolsByName = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);

        // Verify every expected tool exists in the manifest
        foreach (var (toolName, expectedIsReadOnly, expectedCategory) in ExpectedClassifications)
        {
            toolsByName.Should().ContainKey(toolName, $"tool '{toolName}' should exist in the manifest");

            var tool = toolsByName[toolName];

            tool.IsReadOnly.Should().Be(expectedIsReadOnly,
                $"tool '{toolName}' IsReadOnly should be {expectedIsReadOnly} " +
                $"(it {(expectedIsReadOnly ? "performs pure queries only" : "persists state")})");

            tool.Category.Should().Be(expectedCategory,
                $"tool '{toolName}' Category should be {expectedCategory}");

            // Permission requirements must carry the same ToolCategory and IsReadOnly
            foreach (var perm in tool.Permissions)
            {
                perm.ToolCategory.Should().Be(expectedCategory,
                    $"permission '{perm.PermissionName}' on tool '{toolName}' should have ToolCategory={expectedCategory}");
                perm.IsReadOnly.Should().Be(expectedIsReadOnly,
                    $"permission '{perm.PermissionName}' on tool '{toolName}' should have IsReadOnly={expectedIsReadOnly}");
            }
        }
    }

    [Fact]
    public void Manifest_Contains_All_Expected_Tools()
    {
        var tools = _manifest.GetAllTools();
        var toolNames = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var expectedNames = ExpectedClassifications.Select(e => e.Item1).ToHashSet(StringComparer.Ordinal);

        toolNames.Should().BeEquivalentTo(expectedNames,
            "manifest should contain exactly the tools in the expected classification table");
    }

    [Fact]
    public void No_Mutating_Tool_Is_Classed_As_ReadOnly()
    {
        var mutatingTools = ExpectedClassifications
            .Where(e => !e.ExpectedIsReadOnly)
            .Select(e => e.ToolName)
            .ToList();

        var tools = _manifest.GetAllTools();
        foreach (var toolName in mutatingTools)
        {
            var tool = tools.First(t => t.Name == toolName);
            tool.IsReadOnly.Should().BeFalse(
                $"mutating tool '{toolName}' must not be marked IsReadOnly=true in the manifest");
        }
    }

    [Fact]
    public async Task ProductionDefaults_Denies_All_Mutating_Tools()
    {
        var service = new DefaultAgentToolAuthorizationService(AgentToolAuthorizationOptions.ProductionDefaults);
        var tools = _manifest.GetAllTools();

        foreach (var tool in tools)
        {
            foreach (var perm in tool.Permissions)
            {
                var context = new AgentToolInvocationContext
                {
                    TenantId = "test",
                    ActorId = "actor",
                    ActorKind = AgentToolActorKind.Agent,
                    CorrelationId = "corr",
                    ToolName = tool.Name,
                    InvocationSource = AgentToolInvocationSource.Direct
                };

                var result = await service.AuthorizeAsync(context, perm, tool.Name);

                if (!tool.IsReadOnly)
                {
                    result.IsAllowed.Should().BeFalse(
                        $"ProductionDefaults should deny mutating tool '{tool.Name}' (permission '{perm.PermissionName}')");
                }
            }
        }
    }
}
