using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Metadata.Tests.DescriptorTopology;

public class DescriptorTopologyBuilderTests
{
    [Fact]
    public void Build_Empty_Input_Produces_Empty_Snapshot()
    {
        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        var builder = new DescriptorTopologyBuilder(mockProvider.Object);

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
        mockProvider.Setup(p => p.GetRelationships(It.IsAny<IDescriptor>())).Returns(Array.Empty<DescriptorRelationship>());
        var builder = new DescriptorTopologyBuilder(mockProvider.Object);

        var schemaDesc = CreateMockDescriptor("schema", "User", "User Schema", DescriptorKind.Schema);
        var capDesc = CreateMockDescriptor("capability", "CreateUser", "Create User", DescriptorKind.Capability);

        var snapshot = builder.Build(new[] { schemaDesc, capDesc });

        snapshot.NodeCount.Should().Be(2);
        snapshot.Contains(new DescriptorRef("schema", "User", null)).Should().BeTrue();
        snapshot.Contains(new DescriptorRef("capability", "CreateUser", null)).Should().BeTrue();
    }

    [Fact]
    public void Build_Node_Has_Correct_Summary_Properties()
    {
        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(It.IsAny<IDescriptor>())).Returns(Array.Empty<DescriptorRelationship>());
        var builder = new DescriptorTopologyBuilder(mockProvider.Object);

        var desc = CreateMockDescriptor("schema", "User", "User Schema", DescriptorKind.Schema,
            state: DescriptorState.Active, contractHash: "abc123", supersededById: null);

        var snapshot = builder.Build(new[] { desc });

