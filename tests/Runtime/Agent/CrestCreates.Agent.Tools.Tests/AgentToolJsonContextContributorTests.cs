using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Tools.Tests;

public sealed class AgentToolJsonContextContributorTests
{
    [Fact]
    public void Contributor_context_uses_the_single_shared_options_instance()
    {
        var capability = AgentToolRuntimeTestFixture.Capability("contributor-capability");
        var tool = AgentToolRuntimeTestFixture.Tool("contributor-tool", capability.Id, "contributor.tool");
        AgentToolRuntimeTestFixture.RegisterNoPayloadBinding(tool);
        var options = new AgentToolJsonOptions();
        options.ContextContributors.Add(new TestContributor("test", 10));

        var snapshot = AgentToolRuntimeTestFixture.SnapshotBuilder(
                AgentToolRuntimeTestFixture.BuildToolRegistry(tool),
                AgentToolRuntimeTestFixture.BuildCapabilityRegistry(capability),
                AgentToolRuntimeTestFixture.BuildSchemaRegistry(),
                options)
            .Build();

        snapshot.Find(tool.ToolName).Should().NotBeNull();
    }

    [Fact]
    public void Duplicate_contributor_id_fails_startup()
    {
        var capability = AgentToolRuntimeTestFixture.Capability("duplicate-contributor-capability");
        var tool = AgentToolRuntimeTestFixture.Tool("duplicate-contributor-tool", capability.Id, "duplicate.contributor.tool");
        AgentToolRuntimeTestFixture.RegisterNoPayloadBinding(tool);
        var options = new AgentToolJsonOptions();
        options.ContextContributors.Add(new TestContributor("same", 1));
        options.ContextContributors.Add(new TestContributor("same", 2));

        var action = () => AgentToolRuntimeTestFixture.SnapshotBuilder(
                AgentToolRuntimeTestFixture.BuildToolRegistry(tool),
                AgentToolRuntimeTestFixture.BuildCapabilityRegistry(capability),
                AgentToolRuntimeTestFixture.BuildSchemaRegistry(), options).Build();

        action.Should().Throw<AgentToolConfigurationException>()
            .Which.Code.Should().Be(AgentToolStartupDiagnosticCodes.DuplicateJsonContributor);
    }

    [Fact]
    public void Duplicate_binding_root_owner_fails_startup()
    {
        var capability = AgentToolRuntimeTestFixture.Capability("duplicate-root-capability");
        var tool = AgentToolRuntimeTestFixture.Tool("duplicate-root-tool", capability.Id, "duplicate.root.tool");
        AgentToolRuntimeTestFixture.RegisterNoPayloadBinding(tool);
        var options = new AgentToolJsonOptions();
        options.ContextContributors.Add(new TestContributor("first", 1));
        options.ContextContributors.Add(new TestContributor("second", 2));

        var action = () => AgentToolRuntimeTestFixture.SnapshotBuilder(
                AgentToolRuntimeTestFixture.BuildToolRegistry(tool),
                AgentToolRuntimeTestFixture.BuildCapabilityRegistry(capability),
                AgentToolRuntimeTestFixture.BuildSchemaRegistry(), options).Build();

        action.Should().Throw<AgentToolConfigurationException>()
            .Which.Code.Should().Be(AgentToolStartupDiagnosticCodes.DuplicateJsonBindingRoot);
    }

    private sealed class TestContributor(string id, int order) : IAgentToolJsonContextContributor
    {
        public string Id { get; } = id;
        public int Order { get; } = order;
        public IReadOnlyCollection<Type> BindingRootTypes { get; } = [typeof(TestDto)];

        public JsonSerializerContext Create(JsonSerializerOptions sharedOptions)
            => new AgentToolTestJsonContext(sharedOptions);
    }
}
