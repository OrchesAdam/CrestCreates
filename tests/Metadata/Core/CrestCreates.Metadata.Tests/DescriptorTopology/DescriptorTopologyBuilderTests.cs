using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.DescriptorTopology;
using CrestCreates.Form.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Metadata.Tests.DescriptorTopology;

public class DescriptorTopologyBuilderTests
{
    private readonly ICanonicalHashComputer _hashComputer = new DefaultCanonicalHashComputer();
    private readonly IDescriptorStableHashBuilder _hashBuilder;

    public DescriptorTopologyBuilderTests()
    {
        _hashBuilder = new DescriptorStableHashBuilder(_hashComputer);
    }
    [Fact]
    public void Build_Empty_Input_Produces_Empty_Snapshot()
    {
        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        var builder = new DescriptorTopologyBuilder(mockProvider.Object, _hashBuilder);

        var snapshot = builder.Build(Array.Empty<IDescriptor>());

        snapshot.NodeCount.Should().Be(0);
        snapshot.EdgeCount.Should().Be(0);
        snapshot.Nodes.Should().BeEmpty();
        snapshot.Edges.Should().BeEmpty();
        snapshot.Diagnostics.All.Should().BeEmpty();
    }

    [Fact]
    public void Build_Creates_Nodes_For_All_Provided_Descriptors()
    {
        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(It.IsAny<IDescriptor>())).Returns(Array.Empty<CrestCreates.Metadata.Abstractions.DescriptorRelationship.DescriptorRelationship>());
        var builder = new DescriptorTopologyBuilder(mockProvider.Object, _hashBuilder);

        var schemaDesc = CreateConcreteDescriptor("schema", "User", "User Schema", DescriptorKind.Schema);
        var capDesc = CreateConcreteDescriptor("capability", "CreateUser", "Create User", DescriptorKind.Capability);

        var snapshot = builder.Build(new[] { schemaDesc, capDesc });

