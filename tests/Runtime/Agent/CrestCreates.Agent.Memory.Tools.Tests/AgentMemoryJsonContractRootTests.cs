using System.Collections;
using System.Text.Json;
using CrestCreates.Agent.Tools;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.Memory.Tools.Tests;

public sealed class AgentMemoryJsonContractRootTests
{
    private static readonly Type[] s_expectedRoots =
    [
        typeof(BuildAgentMemoryPackInput),
        typeof(ExpandAgentMemorySourceInput),
        typeof(CompressAgentHistoryInput),
        typeof(ExtractMemoryCandidatesInput),
        typeof(PromoteMemoryCandidateInput),
        typeof(RejectMemoryCandidateInput),
        typeof(SupersedeMemoryItemInput),
        typeof(BuildAgentMemoryPackResult),
        typeof(ExpandAgentMemorySourceResult),
        typeof(CompressAgentHistoryResult),
        typeof(ExtractMemoryCandidatesResult),
        typeof(PromoteMemoryCandidateResult),
        typeof(RejectMemoryCandidateResult),
        typeof(SupersedeMemoryItemResult),
    ];

    [Fact]
    public void AgentMemoryGeneratedRoots_MatchSpecsExactly()
    {
        var declaredRoots = typeof(AgentMemoryToolSpecifications).GetNestedTypes()
            .Select(type => type.GetCustomAttributes(typeof(AgentToolSpecAttribute), inherit: false)
                .Cast<AgentToolSpecAttribute>().Single())
            .SelectMany(attribute => new[] { attribute.InputType, attribute.OutputType })
            .Where(type => type is not null)
            .Cast<Type>()
            .ToHashSet();

        declaredRoots.Should().HaveCount(14).And.BeEquivalentTo(s_expectedRoots);
        AgentMemoryToolJsonSerializerContext.AgentMemoryToolJsonSerializerContextRootManifest.BindingRootTypes
            .Should().BeEquivalentTo(declaredRoots);
    }

    [Fact]
    public void AgentMemoryPublicManifest_IsConsumableAndImmutable()
    {
        var roots = AgentMemoryToolJsonSerializerContext.AgentMemoryToolJsonSerializerContextRootManifest.BindingRootTypes;

        roots.Should().NotBeOfType<HashSet<Type>>();
        roots.Should().NotBeAssignableTo<IList>();
        var mutableView = roots.Should().BeAssignableTo<ISet<Type>>().Subject;
        var mutate = () => mutableView.Add(typeof(AgentMemoryJsonContractRootTests));
        mutate.Should().Throw<NotSupportedException>();
        roots.Should().BeEquivalentTo(s_expectedRoots);
        roots.Should().BeSameAs(
            AgentMemoryToolJsonSerializerContext.AgentMemoryToolJsonSerializerContextRootManifest.BindingRootTypes);
    }

    [Fact]
    public void EveryGeneratedBindingRoot_HasJsonTypeInfo()
    {
        foreach (var root in s_expectedRoots)
            AgentMemoryToolJsonSerializerContext.Default.GetTypeInfo(root).Should().NotBeNull(root.FullName);

        AgentMemoryToolJsonSerializerContext.Default.GetTypeInfo(typeof(AgentMemoryToolConfidence))
            .Should().NotBeNull("nested enum metadata remains STJ-owned transitive closure");
    }

    [Fact]
    public void RepresentativeAgentToolPayloads_RoundTrip_WithoutWireShapeDrift()
    {
        var input = new BuildAgentMemoryPackInput
        {
            MemoryHandles = ["memory-1"],
            Kinds = [],
            Tags = ["important"],
            MaximumCount = 4,
            CharacterBudget = 1024,
            MinimumConfidence = AgentMemoryToolConfidence.High,
        };

        var json = JsonSerializer.Serialize(
            input,
            AgentMemoryToolJsonSerializerContext.Default.BuildAgentMemoryPackInput);
        json.Should().Be(
            "{\"MemoryHandles\":[\"memory-1\"],\"Kinds\":[],\"Tags\":[\"important\"],\"MaximumCount\":4,\"CharacterBudget\":1024,\"MinimumConfidence\":\"high\"}");

        var roundTrip = JsonSerializer.Deserialize(
            json,
            AgentMemoryToolJsonSerializerContext.Default.BuildAgentMemoryPackInput);
        roundTrip.Should().NotBeNull();
        roundTrip!.MemoryHandles.Should().Equal("memory-1");
        roundTrip.Tags.Should().Equal("important");
        roundTrip.MinimumConfidence.Should().Be(AgentMemoryToolConfidence.High);
    }

    [Fact]
    public void AgentMemoryContributor_IdOrderModuleIdRemainUnchanged_AndUsesManifest()
    {
        var contributorType = typeof(AgentMemoryToolServiceCollectionExtensions).Assembly
            .GetType("CrestCreates.Agent.Memory.Tools.AgentMemoryToolJsonContextContributor", throwOnError: true)!;
        var contributor = (IAgentToolJsonContextContributor)Activator.CreateInstance(contributorType, nonPublic: true)!;

        contributor.Id.Should().Be("agent-memory-tools");
        contributor.Order.Should().Be(200);
        contributor.ModuleId.Should().Be("agent-memory-tools");
        contributor.BindingRootTypes.Should().BeSameAs(
            AgentMemoryToolJsonSerializerContext.AgentMemoryToolJsonSerializerContextRootManifest.BindingRootTypes);
        contributor.Create(new JsonSerializerOptions()).Should().BeOfType<AgentMemoryToolJsonSerializerContext>();
    }

    [Fact]
    public void AgentMemoryMovedDeclarations_RemainBinaryTypeForwardedFromRuntimeAssembly()
    {
        Type.GetType(
                "CrestCreates.Agent.Memory.Tools.AgentMemoryToolCapabilityIds, CrestCreates.Agent.Memory.Tools",
                throwOnError: true)
            .Should().Be(typeof(AgentMemoryToolCapabilityIds));
        Type.GetType(
                "CrestCreates.Agent.Memory.Tools.AgentMemoryToolSpecifications, CrestCreates.Agent.Memory.Tools",
                throwOnError: true)
            .Should().Be(typeof(AgentMemoryToolSpecifications));
        Type.GetType(
                "CrestCreates.Agent.Memory.Tools.AgentMemoryToolSpecifications+BuildPack, CrestCreates.Agent.Memory.Tools",
                throwOnError: true)
            .Should().Be(typeof(AgentMemoryToolSpecifications.BuildPack));
    }
}
