using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.AgentTool;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests;

public sealed class AgentToolDescriptorValidatorTests
{
    [Fact]
    public void Validate_ReportsGlobalActiveToolNameAndIdentityConflicts()
    {
        var first = AgentToolRuntimeTestFixture.Tool("tool-a", "cap-a", "same");
        var duplicateName = AgentToolRuntimeTestFixture.Tool("tool-b", "cap-b", "same");
        var duplicateIdentity = AgentToolRuntimeTestFixture.Tool("tool-a", "cap-c", "third");

        var report = new AgentToolDescriptorValidator().Validate(
            new[] { first, duplicateName, duplicateIdentity });

        report.Issues.Select(issue => issue.Code?.Value).Should().Contain(
            AgentToolStartupDiagnosticCodes.ActiveToolNameConflict);
        report.Issues.Select(issue => issue.Code?.Value).Should().Contain(
            AgentToolStartupDiagnosticCodes.DescriptorIdentityConflict);
    }

    [Fact]
    public void Validate_AllowsHistoricalDescriptorWithoutExecutableArtifacts()
    {
        var historical = AgentToolRuntimeTestFixture.Tool(
            "old-tool",
            "old-capability",
            "old.tool",
            DescriptorState.Removed);

        var report = new AgentToolDescriptorValidator().Validate(new[] { historical });

        report.HasErrors.Should().BeFalse();
    }

    [Theory]
    [InlineData(VersionSelectionMode.Exact, 0)]
    [InlineData(VersionSelectionMode.Latest, 1)]
    [InlineData(VersionSelectionMode.Compatible, 1)]
    public void Validate_RejectsUnsupportedCapabilitySelection(
        VersionSelectionMode mode,
        int version)
    {
        var source = AgentToolRuntimeTestFixture.Tool("tool", "cap", "tool");
        var invalid = new AgentCapabilityToolDescriptor
        {
            Id = source.Id,
            Name = source.Name,
            Version = source.Version,
            State = source.State,
            Capability = new CapabilityProjectionReference("cap", version, mode),
            ToolName = source.ToolName,
            Description = source.Description,
            SelectionPolicy = source.SelectionPolicy,
            SideEffectKind = source.SideEffectKind,
            ApprovalMode = source.ApprovalMode,
            Budget = source.Budget,
            AuditMode = source.AuditMode,
            AllowedAgentRoles = source.AllowedAgentRoles
        };

        var report = new AgentToolDescriptorValidator().Validate(new[] { invalid });

        report.Issues.Select(issue => issue.Code?.Value).Should().Contain(
            AgentToolStartupDiagnosticCodes.UnsupportedCapabilitySelection);
    }

    [Fact]
    public void Validate_RejectsUnsafeDefaultEnumValuesAndInvalidLifecycle()
    {
        var source = AgentToolRuntimeTestFixture.Tool("tool", "cap", "tool");
        var invalid = new AgentCapabilityToolDescriptor
        {
            Id = source.Id,
            Name = source.Name,
            Version = source.Version,
            State = DescriptorState.Active,
            SupersededById = "replacement",
            Capability = source.Capability,
            ToolName = source.ToolName,
            Description = source.Description,
            SelectionPolicy = AgentToolSelectionPolicy.Unknown,
            SideEffectKind = source.SideEffectKind,
            ApprovalMode = source.ApprovalMode,
            Budget = source.Budget,
            AuditMode = source.AuditMode,
            AllowedAgentRoles = source.AllowedAgentRoles
        };

        var report = new AgentToolDescriptorValidator().Validate(new[] { invalid });

        report.Issues.Select(issue => issue.Code?.Value).Should().Contain(
            AgentToolStartupDiagnosticCodes.InvalidDescriptorContract);
        report.Issues.Select(issue => issue.Code?.Value).Should().Contain(
            AgentToolStartupDiagnosticCodes.InvalidLifecycle);
    }
}
