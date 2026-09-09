using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Activation;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using CrestCreates.Runtime.Delivery.Abstractions.Registration;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Persistence.Abstractions.Transactions;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CrestCreates.Agent.ControlPlane.Tests.Activation;

/// <summary>
/// Regression boundary for completion facts. The task completion event is produced by
/// DefaultHumanTaskRuntime.CompleteAsync and then passed through the production callback
/// handler/orchestrator/request service. The request, evidence, and gate collaborators are
/// in-memory substitutes; this does not claim package generation, registry deployment, or PG durability.
/// </summary>
public sealed class ActivationReviewCompletionBindingRegressionTests : AgentControlPlaneTestBase
{
    private const string ReviewTaskId = "descriptor-activation-review";
    private const string ReviewerId = "reviewer-001";
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public ActivationReviewCompletionBindingRegressionTests(Xunit.Abstractions.ITestOutputHelper output)
        => _output = output;

    private static Task<Fixture> CreateFixtureAsync(string requestId, string creatorId)
        => Fixture.CreateAsync(requestId, creatorId);

    [Fact]
    public async Task CanonicalRejectCannotBecomeApprovalFromTypedResult()
    {
        var fixture = await CreateFixtureAsync("request-a", TestActorId);

        var completion = await fixture.CompleteAsync(
            "Reject", CreateDecision(fixture.RequestId, DescriptorActivationReviewOutcome.Approved, ReviewerId),
            actorId: ReviewerId);
        var outcome = await fixture.Handler.HandleAsync(completion);
        var status = await fixture.GetStatusAsync(fixture.RequestId);
        var gateCalls = fixture.ActivationGate.Invocations.Count(invocation => invocation.Method.Name == nameof(IRuntimeActivationGate.ActivateAsync));
        _output.WriteLine($"canonical-reject/typed-approve: dispatch={outcome}, requestStatus={status}, gateActivateCalls={gateCalls}");

        using (new AssertionScope())
        {
            outcome.Should().Be(ActivationReviewDispatchOutcome.Conflict);
            status.Should().Be(ActivationRequestStatus.UnderReview);
            gateCalls.Should().Be(0);
        }
    }

    [Fact]
    public async Task CompletionForRequestAIsNotAllowedToApproveRequestB()
    {
        var fixture = await CreateFixtureAsync("request-a", TestActorId);
        var requestB = await fixture.CreateRequestAsync("request-b", "creator-b");

        var completion = await fixture.CompleteAsync(
            "Approve", CreateDecision(requestB, DescriptorActivationReviewOutcome.Approved, ReviewerId),
            actorId: ReviewerId);
        var outcome = await fixture.Handler.HandleAsync(completion);
        var statusA = await fixture.GetStatusAsync(fixture.RequestId);
        var statusB = await fixture.GetStatusAsync(requestB);
        var gateCalls = fixture.ActivationGate.Invocations.Count(invocation => invocation.Method.Name == nameof(IRuntimeActivationGate.ActivateAsync));
        _output.WriteLine($"task-A/result-B: dispatch={outcome}, requestAStatus={statusA}, requestBStatus={statusB}, gateActivateCalls={gateCalls}");

        using (new AssertionScope())
        {
            outcome.Should().Be(ActivationReviewDispatchOutcome.Conflict);
            statusA.Should().Be(ActivationRequestStatus.UnderReview);
            statusB.Should().Be(ActivationRequestStatus.UnderReview);
            gateCalls.Should().Be(0);
        }
    }

    [Fact]
    public async Task TaskCreatorCannotBypassSelfApprovalBySpoofingTypedActor()
    {
        var fixture = await CreateFixtureAsync("request-a", TestActorId);

        var completion = await fixture.CompleteAsync(
            "Approve", CreateDecision(fixture.RequestId, DescriptorActivationReviewOutcome.Approved, ReviewerId),
            actorId: TestActorId);
        var outcome = await fixture.Handler.HandleAsync(completion);
        var status = await fixture.GetStatusAsync(fixture.RequestId);
        var gateCalls = fixture.ActivationGate.Invocations.Count(invocation => invocation.Method.Name == nameof(IRuntimeActivationGate.ActivateAsync));
        _output.WriteLine($"creator-event/spoofed-actor: dispatch={outcome}, requestStatus={status}, gateActivateCalls={gateCalls}");

        using (new AssertionScope())
        {
            outcome.Should().Be(ActivationReviewDispatchOutcome.Conflict);
            status.Should().Be(ActivationRequestStatus.UnderReview);
            gateCalls.Should().Be(0);
        }
    }

