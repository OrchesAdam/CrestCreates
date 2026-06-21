using CrestCreates.Agent.ControlPlane;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.ContextPack.Abstractions;
using CrestCreates.Metadata;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Shared test infrastructure for Agent Control Plane tool surface tests.
/// Provides mock setup helpers and common invocation context factories.
/// </summary>
public abstract class AgentControlPlaneTestBase
{
    protected readonly Mock<IAgentToolManifestProvider> ManifestProviderMock = new();
    protected readonly Mock<IAgentToolAuthorizationService> AuthorizationServiceMock = new();
    protected readonly Mock<IAgentToolInvocationAuditor> AuditorMock = new();
    protected readonly Mock<DraftAbstractions.IDescriptorDraftStore> DraftStoreMock = new();
    protected readonly Mock<DraftAbstractions.IDescriptorDraftValidator> DraftValidatorMock = new();
    protected readonly Mock<DraftAbstractions.IDescriptorDraftReviewService> DraftReviewServiceMock = new();
    protected readonly Mock<DraftAbstractions.IDescriptorDraftMaterializer> DraftMaterializerMock = new();
    protected readonly Mock<IMetadataContextPackBuilder> ContextPackBuilderMock = new();
    protected readonly Mock<IDescriptorCatalog> DescriptorCatalogMock = new();
    protected readonly Mock<IDescriptorRelationshipProvider> RelationshipProviderMock = new();
    protected readonly Mock<IDescriptorTopologyBuilder> TopologyBuilderMock = new();
    protected readonly Mock<IDescriptorPackageBuilder> PackageBuilderMock = new();
    protected readonly InMemoryAgentToolInvocationAuditor InMemoryAuditor = new();

    protected const string TestTenantId = "tenant-001";
    protected const string TestActorId = "actor-001";
    protected const string TestCorrelationId = "corr-001";

    /// <summary>
    /// Creates a DefaultAgentControlPlaneToolService with real manifest provider,
    /// real authorization service (DevelopmentDefaults — allows all tools), and in-memory auditor.
    /// Mocks are used for all downstream services.
    /// </summary>
    protected DefaultAgentControlPlaneToolService CreateService(
        InMemoryAgentToolInvocationAuditor? auditor = null)
    {
        var options = AgentToolAuthorizationOptions.DevelopmentDefaults;
        var authzService = new DefaultAgentToolAuthorizationService(options);
        var actualAuditor = auditor ?? InMemoryAuditor;

        return new DefaultAgentControlPlaneToolService(
            new StaticAgentToolManifestProvider(),
            authzService,
            actualAuditor,
            DraftStoreMock.Object,
            DraftValidatorMock.Object,
            DraftReviewServiceMock.Object,
            DraftMaterializerMock.Object,
            ContextPackBuilderMock.Object,
            DescriptorCatalogMock.Object,
            RelationshipProviderMock.Object,
            TopologyBuilderMock.Object,
            PackageBuilderMock.Object,
            NullLogger<DefaultAgentControlPlaneToolService>.Instance,
            authorizationOptions: options);
    }

    /// <summary>
    /// Creates a DefaultAgentControlPlaneToolService with the specified authorization options.
    /// Uses real manifest provider and in-memory auditor.
    /// </summary>
    protected DefaultAgentControlPlaneToolService CreateService(
        AgentToolAuthorizationOptions options,
        InMemoryAgentToolInvocationAuditor? auditor = null)
    {
        var authzService = new DefaultAgentToolAuthorizationService(options);
        var actualAuditor = auditor ?? InMemoryAuditor;

        return new DefaultAgentControlPlaneToolService(
            new StaticAgentToolManifestProvider(),
            authzService,
            actualAuditor,
            DraftStoreMock.Object,
            DraftValidatorMock.Object,
            DraftReviewServiceMock.Object,
            DraftMaterializerMock.Object,
            ContextPackBuilderMock.Object,
            DescriptorCatalogMock.Object,
            RelationshipProviderMock.Object,
            TopologyBuilderMock.Object,
            PackageBuilderMock.Object,
            NullLogger<DefaultAgentControlPlaneToolService>.Instance,
            authorizationOptions: options);
    }