        snapshot.NodeCount.Should().Be(2);
        snapshot.Contains(new DescriptorRef("schema", "User", null)).Should().BeTrue();
        snapshot.Contains(new DescriptorRef("capability", "CreateUser", null)).Should().BeTrue();
    }

    [Fact]
    public void Build_Node_Has_Correct_Summary_Properties()
    {
        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(It.IsAny<IDescriptor>())).Returns(Array.Empty<CrestCreates.Metadata.Abstractions.DescriptorRelationship.DescriptorRelationship>());
        var builder = new DescriptorTopologyBuilder(mockProvider.Object, _hashBuilder);

        var desc = CreateConcreteDescriptor("schema", "User", "User Schema", DescriptorKind.Schema,
            state: DescriptorState.Active, supersededById: null);
        var expectedHashes = _hashBuilder.Build(desc);

        var snapshot = builder.Build(new[] { desc });

        var node = snapshot.FindNode(new DescriptorRef("schema", "User", null));
        node.Should().NotBeNull();
        node!.Kind.Should().Be(DescriptorKind.Schema);
        node.Name.Should().Be("User Schema");
        node.State.Should().Be(DescriptorState.Active);
        node.ContractHash.Should().Be(expectedHashes.ContractHash.Value);
        node.SupersededById.Should().BeNull();
        node.OutgoingEdgeIndices.Should().BeEmpty();
        node.IncomingEdgeIndices.Should().BeEmpty();
    }

    [Fact]
    public void Build_Extracts_Edges_From_RelationshipProvider()
    {
        var schemaRef = new DescriptorRef("schema", "User", null);
        var formRef = new DescriptorRef("form", "UserForm", null);

        var relationships = new List<CrestCreates.Metadata.Abstractions.DescriptorRelationship.DescriptorRelationship>
        {
            new(formRef, schemaRef, RelationshipKind.Uses, "Schema", "Schema", RelationshipStrength.Strong, false)
        };

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        var schemaDesc = CreateConcreteDescriptor("schema", "User", "User Schema", DescriptorKind.Schema);
        var formDesc = CreateConcreteDescriptor("form", "UserForm", "User Form", DescriptorKind.Form);

        mockProvider
            .Setup(p => p.GetRelationships(formDesc))
            .Returns(relationships);
        mockProvider
            .Setup(p => p.GetRelationships(schemaDesc))
            .Returns(Array.Empty<CrestCreates.Metadata.Abstractions.DescriptorRelationship.DescriptorRelationship>());

        var builder = new DescriptorTopologyBuilder(mockProvider.Object, _hashBuilder);
        var snapshot = builder.Build(new IDescriptor[] { schemaDesc, formDesc });

        snapshot.EdgeCount.Should().Be(1);
        var edge = snapshot.Edges[0];
        edge.Index.Should().Be(0);
        edge.From.Should().Be(formRef);
        edge.To.Should().Be(schemaRef);
        edge.Kind.Should().Be(RelationshipKind.Uses);
        edge.Role.Should().Be("Schema");
        edge.Strength.Should().Be(RelationshipStrength.Strong);
    }

    [Fact]
    public void Build_Edge_Indices_Populated_On_Nodes()
    {
        var schemaRef = new DescriptorRef("schema", "User", null);
        var formRef = new DescriptorRef("form", "UserForm", null);

        var relationships = new List<CrestCreates.Metadata.Abstractions.DescriptorRelationship.DescriptorRelationship>
        {
            new(formRef, schemaRef, RelationshipKind.Uses, "Schema", "Schema", RelationshipStrength.Strong, false)
        };

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        var schemaDesc = CreateConcreteDescriptor("schema", "User", "User Schema", DescriptorKind.Schema);
        var formDesc = CreateConcreteDescriptor("form", "UserForm", "User Form", DescriptorKind.Form);

        mockProvider.Setup(p => p.GetRelationships(formDesc)).Returns(relationships);
        mockProvider.Setup(p => p.GetRelationships(schemaDesc)).Returns(Array.Empty<CrestCreates.Metadata.Abstractions.DescriptorRelationship.DescriptorRelationship>());

        var builder = new DescriptorTopologyBuilder(mockProvider.Object, _hashBuilder);
        var snapshot = builder.Build(new IDescriptor[] { schemaDesc, formDesc });

        var formNode = snapshot.FindNode(formRef)!;
        var schemaNode = snapshot.FindNode(schemaRef)!;

        formNode.OutgoingEdgeIndices.Should().BeEquivalentTo(new[] { 0 });
        formNode.IncomingEdgeIndices.Should().BeEmpty();
        schemaNode.OutgoingEdgeIndices.Should().BeEmpty();
        schemaNode.IncomingEdgeIndices.Should().BeEquivalentTo(new[] { 0 });
    }

    [Fact]
    public void Build_Edge_To_Unknown_Target_Still_Created()
    {
        var formRef = new DescriptorRef("form", "UserForm", null);
        var missingRef = new DescriptorRef("schema", "MissingSchema", null);

        var relationships = new List<CrestCreates.Metadata.Abstractions.DescriptorRelationship.DescriptorRelationship>
        {
            new(formRef, missingRef, RelationshipKind.Uses, "Schema", "Schema", RelationshipStrength.Strong, false)
        };

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        var formDesc = CreateConcreteDescriptor("form", "UserForm", "User Form", DescriptorKind.Form);
        mockProvider.Setup(p => p.GetRelationships(formDesc)).Returns(relationships);

        var builder = new DescriptorTopologyBuilder(mockProvider.Object, _hashBuilder);
        var snapshot = builder.Build(new IDescriptor[] { formDesc });

        snapshot.EdgeCount.Should().Be(1);
        snapshot.Edges[0].To.Should().Be(missingRef);
    }

    [Fact]
    public void Build_ConsumerIndex_NullVersion_Returns_All()
    {
        var target = new DescriptorRef("schema", "User", null);
        var c1 = CreateConcreteDescriptor("capability", "CreateUser", "CreateUser", DescriptorKind.Capability);
        var c2 = CreateConcreteDescriptor("form", "UserForm", "UserForm", DescriptorKind.Form);
        var targetDesc = CreateConcreteDescriptor("schema", "User", "User", DescriptorKind.Schema);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(c1)).Returns(new CrestCreates.Metadata.Abstractions.DescriptorRelationship.DescriptorRelationship[]
        {
            new(
                new DescriptorRef("capability", "CreateUser", null), target, RelationshipKind.Uses, null, null, RelationshipStrength.Strong, false)
        });
        mockProvider.Setup(p => p.GetRelationships(c2)).Returns(new CrestCreates.Metadata.Abstractions.DescriptorRelationship.DescriptorRelationship[]
        {
            new(
                new DescriptorRef("form", "UserForm", null), target, RelationshipKind.Uses, null, null, RelationshipStrength.Strong, false)
        });
        mockProvider.Setup(p => p.GetRelationships(targetDesc)).Returns(Array.Empty<CrestCreates.Metadata.Abstractions.DescriptorRelationship.DescriptorRelationship>());

        var builder = new DescriptorTopologyBuilder(mockProvider.Object, _hashBuilder);
        var snapshot = builder.Build(new IDescriptor[] { targetDesc, c1, c2 });

        var consumers = snapshot.GetConsumers("schema", "User");
        consumers.Should().HaveCount(2);
        consumers.Select(n => n.Ref.Id).Should().BeEquivalentTo(new[] { "CreateUser", "UserForm" });
    }

    [Fact]
    public void Build_ConsumerIndex_ExactVersion_Returns_Exact_Plus_Unpinned()
    {
        var targetV2 = new DescriptorRef("schema", "User", 2);
        var cv1 = CreateConcreteDescriptor("capability", "ExactV1", "EV1", DescriptorKind.Capability);
        var cv2 = CreateConcreteDescriptor("capability", "ExactV2", "EV2", DescriptorKind.Capability);
        var cUnpinned = CreateConcreteDescriptor("form", "Unpinned", "UP", DescriptorKind.Form);
        var targetDesc = CreateConcreteDescriptor("schema", "User", "User", DescriptorKind.Schema);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(cv1)).Returns(new CrestCreates.Metadata.Abstractions.DescriptorRelationship.DescriptorRelationship[]
        {
            new(new DescriptorRef("capability", "ExactV1", null), new DescriptorRef("schema", "User", 1), RelationshipKind.Uses, null, null, RelationshipStrength.Strong, false)
        });
        mockProvider.Setup(p => p.GetRelationships(cv2)).Returns(new CrestCreates.Metadata.Abstractions.DescriptorRelationship.DescriptorRelationship[]
        {
            new(new DescriptorRef("capability", "ExactV2", null), targetV2, RelationshipKind.Uses, null, null, RelationshipStrength.Strong, false)
        });
        mockProvider.Setup(p => p.GetRelationships(cUnpinned)).Returns(new CrestCreates.Metadata.Abstractions.DescriptorRelationship.DescriptorRelationship[]
        {
            new(new DescriptorRef("form", "Unpinned", null), new DescriptorRef("schema", "User", null), RelationshipKind.Uses, null, null, RelationshipStrength.Strong, false)
        });
        mockProvider.Setup(p => p.GetRelationships(targetDesc)).Returns(Array.Empty<CrestCreates.Metadata.Abstractions.DescriptorRelationship.DescriptorRelationship>());

        var builder = new DescriptorTopologyBuilder(mockProvider.Object, _hashBuilder);
        var snapshot = builder.Build(new IDescriptor[] { targetDesc, cv1, cv2, cUnpinned });

        var consumersV2 = snapshot.GetConsumers("schema", "User", version: 2);
        consumersV2.Should().HaveCount(2); // ExactV2 + Unpinned
        consumersV2.Select(n => n.Ref.Id).Should().BeEquivalentTo(new[] { "ExactV2", "Unpinned" });
    }

    [Fact]
    public void Build_ConsumerIndex_No_Match_Returns_Empty()
    {
        var desc = CreateConcreteDescriptor("schema", "User", "User", DescriptorKind.Schema);
        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(desc)).Returns(Array.Empty<CrestCreates.Metadata.Abstractions.DescriptorRelationship.DescriptorRelationship>());

        var builder = new DescriptorTopologyBuilder(mockProvider.Object, _hashBuilder);
        var snapshot = builder.Build(new IDescriptor[] { desc });

        snapshot.GetConsumers("schema", "NoSuch").Should().BeEmpty();
    }

    // Helper: create concrete descriptors instead of mocks for SG-generated hash dispatcher
    private static IDescriptor CreateConcreteDescriptor(
        string ns, string id, string name, DescriptorKind kind,
        DescriptorState state = DescriptorState.Active,
        string? supersededById = null)
    {
        return kind switch
        {
            DescriptorKind.Schema => new SchemaDescriptor
            {
                Id = id, Name = name, Version = 0,
                State = state, SupersededById = supersededById,
                ChangeKind = SchemaChangeKind.Additive,
                Fields = [], References = [], ValidationRules = []
            },
            DescriptorKind.Capability => new CapabilityDescriptor
            {
                Id = id, Name = name, Version = 0,
                State = state, SupersededById = supersededById,
                CapabilityKind = CapabilityKind.Command
            },
            DescriptorKind.Form => new FormDescriptor
            {
                Id = id, Name = name, Version = 0,
                State = state, SupersededById = supersededById,
                Schema = default,
                Fields = [], LayoutColumns = null
            },
            _ => throw new ArgumentException($"Unsupported descriptor kind: {kind}", nameof(kind))
        };
    }
}