        var node = snapshot.FindNode(new DescriptorRef("schema", "User", null));
        node.Should().NotBeNull();
        node!.Kind.Should().Be(DescriptorKind.Schema);
        node.Name.Should().Be("User Schema");
        node.State.Should().Be(DescriptorState.Active);
        node.ContractHash.Should().Be("abc123");
        node.SupersededById.Should().BeNull();
        node.OutgoingEdgeIndices.Should().BeEmpty();
        node.IncomingEdgeIndices.Should().BeEmpty();
    }

    [Fact]
    public void Build_Extracts_Edges_From_RelationshipProvider()
    {
        var schemaRef = new DescriptorRef("schema", "User", null);
        var formRef = new DescriptorRef("form", "UserForm", null);

        var relationships = new List<DescriptorRelationship>
        {
            new(formRef, schemaRef, RelationshipKind.Uses, "Schema", "Schema", RelationshipStrength.Strong, false)
        };

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        var schemaDesc = CreateMockDescriptor("schema", "User", "User Schema", DescriptorKind.Schema);
        var formDesc = CreateMockDescriptor("form", "UserForm", "User Form", DescriptorKind.Form);

        mockProvider
            .Setup(p => p.GetRelationships(formDesc))
            .Returns(relationships);
        mockProvider
            .Setup(p => p.GetRelationships(schemaDesc))
            .Returns(Array.Empty<DescriptorRelationship>());

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
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

        var relationships = new List<DescriptorRelationship>
        {
            new(formRef, schemaRef, RelationshipKind.Uses, "Schema", "Schema", RelationshipStrength.Strong, false)
        };

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        var schemaDesc = CreateMockDescriptor("schema", "User", "User Schema", DescriptorKind.Schema);
        var formDesc = CreateMockDescriptor("form", "UserForm", "User Form", DescriptorKind.Form);

        mockProvider.Setup(p => p.GetRelationships(formDesc)).Returns(relationships);
        mockProvider.Setup(p => p.GetRelationships(schemaDesc)).Returns(Array.Empty<DescriptorRelationship>());

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
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

        var relationships = new List<DescriptorRelationship>
        {
            new(formRef, missingRef, RelationshipKind.Uses, "Schema", "Schema", RelationshipStrength.Strong, false)
        };

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        var formDesc = CreateMockDescriptor("form", "UserForm", "User Form", DescriptorKind.Form);
        mockProvider.Setup(p => p.GetRelationships(formDesc)).Returns(relationships);

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
        var snapshot = builder.Build(new IDescriptor[] { formDesc });

        snapshot.EdgeCount.Should().Be(1);
        snapshot.Edges[0].To.Should().Be(missingRef);
    }

    [Fact]
    public void Build_ConsumerIndex_NullVersion_Returns_All()
    {
        var target = new DescriptorRef("schema", "User", null);
        var c1 = CreateMockDescriptor("capability", "CreateUser", "CreateUser", DescriptorKind.Capability);
        var c2 = CreateMockDescriptor("form", "UserForm", "UserForm", DescriptorKind.Form);
        var targetDesc = CreateMockDescriptor("schema", "User", "User", DescriptorKind.Schema);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(c1)).Returns(new DescriptorRelationship[]
        {
            new(
                new DescriptorRef("capability", "CreateUser", null), target, RelationshipKind.Uses, null, null, RelationshipStrength.Strong, false)
        });
        mockProvider.Setup(p => p.GetRelationships(c2)).Returns(new DescriptorRelationship[]
        {
            new(
                new DescriptorRef("form", "UserForm", null), target, RelationshipKind.Uses, null, null, RelationshipStrength.Strong, false)
        });
        mockProvider.Setup(p => p.GetRelationships(targetDesc)).Returns(Array.Empty<DescriptorRelationship>());

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
        var snapshot = builder.Build(new IDescriptor[] { targetDesc, c1, c2 });

        var consumers = snapshot.GetConsumers("schema", "User");
        consumers.Should().HaveCount(2);
        consumers.Select(n => n.Ref.Id).Should().BeEquivalentTo(new[] { "CreateUser", "UserForm" });
    }

    [Fact]
    public void Build_ConsumerIndex_ExactVersion_Returns_Exact_Plus_Unpinned()
    {
        var targetV2 = new DescriptorRef("schema", "User", 2);
        var cv1 = CreateMockDescriptor("capability", "ExactV1", "EV1", DescriptorKind.Capability);
        var cv2 = CreateMockDescriptor("capability", "ExactV2", "EV2", DescriptorKind.Capability);
        var cUnpinned = CreateMockDescriptor("form", "Unpinned", "UP", DescriptorKind.Form);
        var targetDesc = CreateMockDescriptor("schema", "User", "User", DescriptorKind.Schema);

        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(cv1)).Returns(new DescriptorRelationship[]
        {
            new(new DescriptorRef("capability", "ExactV1", null), new DescriptorRef("schema", "User", 1), RelationshipKind.Uses, null, null, RelationshipStrength.Strong, false)
        });
        mockProvider.Setup(p => p.GetRelationships(cv2)).Returns(new DescriptorRelationship[]
        {
            new(new DescriptorRef("capability", "ExactV2", null), targetV2, RelationshipKind.Uses, null, null, RelationshipStrength.Strong, false)
        });
        mockProvider.Setup(p => p.GetRelationships(cUnpinned)).Returns(new DescriptorRelationship[]
        {
            new(new DescriptorRef("form", "Unpinned", null), new DescriptorRef("schema", "User", null), RelationshipKind.Uses, null, null, RelationshipStrength.Strong, false)
        });
        mockProvider.Setup(p => p.GetRelationships(targetDesc)).Returns(Array.Empty<DescriptorRelationship>());

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
        var snapshot = builder.Build(new IDescriptor[] { targetDesc, cv1, cv2, cUnpinned });

        var consumersV2 = snapshot.GetConsumers("schema", "User", version: 2);
        consumersV2.Should().HaveCount(2); // ExactV2 + Unpinned
        consumersV2.Select(n => n.Ref.Id).Should().BeEquivalentTo(new[] { "ExactV2", "Unpinned" });
    }

    [Fact]
    public void Build_ConsumerIndex_No_Match_Returns_Empty()
    {
        var desc = CreateMockDescriptor("schema", "User", "User", DescriptorKind.Schema);
        var mockProvider = new Mock<IDescriptorRelationshipProvider>();
        mockProvider.Setup(p => p.GetRelationships(desc)).Returns(Array.Empty<DescriptorRelationship>());

        var builder = new DescriptorTopologyBuilder(mockProvider.Object);
        var snapshot = builder.Build(new IDescriptor[] { desc });

        snapshot.GetConsumers("schema", "NoSuch").Should().BeEmpty();
    }

    // Helper
    private static IDescriptor CreateMockDescriptor(
        string ns, string id, string name, DescriptorKind kind,
        DescriptorState state = DescriptorState.Active,
        string contractHash = "hash",
        string? supersededById = null)
    {
        var mock = new Mock<IDescriptor>();
        mock.Setup(d => d.Namespace).Returns(ns);
        mock.Setup(d => d.Id).Returns(id);
        mock.Setup(d => d.Name).Returns(name);
        mock.Setup(d => d.Kind).Returns(kind);
        mock.Setup(d => d.State).Returns(state);
        mock.Setup(d => d.ContractHash).Returns(contractHash);
        mock.Setup(d => d.SupersededById).Returns(supersededById);
        return mock.Object;
    }
}
