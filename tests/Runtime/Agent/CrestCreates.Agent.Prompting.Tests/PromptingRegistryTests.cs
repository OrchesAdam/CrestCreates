using CrestCreates.Agent.Prompting;
using CrestCreates.Agent.Prompting.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Prompting.Tests;

public sealed class PromptingRegistryTests
{
    [Fact]
    public void Registry_StoresDescriptorMetadataOnly()
    {
        var registry = new InMemoryAgentPromptTemplateRegistry(new[]
        {
            new AgentPromptTemplateDescriptor
            {
                TemplateId = new AgentPromptTemplateId("descriptor-authoring"),
                Version = new AgentPromptVersion("v1"),
                Purpose = AgentPromptPurpose.DescriptorAuthoring,
                ContractVersion = new AgentPromptContractVersion("7h.v1"),
                Metadata = new Dictionary<string, string> { ["owner"] = "authoring" }
            }
        });

        var descriptor = registry.Find(new AgentPromptTemplateId("descriptor-authoring"), new AgentPromptVersion("v1"));

        descriptor.Should().NotBeNull();
        descriptor!.Metadata.Should().ContainKey("owner");
        typeof(AgentPromptTemplateDescriptor).GetProperties().Select(p => p.Name)
            .Should().NotContain(new[] { "TemplateBody", "PromptBody", "RenderedPrompt", "ExternalContent" });
    }

    [Fact]
    public void Find_ReturnsDefensiveCopy_MutationDoesNotAffectRegistry()
    {
        var original = new AgentPromptTemplateDescriptor
        {
            TemplateId = new AgentPromptTemplateId("test-template"),
            Version = new AgentPromptVersion("v1"),
            Purpose = AgentPromptPurpose.DescriptorAuthoring,
            ContractVersion = new AgentPromptContractVersion("7g.v1"),
            Metadata = new Dictionary<string, string> { ["key1"] = "value1" }
        };
        var registry = new InMemoryAgentPromptTemplateRegistry([original]);

        var found = registry.Find(new AgentPromptTemplateId("test-template"), new AgentPromptVersion("v1"));
        found.Should().NotBeNull();

        // Mutate returned metadata
        ((Dictionary<string, string>)found!.Metadata)["key1"] = "mutated";

        // Registry should be unaffected
        var foundAgain = registry.Find(new AgentPromptTemplateId("test-template"), new AgentPromptVersion("v1"));
        foundAgain!.Metadata["key1"].Should().Be("value1");
    }

    [Fact]
    public void List_ReturnsDefensiveCopies_MutationDoesNotAffectRegistry()
    {
        var original = new AgentPromptTemplateDescriptor
        {
            TemplateId = new AgentPromptTemplateId("test-template"),
            Version = new AgentPromptVersion("v1"),
            Purpose = AgentPromptPurpose.DescriptorAuthoring,
            ContractVersion = new AgentPromptContractVersion("7g.v1"),
            Metadata = new Dictionary<string, string> { ["key1"] = "value1" }
        };
        var registry = new InMemoryAgentPromptTemplateRegistry([original]);

        var listed = registry.List();
        listed.Should().HaveCount(1);

        // Mutate returned metadata
        ((Dictionary<string, string>)listed[0].Metadata)["key1"] = "mutated";

        // Registry should be unaffected
        var foundAgain = registry.Find(new AgentPromptTemplateId("test-template"), new AgentPromptVersion("v1"));
        foundAgain!.Metadata["key1"].Should().Be("value1");
    }
}