    /// <summary>
    /// Creates a service with fully mocked dependencies for fine-grained control.
    /// </summary>
    protected DefaultAgentControlPlaneToolService CreateServiceWithMocks()
    {
        return new DefaultAgentControlPlaneToolService(
            ManifestProviderMock.Object,
            AuthorizationServiceMock.Object,
            AuditorMock.Object,
            DraftStoreMock.Object,
            DraftValidatorMock.Object,
            DraftReviewServiceMock.Object,
            DraftMaterializerMock.Object,
            ContextPackBuilderMock.Object,
            DescriptorCatalogMock.Object,
            RelationshipProviderMock.Object,
            TopologyBuilderMock.Object,
            PackageBuilderMock.Object,
            NullLogger<DefaultAgentControlPlaneToolService>.Instance,
            authorizationOptions: AgentToolAuthorizationOptions.DevelopmentDefaults);
    }

    protected static AgentToolInvocationContext CreateContext(
        string toolName,
        string tenantId = TestTenantId,
        string actorId = TestActorId,
        AgentToolActorKind actorKind = AgentToolActorKind.Agent,
        AgentToolInvocationSource source = AgentToolInvocationSource.Direct)
    {
        return new AgentToolInvocationContext
        {
            TenantId = tenantId,
            ActorId = actorId,
            ActorKind = actorKind,
            CorrelationId = TestCorrelationId,
            ToolName = toolName,
            InvocationSource = source
        };
    }

    protected static AgentToolInvocationContext CreateHumanContext(string toolName)
        => CreateContext(toolName, actorKind: AgentToolActorKind.Human,
            source: AgentToolInvocationSource.HttpAdapter);

    protected static DescriptorRef CreateDescriptorRef(
        string ns = "test", string id = "desc-001", int? version = null)
        => new(ns, id, version);

    protected static TestDescriptor CreateTestDescriptor(
        string ns = "test", string id = "desc-001",
        DescriptorKind kind = DescriptorKind.Event,
        DescriptorState state = DescriptorState.Active,
        string name = "TestDescriptor")
    {
        return new TestDescriptor
        {
            Namespace = ns,
            Id = id,
            Name = name,
            Kind = kind,
            State = state,
            ContractHash = "ch-001",
            DefinitionHash = "dh-001"
        };
    }

    protected static Draft CreateTestDraft(
        string draftId = "draft-001",
        string tenantId = TestTenantId,
        DescriptorKind kind = DescriptorKind.Event,
        string descriptorId = "test.desc-001",
        DraftAbstractions.DescriptorDraftOperation operation = DraftAbstractions.DescriptorDraftOperation.Create,
        DraftAbstractions.DescriptorDraftStatus status = DraftAbstractions.DescriptorDraftStatus.Created)
    {
        return new Draft
        {
            TenantId = tenantId,
            DraftId = draftId,
            DescriptorKind = kind,
            DescriptorId = descriptorId,
            Operation = operation,
            AuthorKind = DraftAbstractions.DescriptorDraftAuthorKind.Agent,
            AuthorId = TestActorId,
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new TestDraftPayload(kind, descriptorId, "TestDraft"),
            Status = status
        };
    }

    /// <summary>
    /// Creates an AgentDraftPayloadDto for use in CreateDescriptorDraftRequest
    /// and UpdateDescriptorDraftRequest payloads. This replaces direct
    /// TestDraftPayload usage in request DTO construction.
    /// TestDraftPayload remains in use for internal Draft store objects.
    /// </summary>
    protected static AgentDraftPayloadDto CreateTestPayloadDto(DescriptorKind kind, string id, string name)
    {
        return new AgentDraftPayloadDto
        {
            Discriminator = kind,
            Capability = kind == DescriptorKind.Capability
                ? new AgentCapabilityDraftPayloadDto { Name = name, CapabilityKind = "Action", RiskLevel = "Low" }
                : null,
            Workflow = kind == DescriptorKind.Workflow
                ? new AgentWorkflowDraftPayloadDto { Name = name }
                : null,
            HumanTask = kind == DescriptorKind.HumanTask
                ? new AgentHumanTaskDraftPayloadDto { Name = name }
                : null,
            Form = kind == DescriptorKind.Form
                ? new AgentFormDraftPayloadDto { Name = name }
                : null,
            Event = kind == DescriptorKind.Event
                ? new AgentEventDraftPayloadDto { Name = name }
                : null,
            Schema = kind == DescriptorKind.Schema
                ? new AgentSchemaDraftPayloadDto { Name = name }
                : null,
        };
    }

