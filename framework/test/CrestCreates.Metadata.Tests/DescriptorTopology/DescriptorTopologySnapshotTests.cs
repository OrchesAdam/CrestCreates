using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests.DescriptorTopology;

public class DescriptorTopologySnapshotTests
{
    // Helper: build a minimal snapshot from raw data
    private static DescriptorTopologySnapshot CreateSnapshot(
        (DescriptorRef Ref, DescriptorKind Kind, string Name)[] nodeDefs,
        (int Index, DescriptorRef From, DescriptorRef To, RelationshipKind Kind,
         string? Role, RelationshipStrength Strength)[] edgeDefs)
    {
        var nodes = new Dictionary<DescriptorRef, DescriptorNode>();
        foreach (var def in nodeDefs)
        {
            nodes[def.Ref] = new DescriptorNode
            {
                Ref = def.Ref,
                Kind = def.Kind,
                Name = def.Name,
                State = DescriptorState.Active,
                OutgoingEdgeIndices = new HashSet<int>(),
                IncomingEdgeIndices = new HashSet<int>()
            };
        }

        var edges = new List<DescriptorEdge>();
        foreach (var def in edgeDefs)
        {
            var edge = new DescriptorEdge
            {
                Index = def.Index,
                From = def.From,
                To = def.To,
                Kind = def.Kind,
                Role = def.Role,
                SourcePath = null,
                Strength = def.Strength,
                IsRuntimeBinding = false
            };
            edges.Add(edge);

            if (nodes.TryGetValue(def.From, out var fromNode))
                ((HashSet<int>)fromNode.OutgoingEdgeIndices).Add(def.Index);
            if (nodes.TryGetValue(def.To, out var toNode))
                ((HashSet<int>)toNode.IncomingEdgeIndices).Add(def.Index);
        }

        // Freeze
        foreach (var key in nodes.Keys.ToList())
        {
            nodes[key] = nodes[key] with
            {
                OutgoingEdgeIndices = nodes[key].OutgoingEdgeIndices.ToHashSet(),
                IncomingEdgeIndices = nodes[key].IncomingEdgeIndices.ToHashSet()
            };
        }

        var diagnostics = new DescriptorTopologyDiagnostics { All = Array.Empty<DescriptorTopologyDiagnostic>() };
        return new DescriptorTopologySnapshot(
            nodes, edges, diagnostics,
            new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>(),
            new Dictionary<(DescriptorIdentity, int), List<(DescriptorRef, DescriptorEdge)>>(),
            new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>(),
            DateTimeOffset.UtcNow);
    }

