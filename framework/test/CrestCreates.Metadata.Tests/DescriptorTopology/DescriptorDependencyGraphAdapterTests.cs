using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Metadata.Tests.DescriptorTopology;

public class DescriptorDependencyGraphAdapterTests
{
    [Fact]
    public void Adapter_GetDependencies_Maps_Correctly()
    {
        var a = new DescriptorRef("ns", "A", null);
        var b = new DescriptorRef("ns", "B", null);

        var mockBuilder = new Mock<IDescriptorTopologyBuilder>();
        var snapshot = CreateSimpleSnapshot(
            new[] { (a, "A"), (b, "B") },
            new[] { (a, b, RelationshipKind.Uses) });
        mockBuilder.Setup(b => b.Build(It.IsAny<IReadOnlyList<IDescriptor>>())).Returns(snapshot);

        var adapter = new DescriptorDependencyGraphAdapter(mockBuilder.Object, Array.Empty<IDescriptor>());
        var deps = adapter.GetDependencies("A");

        deps.Should().HaveCount(1);
        deps[0].SourceId.Should().Be("A");
        deps[0].TargetId.Should().Be("B");
        deps[0].Kind.Should().Be(DescriptorDependencyKind.Uses);
    }

    [Fact]
    public void Adapter_GetDependents_Maps_Correctly()
    {
        var a = new DescriptorRef("ns", "A", null);
        var b = new DescriptorRef("ns", "B", null);

        var mockBuilder = new Mock<IDescriptorTopologyBuilder>();
        var snapshot = CreateSimpleSnapshot(
            new[] { (a, "A"), (b, "B") },
            new[] { (a, b, RelationshipKind.Uses) });
        mockBuilder.Setup(b => b.Build(It.IsAny<IReadOnlyList<IDescriptor>>())).Returns(snapshot);

        var adapter = new DescriptorDependencyGraphAdapter(mockBuilder.Object, Array.Empty<IDescriptor>());
        var deps = adapter.GetDependents("B");

        deps.Should().HaveCount(1);
        deps[0].SourceId.Should().Be("A");
        deps[0].TargetId.Should().Be("B");
    }

    [Fact]
    public void Adapter_AddEdge_Throws_NotSupportedException()
    {
        var mockBuilder = new Mock<IDescriptorTopologyBuilder>();
        mockBuilder.Setup(b => b.Build(It.IsAny<IReadOnlyList<IDescriptor>>())).Returns(
            CreateSimpleSnapshot(
                Array.Empty<(DescriptorRef, string)>(),
                Array.Empty<(DescriptorRef, DescriptorRef, RelationshipKind)>()));

        var adapter = new DescriptorDependencyGraphAdapter(mockBuilder.Object, Array.Empty<IDescriptor>());
        var act = () => adapter.AddEdge("a", "b", DescriptorDependencyKind.Uses);
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Adapter_KindMapping_All_Six_Covered()
    {
        var a = new DescriptorRef("ns", "A", null);
        var b = new DescriptorRef("ns", "B", null);

        var testCases = new (RelationshipKind Rel, DescriptorDependencyKind Dep)[]
        {
            (RelationshipKind.Produces, DescriptorDependencyKind.Produces),
            (RelationshipKind.Consumes, DescriptorDependencyKind.Consumes),
            (RelationshipKind.DependsOn, DescriptorDependencyKind.References),
            (RelationshipKind.References, DescriptorDependencyKind.References),
            (RelationshipKind.Uses, DescriptorDependencyKind.Uses),
            (RelationshipKind.Triggers, DescriptorDependencyKind.Triggers),
        };

        foreach (var tc in testCases)
        {
            var mockBuilder = new Mock<IDescriptorTopologyBuilder>();
            var snapshot = CreateSimpleSnapshot(
                new[] { (a, "A"), (b, "B") },
                new[] { (a, b, tc.Rel) });
            mockBuilder.Setup(b => b.Build(It.IsAny<IReadOnlyList<IDescriptor>>())).Returns(snapshot);

            var adapter = new DescriptorDependencyGraphAdapter(mockBuilder.Object, Array.Empty<IDescriptor>());
            var deps = adapter.GetDependencies("A");
            deps[0].Kind.Should().Be(tc.Dep, $"RelationshipKind.{tc.Rel} should map to DescriptorDependencyKind.{tc.Dep}");
        }
    }

    // Helper
    private static DescriptorTopologySnapshot CreateSimpleSnapshot(
        (DescriptorRef Ref, string Name)[] nodeDefs,
        (DescriptorRef From, DescriptorRef To, RelationshipKind Kind)[] edgeDefs)
    {
        var nodes = new Dictionary<DescriptorRef, DescriptorNode>();
        foreach (var def in nodeDefs)
        {
            nodes[def.Ref] = new DescriptorNode
            {
                Ref = def.Ref, Kind = DescriptorKind.Capability, Name = def.Name,
                State = DescriptorState.Active,
                OutgoingEdgeIndices = new HashSet<int>(),
                IncomingEdgeIndices = new HashSet<int>()
            };
        }

        var edges = new List<DescriptorEdge>();
        for (int i = 0; i < edgeDefs.Length; i++)
        {
            var def = edgeDefs[i];
            var edge = new DescriptorEdge
            {
                Index = i, From = def.From, To = def.To, Kind = def.Kind,
                Role = null, SourcePath = null, Strength = RelationshipStrength.Strong,
                IsRuntimeBinding = false
            };
            edges.Add(edge);
            if (nodes.TryGetValue(def.From, out var fn))
                ((HashSet<int>)fn.OutgoingEdgeIndices).Add(i);
            if (nodes.TryGetValue(def.To, out var tn))
                ((HashSet<int>)tn.IncomingEdgeIndices).Add(i);
        }

        foreach (var key in nodes.Keys.ToList())
        {
            nodes[key] = nodes[key] with
            {
                OutgoingEdgeIndices = nodes[key].OutgoingEdgeIndices.ToHashSet(),
                IncomingEdgeIndices = nodes[key].IncomingEdgeIndices.ToHashSet()
            };
        }

        return new DescriptorTopologySnapshot(
            nodes, edges,
            new DescriptorTopologyDiagnostics { All = Array.Empty<DescriptorTopologyDiagnostic>() },
            new(), new(), new(), DateTimeOffset.UtcNow);
    }
}
