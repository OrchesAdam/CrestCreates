using System.Text.Json;
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Parsing;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.DescriptorDraft.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Authoring.Tests;

public sealed class UpdateBaseVersionParserTests
{
    private readonly JsonDescriptorAuthoringOutputParser _parser = new();
    private readonly DescriptorAuthoringParseContext _context = new()
    {
        TenantId = "tenant-test",
        AuthorId = "llm-descriptor-authoring-agent",
        AuthorKind = DescriptorDraftAuthorKind.Agent,
        CreatedAt = DateTimeOffset.UnixEpoch,
        IntentText = "Update the asset workflow",
        ExpectedPromptInputHash = "base-version-parser-hash"
    };

    [Fact]
    public void Update_WithExplicitBaseVersion_UsesBaseOneAndProposedPayloadVersionTwo()
    {
        var result = _parser.Parse(
            BuildWorkflowOutput("Update", payloadVersion: 2, baseVersion: "1", includeBaseVersion: true),
            _context);

        result.Status.Should().Be(DescriptorAuthoringStatus.Succeeded);
        result.Diagnostics.Should().BeEmpty();
        var draft = result.DraftSet.Drafts.Should().ContainSingle().Which;
        draft.Operation.Should().Be(DescriptorDraftOperation.Update);
        draft.BaseVersion.Should().Be("1");
        draft.ProposedVersion.Should().Be("2");
    }

    [Fact]
    public void Update_WithoutBaseVersion_RetainsSameVersionCompatibility()
    {
        var result = _parser.Parse(
            BuildWorkflowOutput("Update", payloadVersion: 1),
            _context);

        result.Status.Should().Be(DescriptorAuthoringStatus.Succeeded);
        result.Diagnostics.Should().BeEmpty();
        var draft = result.DraftSet.Drafts.Should().ContainSingle().Which;
        draft.BaseVersion.Should().Be("1");
        draft.ProposedVersion.Should().Be("1");
    }

    [Fact]
    public void Create_WithBaseVersion_IsBlockedWithStructuredDiagnosticAndNoDraft()
    {
        var result = _parser.Parse(
            BuildWorkflowOutput("Create", payloadVersion: 1, baseVersion: "1", includeBaseVersion: true),
            _context);

        result.Status.Should().Be(DescriptorAuthoringStatus.Blocked);
        result.Diagnostics.Should().Contain(d =>
            d.Code == DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput
            && d.Message.Contains("baseVersion", StringComparison.Ordinal));
        result.DraftSet.Drafts.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-a-number")]
    public void Update_WithInvalidBaseVersion_IsBlockedWithStructuredDiagnosticAndNoDraft(string baseVersion)
    {
        var result = _parser.Parse(
            BuildWorkflowOutput("Update", payloadVersion: 2, baseVersion: baseVersion, includeBaseVersion: true),
            _context);

        result.Status.Should().Be(DescriptorAuthoringStatus.Blocked);
        result.Diagnostics.Should().Contain(d =>
            d.Code == DescriptorAuthoringDiagnosticCodes.InvalidProviderOutput
            && d.Message.Contains("baseVersion", StringComparison.Ordinal));
        result.DraftSet.Drafts.Should().BeEmpty();
    }

    private string BuildWorkflowOutput(
        string operation,
        int payloadVersion,
        string? baseVersion = null,
        bool includeBaseVersion = false)
    {
        var item = new Dictionary<string, object?>
        {
            ["descriptorKind"] = "Workflow",
            ["descriptorId"] = "wf_asset_maintenance",
            ["operation"] = operation,
            ["payload"] = new
            {
                id = "wf_asset_maintenance",
                name = "Asset maintenance workflow",
                version = payloadVersion
            }
        };

        if (includeBaseVersion)
        {
            item["baseVersion"] = baseVersion;
        }

        return JsonSerializer.Serialize(new
        {
            contractVersion = "7g.v1",
            promptInputHash = _context.ExpectedPromptInputHash,
            plan = new
            {
                planId = "asset-maintenance-workflow-update",
                intentText = _context.IntentText,
                plannedDescriptorRefs = new[]
                {
                    new { @namespace = "workflow", id = "wf_asset_maintenance", version = payloadVersion }
                }
            },
            items = new[] { item }
        });
    }
}
