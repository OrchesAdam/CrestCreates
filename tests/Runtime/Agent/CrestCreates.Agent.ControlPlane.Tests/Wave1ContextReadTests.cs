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

        DescriptorCatalogMock.Setup(c => c.Get(descRef.FullId)).Returns(descriptor);

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

        DescriptorCatalogMock.Setup(c => c.Get(descRef.FullId)).Returns((IDescriptor?)null);

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

        DescriptorCatalogMock.Setup(c => c.Get(descRef.FullId))
            .Returns(CreateTestDescriptor("test", "desc-001"));

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
        var relationships = new List<DescriptorRelationship>
        {
            new(fromRef, toRef, RelationshipKind.DependsOn)
        };

        DescriptorCatalogMock.Setup(c => c.Get(descRef.FullId)).Returns(descriptor);
        RelationshipProviderMock.Setup(r => r.GetRelationships(descriptor)).Returns(relationships);

        var result = await service.ListDescriptorRelationshipsAsync(context, descRef);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Subject.Should().Be(descRef);
    }

    [Fact]
    public async Task ListDescriptorRelationships_Returns_NotFound_When_Descriptor_Missing()
    {
        var service = CreateService();
        var context = CreateContext("ListDescriptorRelationships");
        var descRef = CreateDescriptorRef("ns", "nonexistent");

        DescriptorCatalogMock.Setup(c => c.Get(descRef.FullId)).Returns((IDescriptor?)null);

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
}
