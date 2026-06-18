using Xunit;
using Moq;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.ContextPack.Abstractions;
using FluentAssertions;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Wave 1 tests: Context / Read tools.
/// Verifies: BuildMetadataContextPack, BuildRuntimeScenarioContextPack,
/// GetDescriptorByRef, SearchDescriptors, ListDescriptorRelationships, GetTopologySummary.
/// </summary>
public class Wave1ContextReadTests : AgentControlPlaneTestBase
{
    [Fact]
    public async Task GetDescriptorByRef_Returns_DescriptorInfo_When_Found()
    {
        var service = CreateService();
        var context = CreateContext("GetDescriptorByRef");
        var descRef = CreateDescriptorRef("test", "desc-001");
        var descriptor = CreateTestDescriptor("test", "desc-001", name: "MyEvent");

        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns([descriptor]);

        var result = await service.GetDescriptorByRefAsync(context, descRef);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value.Should().NotBeNull();
        result.Value!.Ref.Should().Be(descRef);
        result.Value.Name.Should().Be("MyEvent");
        result.Value.Kind.Should().Be(DescriptorKind.Event);
    }

    [Fact]
    public async Task GetDescriptorByRef_Returns_NotFound_When_Missing()
    {
        var service = CreateService();
        var context = CreateContext("GetDescriptorByRef");
        var descRef = CreateDescriptorRef("test", "nonexistent");

        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns([]);

        var result = await service.GetDescriptorByRefAsync(context, descRef);

        result.Status.Should().Be(AgentToolResultStatus.NotFound);
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetDescriptorByRef_Audit_Records_TouchedDescriptorRefs()
    {
        var service = CreateService();
        var context = CreateContext("GetDescriptorByRef");
        var descRef = CreateDescriptorRef("test", "desc-001");

        DescriptorCatalogMock.Setup(c => c.GetAll())
            .Returns([CreateTestDescriptor("test", "desc-001")]);

        await service.GetDescriptorByRefAsync(context, descRef);

        InMemoryAuditor.GetAllRecords().Should().Contain(r =>
            r.TouchedDescriptorRefs != null &&
            r.TouchedDescriptorRefs.Any(d => d.FullId == descRef.FullId));
    }

    [Fact]
    public async Task SearchDescriptors_Returns_Matching_Results()
    {
        var service = CreateService();
        var context = CreateContext("SearchDescriptors");

        var descriptors = new List<IDescriptor>
        {
            CreateTestDescriptor("ns1", "d1", name: "Alpha"),
            CreateTestDescriptor("ns1", "d2", name: "Beta"),
            CreateTestDescriptor("ns2", "d3", name: "Gamma")
        };
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns(descriptors);

        var request = new DescriptorSearchRequest { Namespace = "ns1", MaxResults = 50 };

        var result = await service.SearchDescriptorsAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.TotalCount.Should().Be(2);
        result.Value.Descriptors.Should().HaveCount(2);
        result.Value.WasTruncated.Should().BeFalse();
    }

    [Fact]
    public async Task SearchDescriptors_Truncates_When_Exceeds_MaxResults()
    {
        var service = CreateService();
        var context = CreateContext("SearchDescriptors");

        var descriptors = Enumerable.Range(0, 100)
            .Select(i => (IDescriptor)CreateTestDescriptor("ns", $"d{i}", name: $"Desc{i}"))
            .ToList();
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns(descriptors);

        var request = new DescriptorSearchRequest { MaxResults = 10 };

        var result = await service.SearchDescriptorsAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.TotalCount.Should().Be(100);
        result.Value.Descriptors.Should().HaveCount(10);
        result.Value.WasTruncated.Should().BeTrue();
        // SEARCH_TRUNCATED is recorded in the audit, not in result.Diagnostics for Success results
        result.AuditRecord!.Diagnostics.Should().Contain(d => d.Code == "SEARCH_TRUNCATED");
    }

    [Fact]
    public async Task SearchDescriptors_Filters_By_Kind()
    {
        var service = CreateService();
        var context = CreateContext("SearchDescriptors");

        var descriptors = new List<IDescriptor>
        {
            CreateTestDescriptor("ns", "d1", kind: DescriptorKind.Event),
            CreateTestDescriptor("ns", "d2", kind: DescriptorKind.Capability),
        };
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns(descriptors);

        var request = new DescriptorSearchRequest { Kind = DescriptorKind.Event };

        var result = await service.SearchDescriptorsAsync(context, request);

        result.Value!.Descriptors.Should().ContainSingle(d => d.Kind == DescriptorKind.Event);
    }

    [Fact]
    public async Task SearchDescriptors_Filters_By_NameContains()
    {
        var service = CreateService();
        var context = CreateContext("SearchDescriptors");

        var descriptors = new List<IDescriptor>
        {
            CreateTestDescriptor("ns", "d1", name: "OrderCreated"),
            CreateTestDescriptor("ns", "d2", name: "PaymentProcessed"),
        };
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns(descriptors);

        var request = new DescriptorSearchRequest { NameContains = "Order" };

        var result = await service.SearchDescriptorsAsync(context, request);

        result.Value!.Descriptors.Should().ContainSingle();
        result.Value.Descriptors[0].Name.Should().Be("OrderCreated");
    }

    [Fact]
    public async Task ListDescriptorRelationships_Returns_Dependencies_And_Dependents()
    {
        var service = CreateService();
        var context = CreateContext("ListDescriptorRelationships");
        var descRef = CreateDescriptorRef("ns", "d1");
        var descriptor = CreateTestDescriptor("ns", "d1");

        var fromRef = CreateDescriptorRef("ns", "d1");
        var toRef = CreateDescriptorRef("ns", "d2");

        // Set up topology with an outgoing edge from d1 → d2
        var fromNode = new DescriptorNode
        {
            Ref = fromRef, Kind = DescriptorKind.Event, Name = "d1", State = DescriptorState.Active,
            OutgoingEdgeIndices = new HashSet<int> { 0 }, IncomingEdgeIndices = new HashSet<int>()
        };
        var toNode = new DescriptorNode
        {
            Ref = toRef, Kind = DescriptorKind.Event, Name = "d2", State = DescriptorState.Active,
            OutgoingEdgeIndices = new HashSet<int>(), IncomingEdgeIndices = new HashSet<int> { 0 }
        };
        var nodes = new Dictionary<DescriptorRef, DescriptorNode>
        {
            [fromRef] = fromNode,
            [toRef] = toNode
        };
        var edges = new List<DescriptorEdge>
        {
            new()
            {
                Index = 0, From = fromRef, To = toRef, Kind = RelationshipKind.DependsOn,
                Strength = RelationshipStrength.Strong, IsRuntimeBinding = false
            }
        };

        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns([descriptor]);
        SetupTopologySnapshot(nodes, edges);

        var result = await service.ListDescriptorRelationshipsAsync(context, descRef);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Subject.Should().Be(descRef);
        result.Value.Dependencies.Should().HaveCount(1);
        result.Value.Dependents.Should().HaveCount(0);
        result.Value.Dependencies[0].From.Should().Be(fromRef);
        result.Value.Dependencies[0].To.Should().Be(toRef);
        result.Value.Dependencies[0].Kind.Should().Be(RelationshipKind.DependsOn);
    }

    [Fact]
    public async Task ListDescriptorRelationships_Returns_NotFound_When_Descriptor_Missing()
    {
        var service = CreateService();
        var context = CreateContext("ListDescriptorRelationships");
        var descRef = CreateDescriptorRef("ns", "nonexistent");

        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns([]);
        SetupTopologySnapshot(); // Empty topology — ref not found

        var result = await service.ListDescriptorRelationshipsAsync(context, descRef);

        result.Status.Should().Be(AgentToolResultStatus.NotFound);
    }

    [Fact]
    public async Task GetTopologySummary_Returns_Node_And_Edge_Counts()
    {
        var service = CreateService();
        var context = CreateContext("GetTopologySummary");

        SetupTopologySnapshot();
        DescriptorCatalogMock.Setup(c => c.GetAll())
            .Returns(new List<IDescriptor> { CreateTestDescriptor() });

        var result = await service.GetTopologySummaryAsync(context);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.TotalNodeCount.Should().BeGreaterThanOrEqualTo(0);
        result.Value!.TotalEdgeCount.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task BuildMetadataContextPack_Returns_ContextPack()
    {
        var service = CreateService();
        var context = CreateContext("BuildMetadataContextPack");

        SetupTopologySnapshot();
        DescriptorCatalogMock.Setup(c => c.GetAll())
            .Returns(new List<IDescriptor> { CreateTestDescriptor() });

        var packRequest = new MetadataContextPackRequest
        {
            FocusDescriptors = [CreateDescriptorRef()],
            Scope = MetadataContextPackScope.DirectDependencies
        };

        var focusRefs = new List<DescriptorRef> { CreateDescriptorRef() };
        var expectedPack = new MetadataContextPack
        {
            Request = packRequest,
            Descriptors = Array.Empty<MetadataContextPackDescriptorEntry>(),
            Relationships = Array.Empty<MetadataContextPackRelationshipEntry>(),
            Summary = new MetadataContextPackSummary
            {
                TotalDescriptorCount = 1,
                DescriptorCountsByKind = new Dictionary<DescriptorKind, int>(),
                TotalRelationshipCount = 0,
                RelationshipCountsByKind = new Dictionary<RelationshipKind, int>(),
                FocusRefs = focusRefs,
                WasTruncated = false,
                TruncatedAtCount = null,
                TraversalDepthReached = 1
            },
            Diagnostics = Array.Empty<MetadataContextPackDiagnostic>()
        };

        ContextPackBuilderMock.Setup(b => b.Build(
                It.IsAny<MetadataContextPackRequest>(),
                It.IsAny<DescriptorTopologySnapshot>(),
                It.IsAny<IReadOnlyList<IDescriptor>>()))
            .Returns(expectedPack);

        var result = await service.BuildMetadataContextPackAsync(context, packRequest);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task BuildRuntimeScenarioContextPack_Returns_ContextPack()
    {
        var service = CreateService();
        var context = CreateContext("BuildRuntimeScenarioContextPack");

        SetupTopologySnapshot();
        DescriptorCatalogMock.Setup(c => c.GetAll())
            .Returns(new List<IDescriptor> { CreateTestDescriptor() });

        var packRequest = new MetadataContextPackRequest
        {
            FocusDescriptors = [CreateDescriptorRef()],
            Scope = MetadataContextPackScope.RuntimeScenario
        };

        var focusRefs = new List<DescriptorRef> { CreateDescriptorRef() };
        var expectedPack = new MetadataContextPack
        {
            Request = packRequest,
            Descriptors = Array.Empty<MetadataContextPackDescriptorEntry>(),
            Relationships = Array.Empty<MetadataContextPackRelationshipEntry>(),
            Summary = new MetadataContextPackSummary
            {
                TotalDescriptorCount = 1,
                DescriptorCountsByKind = new Dictionary<DescriptorKind, int>(),
                TotalRelationshipCount = 0,
                RelationshipCountsByKind = new Dictionary<RelationshipKind, int>(),
                FocusRefs = focusRefs,
                WasTruncated = false,
                TruncatedAtCount = null,
                TraversalDepthReached = 1
            },
            Diagnostics = Array.Empty<MetadataContextPackDiagnostic>()
        };

        ContextPackBuilderMock.Setup(b => b.Build(
                It.IsAny<MetadataContextPackRequest>(),
                It.IsAny<DescriptorTopologySnapshot>(),
                It.IsAny<IReadOnlyList<IDescriptor>>()))
            .Returns(expectedPack);

        var result = await service.BuildRuntimeScenarioContextPackAsync(context, packRequest);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetDescriptorByRef_VersionPinned_Matches_Specific_Version()
    {
        var service = CreateService();
        var context = CreateContext("GetDescriptorByRef");
        var descRef = CreateDescriptorRef("test", "desc-001", version: 2);

        var v1 = new TestVersionedDescriptor("test", "desc-001", version: 1, name: "V1");
        var v2 = new TestVersionedDescriptor("test", "desc-001", version: 2, name: "V2");

        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns([v1, v2]);

        var result = await service.GetDescriptorByRefAsync(context, descRef);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Name.Should().Be("V2");
        result.Value.Ref.Version.Should().Be(2);
    }

    [Fact]
    public async Task GetDescriptorByRef_Unpinned_Ambiguous_Returns_InvalidRequest()
    {
        var service = CreateService();
        var context = CreateContext("GetDescriptorByRef");
        var descRef = CreateDescriptorRef("test", "desc-001"); // no version

        var v1 = new TestVersionedDescriptor("test", "desc-001", version: 1, name: "V1");
        var v2 = new TestVersionedDescriptor("test", "desc-001", version: 2, name: "V2");

        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns([v1, v2]);

        var result = await service.GetDescriptorByRefAsync(context, descRef);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Value.Should().BeNull();
        result.Diagnostics.Should().Contain(d => d.Code == "DESCRIPTOR_REF_AMBIGUOUS");
        // Diagnostic should list candidate versions
        result.Diagnostics.Should().Contain(d => d.Code == "DESCRIPTOR_REF_AMBIGUOUS" && d.Message.Contains("1, 2"));
    }

    [Fact]
    public async Task GetDescriptorByRef_Unpinned_Single_Version_Resolves_To_Versioned_Ref()
    {
        var service = CreateService();
        var context = CreateContext("GetDescriptorByRef");
        var descRef = CreateDescriptorRef("test", "desc-001"); // no version

        var v2 = new TestVersionedDescriptor("test", "desc-001", version: 2, name: "V2");

        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns([v2]);

        var result = await service.GetDescriptorByRefAsync(context, descRef);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Ref.Version.Should().Be(2);
    }

    [Fact]
    public async Task SearchDescriptors_Preserves_Version_In_Results()
    {
        var service = CreateService();
        var context = CreateContext("SearchDescriptors");

        var v1 = new TestVersionedDescriptor("ns", "d1", version: 1, name: "V1");
        var v2 = new TestVersionedDescriptor("ns", "d1", version: 2, name: "V2");
        var unversioned = CreateTestDescriptor("ns", "d2", name: "Unversioned");

        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns([v1, v2, unversioned]);

        var request = new DescriptorSearchRequest { Namespace = "ns", MaxResults = 50 };

        var result = await service.SearchDescriptorsAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Descriptors.Should().HaveCount(3);
        // Versioned descriptors should have version in their Ref
        result.Value.Descriptors.Should().Contain(d => d.Ref.Version == 1);
        result.Value.Descriptors.Should().Contain(d => d.Ref.Version == 2);
        // Unversioned descriptor should have null version
        result.Value.Descriptors.Should().Contain(d => d.Ref.Id == "d2" && d.Ref.Version == null);
    }

    [Fact]
    public async Task ListDescriptorRelationships_Returns_Incoming_Edges_As_Dependents()
    {
        var service = CreateService();
        var context = CreateContext("ListDescriptorRelationships");
        var d1Ref = CreateDescriptorRef("ns", "d1");
        var d2Ref = CreateDescriptorRef("ns", "d2");

        // d1 depends on d2 → edge d1→d2. From d2's perspective, d1 is a dependent.
        var d1Node = new DescriptorNode
        {
            Ref = d1Ref, Kind = DescriptorKind.Event, Name = "d1", State = DescriptorState.Active,
            OutgoingEdgeIndices = new HashSet<int> { 0 }, IncomingEdgeIndices = new HashSet<int>()
        };
        var d2Node = new DescriptorNode
        {
            Ref = d2Ref, Kind = DescriptorKind.Event, Name = "d2", State = DescriptorState.Active,
            OutgoingEdgeIndices = new HashSet<int>(), IncomingEdgeIndices = new HashSet<int> { 0 }
        };
        var nodes = new Dictionary<DescriptorRef, DescriptorNode>
        {
            [d1Ref] = d1Node,
            [d2Ref] = d2Node
        };
        var edges = new List<DescriptorEdge>
        {
            new()
            {
                Index = 0, From = d1Ref, To = d2Ref, Kind = RelationshipKind.DependsOn,
                Strength = RelationshipStrength.Strong, IsRuntimeBinding = false
            }
        };

        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns([CreateTestDescriptor("ns", "d2")]);
        SetupTopologySnapshot(nodes, edges);

        // Query d2 — should see d1 as a dependent (incoming edge)
        var result = await service.ListDescriptorRelationshipsAsync(context, d2Ref);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Dependencies.Should().HaveCount(0); // d2 has no outgoing edges
        result.Value.Dependents.Should().HaveCount(1);   // d2 has one incoming edge (d1 depends on it)
        result.Value.Dependents[0].From.Should().Be(d1Ref);
        result.Value.Dependents[0].To.Should().Be(d2Ref);
    }
}