    [Fact]
    public void GetDirectDependencies_Returns_Outgoing()
    {
        var a = new DescriptorRef("ns", "A", null);
        var b = new DescriptorRef("ns", "B", null);
        var c = new DescriptorRef("ns", "C", null);

        var snapshot = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Capability, "B"), (c, DescriptorKind.Capability, "C") },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength)[]
            {
                (0, a, b, RelationshipKind.Uses, null, RelationshipStrength.Strong),
                (1, a, c, RelationshipKind.Uses, null, RelationshipStrength.Strong),
            });

        var deps = snapshot.GetDirectDependencies(a);
        deps.Should().HaveCount(2);
        deps.Select(n => n.Ref).Should().BeEquivalentTo(new[] { b, c });
    }

    [Fact]
    public void GetDirectDependents_Returns_Incoming()
    {
        var a = new DescriptorRef("ns", "A", null);
        var b = new DescriptorRef("ns", "B", null);
        var c = new DescriptorRef("ns", "C", null);

        var snapshot = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Capability, "B"), (c, DescriptorKind.Capability, "C") },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength)[]
            {
                (0, a, c, RelationshipKind.Uses, null, RelationshipStrength.Strong),
                (1, b, c, RelationshipKind.Uses, null, RelationshipStrength.Strong),
            });

        var deps = snapshot.GetDirectDependents(c);
        deps.Should().HaveCount(2);
        deps.Select(n => n.Ref).Should().BeEquivalentTo(new[] { a, b });
    }

    [Fact]
    public void GetDirectDependencies_Skips_Missing_Target()
    {
        var a = new DescriptorRef("ns", "A", null);
        var missing = new DescriptorRef("ns", "Missing", null);

        var snapshot = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A") },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength)[]
            {
                (0, a, missing, RelationshipKind.Uses, null, RelationshipStrength.Strong),
            });

        var deps = snapshot.GetDirectDependencies(a);
        deps.Should().BeEmpty(); // Missing target → silently skipped
    }

    [Fact]
    public void GetTransitiveDependencies_Defaults_Strong_Only()
    {
        var a = new DescriptorRef("ns", "A", null);
        var b = new DescriptorRef("ns", "B", null);
        var c = new DescriptorRef("ns", "C", null);

        var snapshot = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Capability, "B"), (c, DescriptorKind.Capability, "C") },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength)[]
            {
                (0, a, b, RelationshipKind.Uses, null, RelationshipStrength.Strong),
                (1, b, c, RelationshipKind.References, null, RelationshipStrength.Weak),
            });

        var deps = snapshot.GetTransitiveDependencies(a);
        deps.Select(n => n.Ref).Should().BeEquivalentTo(new[] { b }); // Only B, not C (Weak edge)
    }

    [Fact]
    public void GetTransitiveDependencies_IncludeWeak()
    {
        var a = new DescriptorRef("ns", "A", null);
        var b = new DescriptorRef("ns", "B", null);
        var c = new DescriptorRef("ns", "C", null);

        var snapshot = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Capability, "B"), (c, DescriptorKind.Capability, "C") },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength)[]
            {
                (0, a, b, RelationshipKind.Uses, null, RelationshipStrength.Strong),
                (1, b, c, RelationshipKind.References, null, RelationshipStrength.Weak),
            });

        var deps = snapshot.GetTransitiveDependencies(a, includeWeak: true);
        deps.Select(n => n.Ref).Should().BeEquivalentTo(new[] { b, c });
    }

    [Fact]
    public void GetTransitiveDependents_Direction_Correct()
    {
        var a = new DescriptorRef("ns", "A", null);
        var b = new DescriptorRef("ns", "B", null);
        var c = new DescriptorRef("ns", "C", null);

        var snapshot = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Capability, "B"), (c, DescriptorKind.Capability, "C") },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength)[]
            {
                (0, a, b, RelationshipKind.Uses, null, RelationshipStrength.Strong),
                (1, b, c, RelationshipKind.Uses, null, RelationshipStrength.Strong),
            });

        var deps = snapshot.GetTransitiveDependents(c);
        deps.Select(n => n.Ref).Should().BeEquivalentTo(new[] { b, a }); // reversed: C ← B ← A
    }

    [Fact]
    public void Transitive_Cycle_Safe()
    {
        var a = new DescriptorRef("ns", "A", null);
        var b = new DescriptorRef("ns", "B", null);

        var snapshot = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Capability, "B") },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength)[]
            {
                (0, a, b, RelationshipKind.Uses, null, RelationshipStrength.Strong),
                (1, b, a, RelationshipKind.Uses, null, RelationshipStrength.Strong),
            });

        var deps = snapshot.GetTransitiveDependencies(a);
        deps.Select(n => n.Ref).Should().BeEquivalentTo(new[] { b }); // Cycle terminates
    }

    [Fact]
    public void GetDirectDependencies_Unknown_Node_Returns_Empty()
    {
        var a = new DescriptorRef("ns", "A", null);
        var unknown = new DescriptorRef("ns", "Unknown", null);

        var snapshot = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A") },
            Array.Empty<(int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength)>());

        var deps = snapshot.GetDirectDependencies(unknown);
        deps.Should().BeEmpty();
    }

    [Fact]
    public void GetDirectDependents_Unknown_Node_Returns_Empty()
    {
        var a = new DescriptorRef("ns", "A", null);
        var unknown = new DescriptorRef("ns", "Unknown", null);

        var snapshot = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A") },
            Array.Empty<(int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength)>());

        var deps = snapshot.GetDirectDependents(unknown);
        deps.Should().BeEmpty();
    }

    [Fact]
    public void GetTransitiveDependencies_Unknown_Node_Returns_Empty()
    {
        var a = new DescriptorRef("ns", "A", null);
        var unknown = new DescriptorRef("ns", "Unknown", null);

        var snapshot = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A") },
            Array.Empty<(int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength)>());

        var deps = snapshot.GetTransitiveDependencies(unknown);
        deps.Should().BeEmpty();
    }

    [Fact]
    public void GetTransitiveDependents_Unknown_Node_Returns_Empty()
    {
        var a = new DescriptorRef("ns", "A", null);
        var unknown = new DescriptorRef("ns", "Unknown", null);

        var snapshot = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A") },
            Array.Empty<(int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength)>());

        var deps = snapshot.GetTransitiveDependents(unknown);
        deps.Should().BeEmpty();
    }

    [Fact]
    public void GetDirectDependencies_With_Unpinned_Edge_Resolves_Versioned_Target()
    {
        var schemaV2 = new DescriptorRef("schema", "User", 2);
        var form = new DescriptorRef("form", "UserForm", null);

        var snapshot = CreateSnapshot(
            new[] { (schemaV2, DescriptorKind.Schema, "User"), (form, DescriptorKind.Form, "UserForm") },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength)[]
            {
                (0, form, new DescriptorRef("schema", "User", null), RelationshipKind.Uses, "Schema", RelationshipStrength.Strong),
            });

        var deps = snapshot.GetDirectDependencies(form);
        deps.Should().HaveCount(1);
        deps[0].Ref.Should().Be(schemaV2);
    }

    [Fact]
    public void GetTransitiveDependencies_With_Unpinned_Edges_Follows_Chain()
    {
        var a = new DescriptorRef("capability", "A", 1);
        var b = new DescriptorRef("capability", "B", 3);
        var c = new DescriptorRef("schema", "C", null);

        var snapshot = CreateSnapshot(
            new[] { (a, DescriptorKind.Capability, "A"), (b, DescriptorKind.Capability, "B"), (c, DescriptorKind.Schema, "C") },
            new (int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength)[]
            {
                (0, a, new DescriptorRef("capability", "B", null), RelationshipKind.Uses, null, RelationshipStrength.Strong),
                (1, b, new DescriptorRef("schema", "C", null), RelationshipKind.Uses, null, RelationshipStrength.Strong),
            });

        var deps = snapshot.GetTransitiveDependencies(a);
        deps.Select(n => n.Ref).Should().BeEquivalentTo(new[] { b, c });
    }

    [Fact]
    public void Contains_With_Unpinned_Ref_Matches_Versioned_Node()
    {
        var schemaV2 = new DescriptorRef("schema", "User", 2);
        var snapshot = CreateSnapshot(
            new[] { (schemaV2, DescriptorKind.Schema, "User") },
            Array.Empty<(int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength)>());

        snapshot.Contains(new DescriptorRef("schema", "User", null)).Should().BeTrue();
    }

    [Fact]
    public void FindNode_With_Unpinned_Ref_Returns_Versioned_Node()
    {
        var schemaV2 = new DescriptorRef("schema", "User", 2);
        var snapshot = CreateSnapshot(
            new[] { (schemaV2, DescriptorKind.Schema, "User") },
            Array.Empty<(int, DescriptorRef, DescriptorRef, RelationshipKind, string?, RelationshipStrength)>());

        var node = snapshot.FindNode(new DescriptorRef("schema", "User", null));
        node.Should().NotBeNull();
        node!.Ref.Should().Be(schemaV2);
    }
}