    [Fact]
    public async Task HonestAuthorizedApprovalStillUsesRequestServiceAndGate()
    {
        var fixture = await CreateFixtureAsync("request-a", TestActorId);

        var completion = await fixture.CompleteAsync(
            "Approve", CreateDecision(fixture.RequestId, DescriptorActivationReviewOutcome.Approved, ReviewerId),
            actorId: ReviewerId);
        var outcome = await fixture.Handler.HandleAsync(completion);

        outcome.Should().Be(ActivationReviewDispatchOutcome.Accepted);
        (await fixture.GetStatusAsync(fixture.RequestId)).Should().Be(ActivationRequestStatus.Activated);
        fixture.ActivationGate.Verify(x => x.ActivateAsync(
            It.Is<AgentToolInvocationContext>(context => context.ActorId == ReviewerId),
            It.Is<ActivationRequest>(request => request.RequestId == fixture.RequestId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static DescriptorActivationReviewDecision CreateDecision(
        string requestId, DescriptorActivationReviewOutcome outcome, string actorId)
        => new()
        {
            ActivationRequestId = requestId,
            TenantId = TestTenantId,
            CorrelationId = TestCorrelationId,
            Decision = outcome,
            ActorKind = DescriptorActivationActorKind.Human,
            ActorId = actorId,
            Reason = outcome == DescriptorActivationReviewOutcome.Approved ? "approve" : "reject",
            DecidedAt = DateTimeOffset.UtcNow,
            BoundEvidenceHash = TestHash("evidence-hash", CanonicalHashArtifactNames.PackageEvidence, CanonicalHashPurposeNames.AuditEvidence),
            BoundEnvelopeHash = TestHash("envelope-hash", CanonicalHashArtifactNames.PackageEvidenceEnvelope, CanonicalHashPurposeNames.AuditEvidence)
        };

    private static CanonicalHash TestHash(string value, string artifactKind, string purpose)
        => new()
        {
            Value = value,
            Algorithm = "SHA-256",
            AlgorithmVersion = "sha256-canonical-json-v1",
            ArtifactKind = artifactKind,
            Scope = CanonicalHashScopeNames.InternalFull,
            Purpose = purpose,
            ContractVersion = "canonical-hash-v1",
            CanonicalShapeVersion = "test-v1"
        };

    private static BindingHashes TestBindingHashes()
        => new()
        {
            SourceReviewHash = TestHash("source-review", CanonicalHashArtifactNames.ReviewResult, CanonicalHashPurposeNames.SourceBinding),
            ReviewManifestHash = TestHash("review-manifest", CanonicalHashArtifactNames.ReviewResult, CanonicalHashPurposeNames.Integrity),
            PackageManifestHash = TestHash("package-manifest", CanonicalHashArtifactNames.PackageManifest, CanonicalHashPurposeNames.Integrity),
            PackageEvidenceHash = TestHash("evidence-hash", CanonicalHashArtifactNames.PackageEvidence, CanonicalHashPurposeNames.AuditEvidence),
            PackageEvidenceEnvelopeHash = TestHash("envelope-hash", CanonicalHashArtifactNames.PackageEvidenceEnvelope, CanonicalHashPurposeNames.AuditEvidence),
            ContractHash = TestHash("contract", CanonicalHashArtifactNames.Descriptor, CanonicalHashPurposeNames.Contract),
            DefinitionHash = TestHash("definition", CanonicalHashArtifactNames.Descriptor, CanonicalHashPurposeNames.Definition)
        };

    private sealed class Fixture
    {
        private InMemoryHumanTaskStore _taskStore = null!;
        private RuntimeStateCapture _state = null!;
        private DefaultHumanTaskRuntime _humanTaskRuntime = null!;
        private IDescriptorActivationRequestService _requestService = null!;

        public DescriptorActivationReviewHumanTaskEventHandler Handler { get; set; } = null!;
        public required Mock<IRuntimeActivationGate> ActivationGate { get; init; }
        public string RequestId { get; set; } = string.Empty;

        public async Task<string> CreateRequestAsync(string requestId, string creatorId)
        {
            var result = await _requestService.CreateActivationRequestAsync(
                new AgentToolInvocationContext
                {
                    TenantId = TestTenantId,
                    ActorId = creatorId,
                    ActorKind = AgentToolActorKind.Human,
                    CorrelationId = TestCorrelationId,
                    ToolName = "SubmitActivationRequest",
                    InvocationSource = AgentToolInvocationSource.Direct
                },
                new SubmitActivationRequestRequest
                {
                    DraftId = requestId,
                    GovernanceDecision = DescriptorLifecycleDecisionKind.Allowed,
                    BindingSnapshot = new ActivationBindingSnapshot
                    {
                        TenantId = TestTenantId,
                        DraftId = requestId,
                        DraftVersion = 1,
                        ReviewResultId = $"review-{requestId}",
                        PackagePreviewId = $"package-{requestId}",
                        EvidencePreviewId = $"evidence-{requestId}",
                        Hashes = TestBindingHashes(),
                        CorrelationId = TestCorrelationId,
                        CreatedAt = DateTimeOffset.UtcNow
                    }
                });
            result.Status.Should().Be(AgentToolResultStatus.Success);
            result.Value.Should().NotBeNull();
            result.Value!.Status.Should().Be(ActivationRequestStatus.UnderReview);
            return result.Value.RequestId;
        }

        public async Task<HumanTaskCompletedEvent> CompleteAsync(
            string outcome, DescriptorActivationReviewDecision decision, string actorId)
        {
            var result = _state.Capture(decision);
            await _humanTaskRuntime.CompleteAsync(new HumanTaskCompletionRequest
            {
                HumanTaskKey = new RuntimeInstanceKey(TestTenantId, "review-task"),
                Outcome = outcome,
                ActorId = actorId,
                ActorRoles = ["reviewer"],
                Result = result
            });
            return _messageFactory!.CompletedEvent!;
        }

        public async Task<ActivationRequestStatus> GetStatusAsync(string requestId)
        {
            var result = await _requestService.GetActivationRequestStatusAsync(
                new AgentToolInvocationContext
                {
                    TenantId = TestTenantId,
                    ActorId = ReviewerId,
                    ActorKind = AgentToolActorKind.Human,
                    CorrelationId = TestCorrelationId,
                    ToolName = "GetActivationRequestStatus",
                    InvocationSource = AgentToolInvocationSource.Direct
                }, requestId);
            result.Value.Should().NotBeNull();
            return result.Value!.Status;
        }

        private CapturingMessageFactory? _messageFactory;

        public static async Task<Fixture> CreateAsync(string requestId, string creatorId)
        {
            var policy = new Mock<IDescriptorActivationPolicyProvider>();
            policy.Setup(x => x.GetPolicyAsync(TestTenantId, It.IsAny<DescriptorKind?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DescriptorActivationPolicy
                {
                    RequireHumanReviewForAll = true,
                    ForbidSelfApproval = true,
                    AutoActivateAllowedWhenPolicyPermits = true
                });
            var draftStore = new Mock<IDescriptorDraftStore>();
            draftStore.Setup(x => x.GetAsync(TestTenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string tenant, string draftId, CancellationToken _) => CreateTestDraft(draftId, tenant));
            var gate = new Mock<IRuntimeActivationGate>();
            gate.Setup(x => x.ActivateAsync(It.IsAny<AgentToolInvocationContext>(), It.IsAny<ActivationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((AgentToolInvocationContext context, ActivationRequest request, CancellationToken _) =>
                    AgentToolResult<RuntimeActivationGateResult>.Success(new RuntimeActivationGateResult
                    {
                        ActivatedDescriptorRef = "activated:test",
                        DraftId = request.DraftId,
                        TenantId = request.TenantId,
                        ActivatedAt = DateTimeOffset.UtcNow
                    }));
            var evidence = new Mock<IActivationEvidenceRechecker>();
            evidence.Setup(x => x.RecheckAsync(It.IsAny<string>(), It.IsAny<ActivationBindingSnapshot>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ActivationEvidenceRecheckResult { IsStale = false, Drifts = [] });
            var requestService = new DefaultDescriptorActivationRequestService(
                policy.Object, new InMemoryDescriptorActivationAuditor(),
                new Mock<IDescriptorStableHashBuilder>().Object, draftStore.Object, gate.Object, evidence.Object,
                new ActivationBindingHashValidator(), NullLogger<DefaultDescriptorActivationRequestService>.Instance);

            var fixture = new Fixture
            {
                ActivationGate = gate,
                RequestId = requestId,
                _taskStore = new InMemoryHumanTaskStore(),
                _state = new RuntimeStateCapture(),
                _requestService = requestService
            };
            // The service allocates the durable request identity. Bind the task input to
            // that returned identity before creating the real HumanTask instance.
            fixture.RequestId = await fixture.CreateRequestAsync(requestId, creatorId);

            var state = fixture._state;
            var taskStore = fixture._taskStore;
            var taskDescriptor = new HumanTaskDescriptor
            {
                Id = ReviewTaskId,
                Name = ReviewTaskId,
                Version = 1,
                Outcomes = [new CompletionOutcome { Condition = CompletionCondition.Approve }, new CompletionOutcome { Condition = CompletionCondition.Reject }]
            };
            var pin = new RuntimeDescriptorPin
            {
                Ref = new DescriptorRef("humantask", ReviewTaskId, 1),
                ContractHash = TestHash("task-contract", CanonicalHashArtifactNames.Descriptor, CanonicalHashPurposeNames.Contract),
                DefinitionHash = TestHash("task-definition", CanonicalHashArtifactNames.Descriptor, CanonicalHashPurposeNames.Definition)
            };
            var registry = new Mock<IHumanTaskRegistry>();
            registry.Setup(x => x.GetById(ReviewTaskId)).Returns(taskDescriptor);
            registry.Setup(x => x.GetByVersion(ReviewTaskId, 1)).Returns(taskDescriptor);
            var pinResolver = new Mock<IRuntimeDescriptorPinResolver<HumanTaskDescriptor>>();
            pinResolver.Setup(x => x.Capture(taskDescriptor)).Returns(new ResolvedRuntimeDescriptor<HumanTaskDescriptor> { Descriptor = taskDescriptor, Pin = pin });
            pinResolver.Setup(x => x.Resolve(pin)).Returns(new ResolvedRuntimeDescriptor<HumanTaskDescriptor> { Descriptor = taskDescriptor, Pin = pin });
            var assignee = new Mock<IHumanTaskAssigneeResolver>();
            assignee.Setup(x => x.ResolveAsync(taskDescriptor, It.IsAny<HumanTaskCreationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HumanTaskAssigneeResolution());
            var messageFactory = new CapturingMessageFactory();
            var runtime = new DefaultHumanTaskRuntime(
                registry.Object, taskStore, new Mock<ILocalEventBus>().Object, assignee.Object, pinResolver.Object,
                state, new InlineTransactions(), messageFactory, new CapturingOutbox(),
                [new OutboxRequiredConsumerMetadata(DescriptorActivationReviewHumanTaskEventHandler.ConsumerIdValue)]);
            await runtime.CreateAsync(new HumanTaskCreationRequest
            {
                InstanceId = "review-task",
                HumanTaskId = ReviewTaskId,
                TenantId = TestTenantId,
                Input = state.Capture(new DescriptorActivationReviewTaskInput
                {
                    ActivationRequestId = fixture.RequestId,
                    DraftId = requestId,
                    TenantId = TestTenantId,
                    Eligibility = DescriptorActivationEligibility.RequiresHumanReview,
                    GovernanceDecision = DescriptorLifecycleDecisionKind.Allowed.ToString(),
                    PolicySummary = "ForbidSelfApproval=true",
                    CorrelationId = TestCorrelationId,
                    BoundHashes = TestBindingHashes()
                }),
                RequiredCompletionConsumerIds = [DescriptorActivationReviewHumanTaskEventHandler.ConsumerIdValue]
            });

            var orchestrator = new DefaultActivationReviewOrchestrator(
                new Mock<IServiceScopeFactory>().Object, requestService,
                NullLogger<DefaultActivationReviewOrchestrator>.Instance, state);
            var handler = new DescriptorActivationReviewHumanTaskEventHandler(
                orchestrator, taskStore, NullLogger<DescriptorActivationReviewHumanTaskEventHandler>.Instance, state);
            fixture.Handler = handler;
            fixture._humanTaskRuntime = runtime;
            fixture._messageFactory = messageFactory;
            return fixture;
        }
    }

    private sealed class InMemoryHumanTaskStore : IHumanTaskInstanceStore
    {
        private readonly Dictionary<RuntimeInstanceKey, HumanTaskInstance> _items = [];
        public Task AddAsync(HumanTaskInstance instance, CancellationToken cancellationToken = default) { _items[instance.Key] = instance; return Task.CompletedTask; }
        public Task<HumanTaskInstance?> GetAsync(RuntimeInstanceKey key, CancellationToken cancellationToken = default) => Task.FromResult(_items.GetValueOrDefault(key));
        public Task UpdateAsync(HumanTaskInstance instance, long expectedRevision, CancellationToken cancellationToken = default) { _items[instance.Key] = instance; return Task.CompletedTask; }
        public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByAssigneeAsync(RuntimeTenantScope scope, string assigneeUserId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<HumanTaskInstance>>([]);
        public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByWorkflowAsync(RuntimeInstanceKey workflowKey, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<HumanTaskInstance>>([]);
        public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateUserAsync(RuntimeTenantScope scope, string userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<HumanTaskInstance>>([]);
        public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateRoleAsync(RuntimeTenantScope scope, string roleId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<HumanTaskInstance>>([]);
        public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByOrganizationAsync(RuntimeTenantScope scope, string organizationUnitId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<HumanTaskInstance>>([]);
        public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByPositionAsync(RuntimeTenantScope scope, string positionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<HumanTaskInstance>>([]);
    }

    private sealed class RuntimeStateCapture : IRuntimeStateContractRegistry
    {
        private readonly Dictionary<string, object> _values = [];
        public RuntimeStateValue Capture(object? value) { var key = Guid.NewGuid().ToString("N"); _values[key] = value ?? throw new InvalidOperationException(); return new RuntimeStateValue { TypeId = key, JsonPayload = "{}" }; }
        public RuntimeStateValue Capture<T>(T value) => Capture((object?)value);
        public object? Restore(RuntimeStateValue value) => _values[value.TypeId];
        public T Restore<T>(RuntimeStateValue value) => (T)Restore(value)!;
        public void Validate(RuntimeStateValue value) { _ = _values[value.TypeId]; }
    }

    private sealed class InlineTransactions : IRuntimeTransactionCoordinator
    {
        public ValueTask ExecuteAsync(Func<CancellationToken, ValueTask> work, CancellationToken cancellationToken = default) => work(cancellationToken);
        public ValueTask<T> ExecuteAsync<T>(Func<CancellationToken, ValueTask<T>> work, CancellationToken cancellationToken = default) => work(cancellationToken);
    }

    private sealed class CapturingOutbox : ITransactionalOutboxWriter
    {
        public ValueTask<OutboxAppendResult> AppendAsync(OutboxMessage message, CancellationToken cancellationToken = default) => ValueTask.FromResult(OutboxAppendResult.Appended);
    }

    private sealed class CapturingMessageFactory : IOutboxMessageFactory
    {
        public HumanTaskCompletedEvent? CompletedEvent { get; private set; }
        public OutboxMessage Create<TPayload>(OutboxMessageMetadata metadata, TPayload payload, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TPayload> jsonTypeInfo)
        {
            CompletedEvent = payload as HumanTaskCompletedEvent;
            return Message(metadata);
        }
        public OutboxMessage Create(string messageId, string? tenantId, string contractId, string payloadTypeId, ReadOnlySpan<byte> payload, IEnumerable<string>? requiredConsumerIds = null, DateTimeOffset? createdAt = null)
            => Message(new OutboxMessageMetadata { MessageId = messageId, TenantId = tenantId, ContractId = contractId, PayloadTypeId = payloadTypeId, RequiredConsumerIds = (requiredConsumerIds ?? []).ToArray(), CreatedAt = createdAt ?? DateTimeOffset.UtcNow, OccurredAt = createdAt ?? DateTimeOffset.UtcNow });
        private static OutboxMessage Message(OutboxMessageMetadata metadata) => new() { Metadata = metadata, Payload = [], Integrity = TestHash("outbox", "Outbox", "Integrity") };
    }
}
