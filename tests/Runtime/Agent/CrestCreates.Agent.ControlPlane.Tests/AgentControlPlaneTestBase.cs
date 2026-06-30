using System.Collections.Concurrent;
using CrestCreates.Agent.ControlPlane;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Activation;
using CrestCreates.Agent.DraftContracts.Projection;
using CrestCreates.Event.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.ContextPack.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;
using DraftCanonicalHashing = CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;

// semantic-string-guard: allow

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
    protected readonly Mock<IDescriptorReviewReportBuilder> ReportBuilderMock = new();
    protected readonly Mock<IDescriptorReviewReportRenderer> ReportRendererMock = new();
    protected readonly Mock<IDescriptorStableHashBuilder> HashBuilderMock = new();
    protected readonly Mock<DraftCanonicalHashing.IDescriptorDraftReviewHashService> ReviewHashServiceMock = new();
    protected readonly Mock<IDescriptorActivationRequestService> ActivationRequestServiceMock = new();
    protected readonly Mock<IRuntimeActivationGate> RuntimeActivationGateMock = new();
    protected readonly Mock<IActivationEvidenceRechecker> EvidenceRecheckerMock = new();
    protected readonly Mock<IHumanTaskRuntime> HumanTaskRuntimeMock = new();
    protected readonly Mock<IActivationReviewOrchestrator> ActivationReviewOrchestratorMock = new();
    protected readonly InMemoryActivationBindingArtifactResolver InMemoryArtifactResolver = new();
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
        EnsureHashBuilderSetup();
        EnsureActivationRequestServiceSetup();
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
            HashBuilderMock.Object,
            ReviewHashServiceMock.Object,
            ReportBuilderMock.Object,
            ReportRendererMock.Object,
            ActivationRequestServiceMock.Object,
            ActivationReviewOrchestratorMock.Object,
            InMemoryArtifactResolver,
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
        EnsureHashBuilderSetup();
        EnsureActivationRequestServiceSetup();
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
            HashBuilderMock.Object,
            ReviewHashServiceMock.Object,
            ReportBuilderMock.Object,
            ReportRendererMock.Object,
            ActivationRequestServiceMock.Object,
            ActivationReviewOrchestratorMock.Object,
            InMemoryArtifactResolver,
            authorizationOptions: options);
    }

    /// <summary>
    /// Creates a DefaultAgentControlPlaneToolService with a mutable options factory.
    /// Enables testing scenarios where scope changes between calls (e.g., A/B/A reuse).
    /// </summary>
    protected DefaultAgentControlPlaneToolService CreateServiceWithOptionsFactory(
        Func<AgentToolAuthorizationOptions> optionsFactory,
        InMemoryAgentToolInvocationAuditor? auditor = null)
    {
        EnsureHashBuilderSetup();
        EnsureActivationRequestServiceSetup();
        var initialOptions = optionsFactory();
        var authzService = new DefaultAgentToolAuthorizationService(initialOptions);
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
            HashBuilderMock.Object,
            ReviewHashServiceMock.Object,
            ReportBuilderMock.Object,
            ReportRendererMock.Object,
            ActivationRequestServiceMock.Object,
            ActivationReviewOrchestratorMock.Object,
            InMemoryArtifactResolver,
            optionsFactory: optionsFactory);
    }

    /// <summary>
    /// Creates a service with fully mocked dependencies for fine-grained control.
    /// </summary>
    protected DefaultAgentControlPlaneToolService CreateServiceWithMocks()
    {
        EnsureHashBuilderSetup();
        EnsureActivationRequestServiceSetup();
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
            HashBuilderMock.Object,
            ReviewHashServiceMock.Object,
            ReportBuilderMock.Object,
            ReportRendererMock.Object,
            ActivationRequestServiceMock.Object,
            ActivationReviewOrchestratorMock.Object,
            InMemoryArtifactResolver,
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
            State = state
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
        var payloadDto = CreateTestPayloadDto(kind, descriptorId, "TestDraft");
        var createResult = AgentDraftPayloadProjection.Create(payloadDto);
        var domainPayload = createResult.IsSuccess
            ? createResult.Value!
            : (DraftAbstractions.DescriptorDraftPayload)new TestDraftPayload(kind, descriptorId, "TestDraft");

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
            Payload = domainPayload,
            Status = status
        };
    }

    /// <summary>
    /// Creates an AgentDraftPayloadDto for use in CreateDescriptorDraftRequest
    /// payloads. UpdateDescriptorDraftRequest uses AgentDraftPayloadPatchDto
    /// (constructed inline in tests). TestDraftPayload remains in use for
    /// internal Draft store objects.
    /// </summary>
    protected static AgentDraftPayloadDto CreateTestPayloadDto(DescriptorKind kind, string id, string name)
    {
        return new AgentDraftPayloadDto
        {
            Discriminator = kind,
            Capability = kind == DescriptorKind.Capability
                ? new AgentCapabilityDraftPayloadDto { Name = name, CapabilityKind = CapabilityKind.Command, RiskLevel = CapabilityRiskLevel.Low, State = DescriptorState.Active }
                : null,
            Workflow = kind == DescriptorKind.Workflow
                ? new AgentWorkflowDraftPayloadDto { Name = name, State = DescriptorState.Active }
                : null,
            HumanTask = kind == DescriptorKind.HumanTask
                ? new AgentHumanTaskDraftPayloadDto { Name = name, State = DescriptorState.Active, AssigneeStrategy = AssigneeStrategy.SingleUser, Interaction = new DescriptorRef("form", "default-interaction", 1) }
                : null,
            Form = kind == DescriptorKind.Form
                ? new AgentFormDraftPayloadDto { Name = name, State = DescriptorState.Active, FormSchema = new DescriptorRef("schema", "default-form-schema", 1) }
                : null,
            Event = kind == DescriptorKind.Event
                ? new AgentEventDraftPayloadDto { Name = name, State = DescriptorState.Active, Category = EventCategory.Domain, Semantic = EventSemantic.Fact, Importance = EventImportance.Operational, ChangeKind = SchemaChangeKind.Additive, PayloadSchema = new DescriptorRef("schema", "default-event-payload", 1) }
                : null,
            Schema = kind == DescriptorKind.Schema
                ? new AgentSchemaDraftPayloadDto { Name = name, State = DescriptorState.Active, ChangeKind = SchemaChangeKind.Additive }
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
                    DescriptorEntries = Array.Empty<DescriptorManifestEntry>()
                },
                SnapshotData = new DescriptorSnapshot(),
                Evidence = new DescriptorPackageEvidence()
            });
    }

    /// <summary>
    /// Creates a service with all binding artifacts (review result, package preview,
    /// evidence preview) pre-populated in the ToolService's internal stores.
    /// Use this when submitting activation requests that require complete evidence binding.
    /// </summary>
    protected async Task<(DefaultAgentControlPlaneToolService Service, string ReviewResultId, string PackagePreviewId, string EvidencePreviewId)> CreateServiceWithFullBindingArtifacts(
        string draftId = "draft-001")
    {
        var draft = CreateTestDraft(draftId: draftId);

        DraftStoreMock
            .Setup(s => s.GetAsync(TestTenantId, draftId, It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns([]);

        // Review setup
        var reviewResult = new DraftAbstractions.DescriptorDraftReviewResult
        {
            DraftId = draftId,
            TenantId = TestTenantId,
            ValidationResult = DraftAbstractions.DescriptorDraftValidationResult.Success(),
            Diagnostics = Array.Empty<DraftAbstractions.DescriptorDraftDiagnostic>(),
            IsActivationEligible = true,
            ProposedInventory = new List<IDescriptor>().AsReadOnly(),
            GovernanceDecision = new DescriptorLifecycleGovernanceReport
            {
                Decisions = [],
                MaxDecision = DescriptorLifecycleDecisionKind.Allowed,
                PackageFindings = []
            }
        };
        DraftReviewServiceMock
            .Setup(r => r.ReviewAsync(draft, It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(reviewResult));

        // Materializer + package builder for package/evidence tool calls
        DraftMaterializerMock
            .Setup(m => m.Materialize(draft, It.IsAny<IReadOnlyList<IDescriptor>>()))
            .Returns(DraftAbstractions.DescriptorDraftMaterializationResult.Success(new List<IDescriptor>().AsReadOnly()));

        SetupPackageBuilder();

        // Create service AFTER mock setups (EnsureActivationRequestServiceSetup runs inside)
        var service = CreateService();

        // Execute review tool
        var reviewContext = CreateContext("ReviewDescriptorDraft");
        await service.ReviewDescriptorDraftAsync(reviewContext, draftId);
        var reviewResultId = InMemoryAuditor.GetAllRecords().First(r =>
            r.Context.ToolName == "ReviewDescriptorDraft" &&
            r.TouchedReviewResultIds != null).TouchedReviewResultIds!.First();

        // Execute package preview tool
        var previewContext = CreateContext("PreviewDescriptorPackage");
        await service.PreviewDescriptorPackageAsync(previewContext, draftId);
        var packagePreviewId = InMemoryAuditor.GetAllRecords().First(r =>
            r.Context.ToolName == "PreviewDescriptorPackage" &&
            r.TouchedPackagePreviewIds != null).TouchedPackagePreviewIds!.First();

        // Execute evidence preview tool
        var evidenceContext = CreateContext("BuildPackageEvidencePreview");
        await service.BuildPackageEvidencePreviewAsync(evidenceContext, draftId);
        var evidencePreviewId = InMemoryAuditor.GetAllRecords().First(r =>
            r.Context.ToolName == "BuildPackageEvidencePreview" &&
            r.TouchedPackagePreviewIds != null).TouchedPackagePreviewIds!.First();

        return (service, reviewResultId, packagePreviewId, evidencePreviewId);
    }

    /// <summary>
    /// Ensures HashBuilderMock returns valid <see cref="DescriptorStableHashes"/> for any descriptor.
    /// Prevents <see cref="NullReferenceException"/> when the service accesses
    /// <c>hashes.ContractHash.Value</c> or <c>hashes.DefinitionHash.Value</c>.
    /// </summary>
    private void EnsureHashBuilderSetup()
    {
        HashBuilderMock.Setup(x => x.Build(It.IsAny<IDescriptor>()))
            .Returns(new DescriptorStableHashes
            {
                ContractHash = new CanonicalHash
                {
                    Algorithm = "SHA-256",
                    AlgorithmVersion = "sha256-canonical-json-v1",
                    ArtifactKind = CanonicalHashArtifactNames.Descriptor,
                    Scope = CanonicalHashScopeNames.InternalFull,
                    Purpose = CanonicalHashPurposeNames.Contract,
                    ContractVersion = "canonical-hash-v1",
                    CanonicalShapeVersion = "test-contract-hash-v1",
                    Value = "test-contract-hash"
                },
                DefinitionHash = new CanonicalHash
                {
                    Algorithm = "SHA-256",
                    AlgorithmVersion = "sha256-canonical-json-v1",
                    ArtifactKind = CanonicalHashArtifactNames.Descriptor,
                    Scope = CanonicalHashScopeNames.InternalFull,
                    Purpose = CanonicalHashPurposeNames.Definition,
                    ContractVersion = "canonical-hash-v1",
                    CanonicalShapeVersion = "test-definition-hash-v1",
                    Value = "test-definition-hash"
                }
            });
    }

    /// <summary>
    /// In-memory store shared across ActivationRequestServiceMock callbacks to
    /// maintain state between Create, Get, and Cancel calls within a test.
    /// Cleared before each test setup via <see cref="EnsureActivationRequestServiceSetup"/>.
    /// </summary>
    private readonly ConcurrentDictionary<(string TenantId, string RequestId), ActivationRequest> _mockActivationRequests = new();

    /// <summary>
    /// Sets up ActivationRequestServiceMock to return a default Submitted ActivationRequest
    /// when CreateActivationRequestAsync is called, and to route Get/Cancel through an
    /// in-memory store. Tests that need specific behavior (e.g., Blocked, RequiresHumanReview)
    /// should override this setup before calling <c>CreateService()</c>.
    /// </summary>
    private void EnsureActivationRequestServiceSetup()
    {
        _mockActivationRequests.Clear();

        ActivationRequestServiceMock
            .Setup(x => x.CreateActivationRequestAsync(
                It.IsAny<AgentToolInvocationContext>(),
                It.IsAny<SubmitActivationRequestRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentToolInvocationContext ctx, SubmitActivationRequestRequest req, CancellationToken _) =>
            {
                var activationRequest = new ActivationRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    TenantId = ctx.TenantId,
                    DraftId = req.DraftId,
                    Status = ActivationRequestStatus.Submitted,
                    SubmittedAt = DateTimeOffset.UtcNow,
                    SubmittedBy = ctx.ActorId,
                    CreatedByActorId = ctx.ActorId,
                    CreatedByActorKind = DescriptorActivationActorKindExtensions.FromAgentToolActorKind(ctx.ActorKind)
                        ?? DescriptorActivationActorKind.System,
                    GovernanceDecision = DescriptorLifecycleDecisionKind.Allowed,
                    Eligibility = DescriptorActivationEligibility.AutoActivatable,
                    Policy = new DescriptorActivationPolicy
                    {
                        RequireHumanReviewForAll = false,
                        ForbidSelfApproval = true,
                        AutoActivateAllowedWhenPolicyPermits = true
                    },
                    BindingSnapshot = req.BindingSnapshot,
                    Diagnostics = []
                };
                _mockActivationRequests[(ctx.TenantId, activationRequest.RequestId)] = activationRequest;
                return AgentToolResult<ActivationRequest>.Success(activationRequest);
            });

        ActivationRequestServiceMock
            .Setup(x => x.GetActivationRequestStatusAsync(
                It.IsAny<AgentToolInvocationContext>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentToolInvocationContext ctx, string requestId, CancellationToken _) =>
            {
                if (_mockActivationRequests.TryGetValue((ctx.TenantId, requestId), out var request))
                    return AgentToolResult<ActivationRequest>.Success(request);
                return AgentToolResult<ActivationRequest>.NotFound($"Activation request '{requestId}' not found.");
            });

        ActivationRequestServiceMock
            .Setup(x => x.CancelActivationRequestAsync(
                It.IsAny<AgentToolInvocationContext>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentToolInvocationContext ctx, string requestId, string reason, CancellationToken _) =>
            {
                if (!_mockActivationRequests.TryGetValue((ctx.TenantId, requestId), out var request))
                    return AgentToolResult<ActivationRequest>.NotFound($"Activation request '{requestId}' not found.");
                if (request.Status is ActivationRequestStatus.Approved or ActivationRequestStatus.Rejected)
                {
                    return AgentToolResult<ActivationRequest>.InvalidRequest(
                        [new AgentToolDiagnostic
                        {
                            Code = new DiagnosticCode("ACTIVATION_REQUEST_TERMINAL"),
                            Severity = SeverityLevel.Error,
                            Message = $"Activation request '{requestId}' is in terminal state '{request.Status}' and cannot be cancelled."
                        }]);
                }
                var cancelled = request with { Status = ActivationRequestStatus.Cancelled };
                _mockActivationRequests[(ctx.TenantId, requestId)] = cancelled;
                return AgentToolResult<ActivationRequest>.Success(cancelled);
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

    public override DraftAbstractions.DescriptorDraftPayload Snapshot() => new TestDraftPayload(_kind, _id, _name);
}
