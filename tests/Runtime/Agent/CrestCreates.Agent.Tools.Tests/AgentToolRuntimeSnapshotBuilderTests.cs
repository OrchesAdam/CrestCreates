using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.AgentTool;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests;

public sealed class AgentToolRuntimeSnapshotBuilderTests
{
    [Fact]
    public void Build_PublishesOnlyActiveEntriesAndDoesNotRequireHistoricalBindings()
    {
        var capability = AgentToolRuntimeTestFixture.Capability("capability");
        var active = AgentToolRuntimeTestFixture.Tool("active-tool", capability.Id, "active.tool");
        var historical = AgentToolRuntimeTestFixture.Tool(
            "removed-tool",
            "removed-capability",
            "removed.tool",
            DescriptorState.Removed);
        AgentToolRuntimeTestFixture.RegisterNoPayloadBinding(active);
        var tools = AgentToolRuntimeTestFixture.BuildToolRegistry(active, historical);
        var capabilities = AgentToolRuntimeTestFixture.BuildCapabilityRegistry(capability);
        var schemas = AgentToolRuntimeTestFixture.BuildSchemaRegistry();

        var snapshot = AgentToolRuntimeTestFixture
            .SnapshotBuilder(tools, capabilities, schemas)
            .Build();

        snapshot.Entries.Keys.Should().Equal("active.tool");
        snapshot.Find("removed.tool").Should().BeNull();
        snapshot.Find("active.tool")!.EffectiveSideEffectKind
            .Should().Be(AgentToolSideEffectKind.ReadOnly);
    }

    [Fact]
    public void Build_CapturesLatestCapabilityExactlyOnce()
    {
        var first = AgentToolRuntimeTestFixture.Capability("latest-capability", 1);
        var latest = AgentToolRuntimeTestFixture.Capability("latest-capability", 2);
        var source = AgentToolRuntimeTestFixture.Tool("latest-tool", first.Id, "latest.tool");
        var tool = CopyWithCapability(source, new CapabilityProjectionReference(
            first.Id,
            0,
            VersionSelectionMode.Latest));
        AgentToolRuntimeTestFixture.RegisterNoPayloadBinding(tool);

        var snapshot = AgentToolRuntimeTestFixture.SnapshotBuilder(
                AgentToolRuntimeTestFixture.BuildToolRegistry(tool),
                AgentToolRuntimeTestFixture.BuildCapabilityRegistry(first, latest),
                AgentToolRuntimeTestFixture.BuildSchemaRegistry())
            .Build();

        snapshot.Find(tool.ToolName)!.Capability.Should().BeSameAs(latest);
        snapshot.Find(tool.ToolName)!.DiscoveryContract.CapabilityContract.Version.Should().Be(2);
    }

    [Theory]
    [InlineData(DescriptorState.Draft)]
    [InlineData(DescriptorState.Removed)]
    [InlineData(DescriptorState.Deprecated)]
    public void Build_RejectsActiveToolReferencingNonActiveCapability(DescriptorState state)
    {
        var capability = AgentToolRuntimeTestFixture.Capability(
            $"inactive-capability-{state}",
            state: state);
        var tool = AgentToolRuntimeTestFixture.Tool(
            $"inactive-capability-tool-{state}",
            capability.Id,
            "inactive.capability.tool");
        AgentToolRuntimeTestFixture.RegisterNoPayloadBinding(tool);

        var action = () => AgentToolRuntimeTestFixture.SnapshotBuilder(
                AgentToolRuntimeTestFixture.BuildToolRegistry(tool),
                AgentToolRuntimeTestFixture.BuildCapabilityRegistry(capability),
                AgentToolRuntimeTestFixture.BuildSchemaRegistry())
            .Build();

        action.Should().Throw<AgentToolConfigurationException>()
            .Which.Code.Should().Be(AgentToolStartupDiagnosticCodes.CapabilityResolutionFailure);
    }

    [Fact]
    public void Build_RejectsExpectedCapabilityHashMismatch()
    {
        var capability = AgentToolRuntimeTestFixture.Capability("capability");
        var tool = AgentToolRuntimeTestFixture.Tool(
            "hash-tool",
            capability.Id,
            "hash.tool",
            expectedCapabilityHash: "wrong");
        AgentToolRuntimeTestFixture.RegisterNoPayloadBinding(tool);

        var action = () => AgentToolRuntimeTestFixture.SnapshotBuilder(
                AgentToolRuntimeTestFixture.BuildToolRegistry(tool),
                AgentToolRuntimeTestFixture.BuildCapabilityRegistry(capability),
                AgentToolRuntimeTestFixture.BuildSchemaRegistry())
            .Build();

        action.Should().Throw<AgentToolConfigurationException>()
            .Which.Code.Should().Be(AgentToolStartupDiagnosticCodes.ExpectedContractHashMismatch);
    }

