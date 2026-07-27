using System.Collections;
using System.Text.Json;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;
using CrestCreates.Mcp.Abstractions;
using CrestCreates.Mcp.Memory.Json;
using CrestCreates.Mcp;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Mcp.Memory.Tests;

public sealed class McpMemoryJsonContractRootTests
{
    private static readonly Type[] s_expectedRoots =
    [
        typeof(RecallAgentContextInput),
        typeof(RecallAgentContextResult),
        typeof(BuildAgentMemoryPackInput),
        typeof(BuildAgentMemoryPackResult),
        typeof(ExpandAgentMemorySourceInput),
        typeof(ExpandAgentMemorySourceResult),
    ];

    [Fact]
    public void McpMemoryGeneratedRoots_MatchSpecsExactly()
    {
        var declaredRoots = typeof(McpMemoryTools).GetNestedTypes()
            .Select(type => type.GetCustomAttributes(typeof(McpToolSpecAttribute), inherit: false)
                .Cast<McpToolSpecAttribute>().Single())
            .SelectMany(attribute => new[] { attribute.InputType, attribute.OutputType })
            .Where(type => type is not null)
            .Cast<Type>()
            .ToHashSet();

        declaredRoots.Should().HaveCount(6).And.BeEquivalentTo(s_expectedRoots);
        McpMemoryJsonSerializerContext.McpMemoryJsonSerializerContextRootManifest.BindingRootTypes
            .Should().BeEquivalentTo(declaredRoots);
    }

    [Fact]
    public void McpMemoryContributor_UsesGeneratedBindingRootManifest()
    {
        var contributor = new McpMemoryJsonContextContributor();

        contributor.ContributorId.Should().Be("mcp-memory");
        contributor.BindingRootTypes.Should().BeSameAs(
            McpMemoryJsonSerializerContext.McpMemoryJsonSerializerContextRootManifest.BindingRootTypes);
        contributor.BindingRootTypes.Should().NotBeOfType<HashSet<Type>>();
        contributor.BindingRootTypes.Should().NotBeAssignableTo<IList>();
        var mutableView = contributor.BindingRootTypes.Should().BeAssignableTo<ISet<Type>>().Subject;
        var mutate = () => mutableView.Add(typeof(McpMemoryJsonContractRootTests));
        mutate.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void McpBindingKeys_RemainUnchanged_AndEveryRootHasExactlyOneOwner()
    {
        var contributor = new McpMemoryJsonContextContributor();
        var builder = new McpJsonContextBuilder();
        contributor.Contribute(builder);

        var result = builder.Build();
        result.Bindings.Keys.Should().BeEquivalentTo(
            "ctx_recall_input",
            "ctx_recall_output",
            "memory_recall_input",
            "memory_recall_output",
            "ctx_expand_input",
            "ctx_expand_output",
            "memory_source_expand_input",
            "memory_source_expand_output");
        result.BindingRootOwnership.Keys.Should().BeEquivalentTo(s_expectedRoots);
        result.BindingRootOwnership.Values.Should().OnlyContain(owner => owner == "mcp-memory");

        foreach (var root in s_expectedRoots)
            McpMemoryJsonSerializerContext.Default.GetTypeInfo(root).Should().NotBeNull(root.FullName);
    }

    [Fact]
    public void RepresentativeMcpPayloads_RoundTrip_WithoutWireShapeDrift()
    {
        var input = new RecallAgentContextInput
        {
            ContextHandle = "context-1",
            MaximumBlockCount = 3,
            CharacterBudget = 2048,
            StartBlockIndex = 1,
            EndBlockIndexExclusive = 3,
        };

        var json = JsonSerializer.Serialize(
            input,
            McpMemoryJsonSerializerContext.Default.RecallAgentContextInput);
        json.Should().Be(
            "{\"ContextHandle\":\"context-1\",\"MaximumBlockCount\":3,\"CharacterBudget\":2048,\"StartBlockIndex\":1,\"EndBlockIndexExclusive\":3}");

        var roundTrip = JsonSerializer.Deserialize(
            json,
            McpMemoryJsonSerializerContext.Default.RecallAgentContextInput);
        roundTrip.Should().BeEquivalentTo(input);
    }
}