    protected void SetupTopologySnapshot(
        Dictionary<DescriptorRef, DescriptorNode>? nodes = null,
        List<DescriptorEdge>? edges = null)
    {
        var nodeDict = nodes ?? new Dictionary<DescriptorRef, DescriptorNode>();
        var edgeList = edges ?? new List<DescriptorEdge>();
        var diagnostics = new DescriptorTopologyDiagnostics { All = Array.Empty<DescriptorTopologyDiagnostic>() };

        var topology = new DescriptorTopologySnapshot(
            nodeDict, edgeList, diagnostics,
            new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>(),
            new Dictionary<(DescriptorIdentity, int), List<(DescriptorRef, DescriptorEdge)>>(),
            new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>(),
            DateTimeOffset.UtcNow);

        TopologyBuilderMock.Setup(b => b.Build(It.IsAny<IReadOnlyList<IDescriptor>>()))
            .Returns(topology);
    }

    protected void SetupPackageBuilder()
    {
        PackageBuilderMock.Setup(b => b.Build(It.IsAny<DescriptorPackageBuildRequest>()))
            .Returns(new DescriptorPackage
            {
                Manifest = new DescriptorManifest
                {
                    PackageId = "pkg-001",
                    PackageVersion = "1",
                    ContentHash = "hash-001",
                    EvidenceHash = "ev-hash-001",
                    EnvelopeHash = "env-hash-001",
                    DescriptorEntries = Array.Empty<DescriptorManifestEntry>()
                },
                Snapshot = new DescriptorSnapshot(),
                Evidence = new DescriptorPackageEvidence()
            });
    }
}

/// <summary>
/// Test-only IDescriptor implementation for mock-free test scenarios.
/// </summary>
public sealed class TestDescriptor : IDescriptor
{
    public string Namespace { get; init; } = "test";
    public string Id { get; init; } = "desc-001";
    public string Name { get; init; } = "TestDescriptor";
    public DescriptorKind Kind { get; init; } = DescriptorKind.Event;
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string ContractHash { get; init; } = "ch-001";
    public string DefinitionHash { get; init; } = "dh-001";
    public string? SupersededById { get; init; }
}

/// <summary>
/// Test-only IVersionedDescriptor implementation for version-aware test scenarios.
/// </summary>
public sealed class TestVersionedDescriptor : IVersionedDescriptor
{
    public string Namespace { get; init; } = "test";
    public string Id { get; init; } = "desc-001";
    public string Name { get; init; } = "TestVersionedDescriptor";
    public DescriptorKind Kind { get; init; } = DescriptorKind.Event;
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string ContractHash { get; init; } = "ch-001";
    public string DefinitionHash { get; init; } = "dh-001";
    public string? SupersededById { get; init; }
    public int Version { get; init; } = 1;

    public TestVersionedDescriptor(string ns = "test", string id = "desc-001", int version = 1, string name = "TestVersionedDescriptor")
    {
        Namespace = ns;
        Id = id;
        Version = version;
        Name = name;
    }
}

/// <summary>
/// Test-only DescriptorDraftPayload for mock-free test scenarios.
/// Must be a record since DescriptorDraftPayload is an abstract record.
/// </summary>
public sealed record TestDraftPayload : DraftAbstractions.DescriptorDraftPayload
{
    private readonly DescriptorKind _kind;
    private readonly string _id;
    private readonly string _name;

    public TestDraftPayload(DescriptorKind kind, string id, string name)
    {
        _kind = kind;
        _id = id;
        _name = name;
    }

    public override DescriptorKind DescriptorKind => _kind;

    public override IDescriptor GetDescriptor() => new TestDescriptor
    {
        Id = _id,
        Name = _name,
        Kind = _kind
    };

    public override DraftAbstractions.DescriptorDraftPayload CreateClone() => new TestDraftPayload(_kind, _id, _name);
}