    [Fact]
    public void Build_CapturesExactSchemasGeneratedTypeInfoAndDiscoveryContracts()
    {
        var inputSchema = AgentToolRuntimeTestFixture.Schema("input-schema");
        var outputSchema = AgentToolRuntimeTestFixture.Schema("output-schema");
        var capability = AgentToolRuntimeTestFixture.Capability(
            "typed-capability",
            input: new(inputSchema.Id, inputSchema.Version),
            output: new(outputSchema.Id, outputSchema.Version));
        var tool = AgentToolRuntimeTestFixture.Tool("typed-tool", capability.Id, "typed.tool");
        AgentToolRuntimeTestFixture.RegisterDtoBinding(tool);

        var snapshot = AgentToolRuntimeTestFixture.SnapshotBuilder(
                AgentToolRuntimeTestFixture.BuildToolRegistry(tool),
                AgentToolRuntimeTestFixture.BuildCapabilityRegistry(capability),
                AgentToolRuntimeTestFixture.BuildSchemaRegistry(inputSchema, outputSchema))
            .Build();
        var entry = snapshot.Find(tool.ToolName)!;

        entry.Binding.InputTypeInfo!.Type.Should().Be(typeof(TestDto));
        entry.Binding.OutputTypeInfo!.Type.Should().Be(typeof(TestDto));
        entry.DiscoveryContract.InputSchemaContract!.Id.Should().Be(inputSchema.Id);
        entry.DiscoveryContract.OutputSchemaContract!.Id.Should().Be(outputSchema.Id);
        entry.AllowedAgentRoles.Should().BeEquivalentTo(new[] { "operator" });
    }

    [Fact]
    public void Build_RejectsReflectionJsonConfiguration()
    {
        var capability = AgentToolRuntimeTestFixture.Capability("capability");
        var tool = AgentToolRuntimeTestFixture.Tool("json-tool", capability.Id, "json.tool");
        AgentToolRuntimeTestFixture.RegisterNoPayloadBinding(tool);
        var reflectionOptions = new AgentToolJsonOptions();
        reflectionOptions.SerializerOptions.TypeInfoResolver =
            new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver();

        var action = () => AgentToolRuntimeTestFixture.SnapshotBuilder(
                AgentToolRuntimeTestFixture.BuildToolRegistry(tool),
                AgentToolRuntimeTestFixture.BuildCapabilityRegistry(capability),
                AgentToolRuntimeTestFixture.BuildSchemaRegistry(),
                reflectionOptions)
            .Build();

        action.Should().Throw<AgentToolConfigurationException>()
            .Which.Code.Should().Be(AgentToolStartupDiagnosticCodes.InvalidJsonConfiguration);
    }

    [Fact]
    public void Build_RejectsCommandWithoutExplicitWriteClassification()
    {
        var capability = AgentToolRuntimeTestFixture.Capability("command-capability");
        capability = new CrestCreates.Metadata.CapabilityDescriptor
        {
            Id = capability.Id,
            Name = capability.Name,
            Version = capability.Version,
            State = capability.State,
            CapabilityKind = CapabilityKind.Command,
            RiskLevel = CapabilityRiskLevel.Low
        };
        var tool = AgentToolRuntimeTestFixture.Tool("command-tool", capability.Id, "command.tool");
        AgentToolRuntimeTestFixture.RegisterNoPayloadBinding(tool);

        var action = () => AgentToolRuntimeTestFixture.SnapshotBuilder(
                AgentToolRuntimeTestFixture.BuildToolRegistry(tool),
                AgentToolRuntimeTestFixture.BuildCapabilityRegistry(capability),
                AgentToolRuntimeTestFixture.BuildSchemaRegistry())
            .Build();

        action.Should().Throw<AgentToolConfigurationException>()
            .Which.Code.Should().Be(AgentToolStartupDiagnosticCodes.InvalidSideEffectClassification);
    }

    private static AgentCapabilityToolDescriptor CopyWithCapability(
        AgentCapabilityToolDescriptor source,
        CapabilityProjectionReference capability)
        => new()
        {
            Id = source.Id,
            Name = source.Name,
            Version = source.Version,
            State = source.State,
            Capability = capability,
            ToolName = source.ToolName,
            Title = source.Title,
            Description = source.Description,
            SelectionPolicy = source.SelectionPolicy,
            SideEffectKind = source.SideEffectKind,
            RiskFloor = source.RiskFloor,
            ApprovalMode = source.ApprovalMode,
            Budget = source.Budget,
            AuditMode = source.AuditMode,
            AllowedAgentRoles = source.AllowedAgentRoles
        };
}
