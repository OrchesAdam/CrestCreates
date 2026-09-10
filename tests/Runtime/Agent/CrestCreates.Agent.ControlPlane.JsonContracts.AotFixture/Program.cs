using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using CrestCreates.Agent.ControlPlane;
using CrestCreates.Agent.ControlPlane.Activation;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Localization.Services;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Delivery.Abstractions.Handlers;
using CrestCreates.Runtime.Delivery.Abstractions.Messages;
using CrestCreates.Runtime.Delivery.Abstractions.Stores;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Persistence.State;
using Microsoft.Extensions.Logging.Abstractions;

public static class ControlPlaneJsonContractFixtureRunner
{
    private static readonly CanonicalHash FixtureHash = new()
    {
        Value = "abc123",
        Algorithm = "SHA-256",
        AlgorithmVersion = "sha256-canonical-json-v1",
        ArtifactKind = "Descriptor",
        Scope = "InternalFull",
        Purpose = "Contract",
        ContractVersion = "canonical-hash-v1",
        CanonicalShapeVersion = "schema-contract-hash-v1"
    };

    public static int Main()
    {
        try
        {
            var context = AgentControlPlaneToolJsonSerializerContext.Default;
            var options = context.Options;

            var reflectionFallbackDisabled = options.TypeInfoResolver is not DefaultJsonTypeInfoResolver
                && !options.TypeInfoResolverChain.Any(resolver => resolver is DefaultJsonTypeInfoResolver);

            if (!reflectionFallbackDisabled)
            {
                Console.Error.WriteLine("FAIL: DefaultJsonTypeInfoResolver found in resolver chain.");
                return 1;
            }

            Console.WriteLine($"ReflectionFallback_IsDisabled:{(reflectionFallbackDisabled ? "PASS" : "FAIL")}");

            bool allPassed = true;

            allPassed &= RoundTrip<DescriptorSearchRequest>(
                new DescriptorSearchRequest { NameContains = "test-query", MaxResults = 10 },
                "DescriptorSearchRequest");

            allPassed &= RoundTrip<AgentToolResult<string>>(
                AgentToolResult<string>.Success("hello-aot"),
                "AgentToolResult<string>");

            allPassed &= RoundTrip<DescriptorActivationReviewDecision>(
                new DescriptorActivationReviewDecision
                {
                    ActivationRequestId = "act-1",
                    TenantId = "tenant-1",
                    CorrelationId = "corr-1",
                    Decision = DescriptorActivationReviewOutcome.Approved,
                    ActorKind = DescriptorActivationActorKind.Human,
                    ActorId = "reviewer-1",
                    Reason = "AOT fixture test",
                    DecidedAt = DateTimeOffset.UtcNow,
                    BoundEvidenceHash = FixtureHash,
                    BoundEnvelopeHash = FixtureHash
                },
                "DescriptorActivationReviewDecision");

            allPassed &= RoundTrip<CanonicalHash>(FixtureHash, "CanonicalHash");

            var localizedCatalog = new DefaultDescriptorReviewMessageTemplateCatalog(
                new KeyReturningLocalizationService("zh-CN"),
                NullLogger<DefaultDescriptorReviewMessageTemplateCatalog>.Instance);
            var localizedMessage = localizedCatalog.Format(
                DescriptorActivationMessageTemplateIds.ActivationBlocked,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["BlockingReasons"] = "策略检查"
                });
            var localizedMessagePassed = string.Equals(
                localizedMessage,
                "草稿不符合激活条件：策略检查。",
                StringComparison.Ordinal);
            allPassed &= localizedMessagePassed;
            if (localizedMessagePassed)
                Console.WriteLine("CONTROL_PLANE_LOCALIZED_MESSAGE_NATIVEAOT_OK");
            else
                Console.Error.WriteLine($"FAIL: localized descriptor-governance message was '{localizedMessage}'.");

            bool unregisteredTypeRejected = false;
            try
            {
                var unregisteredTypeInfo = context.GetTypeInfo(typeof(System.Net.Http.HttpClient));
                unregisteredTypeRejected = unregisteredTypeInfo is null;
            }
            catch (NotSupportedException)
            {
                unregisteredTypeRejected = true;
            }

            if (!unregisteredTypeRejected)
            {
                Console.Error.WriteLine("FAIL: Unregistered type resolved — fail-closed violated.");
                allPassed = false;
            }

            allPassed &= RunActivationReviewCallbackBoundary();

            Console.WriteLine($"SerializeDeserialize_RepresentativeToolRoots:{(allPassed ? "PASS" : "FAIL")}");

            if (reflectionFallbackDisabled && allPassed)
            {
                Console.WriteLine("CONTROL_PLANE_JSON_CONTRACT_NATIVEAOT_OK");
                return 0;
            }

            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"UNEXPECTED: {ex.Message}");
            return 3;
        }
    }

    private static bool RunActivationReviewCallbackBoundary()
    {
        try
        {
            var stateBuilder = new RuntimeStateContractBuilder();
            new DescriptorActivationRuntimeStateContractContributor().Contribute(stateBuilder);
            var stateRegistry = stateBuilder.Build();
            var taskStore = new FixtureHumanTaskStore();
            var orchestrator = new RecordingActivationReviewOrchestrator();
            var handler = new DescriptorActivationReviewHumanTaskEventHandler(
                orchestrator,
                taskStore,
                NullLogger<DescriptorActivationReviewHumanTaskEventHandler>.Instance,
                stateRegistry);

            var pin = new RuntimeDescriptorPin
            {
                Ref = new DescriptorRef("humantask", DescriptorActivationHumanTaskIds.ActivationReview, 1),
                ContractHash = FixtureHash with { Purpose = "Contract" },
                DefinitionHash = FixtureHash with { Purpose = "Definition" }
            };
            var taskKey = new RuntimeInstanceKey("tenant-a", "task-a");
            var requestId = "activation-a";
            var completionEventId = "completion-a";
            var input = new DescriptorActivationReviewTaskInput
            {
                ActivationRequestId = requestId,
                DraftId = "draft-a",
                TenantId = "tenant-a",
                Eligibility = DescriptorActivationEligibility.RequiresHumanReview,
                GovernanceDecision = "ReviewRequired",
                PolicySummary = "ForbidSelfApproval=true",
                CorrelationId = "corr-a"
            };
            var decision = new DescriptorActivationReviewDecision
            {
                ActivationRequestId = requestId,
                TenantId = "tenant-a",
                CorrelationId = "corr-a",
                Decision = DescriptorActivationReviewOutcome.Approved,
                ActorKind = DescriptorActivationActorKind.Human,
                ActorId = "reviewer-a",
                Reason = "approve",
                DecidedAt = DateTimeOffset.UtcNow,
                BoundEvidenceHash = FixtureHash,
                BoundEnvelopeHash = FixtureHash
            };

            taskStore.Set(new HumanTaskInstance
            {
                Key = taskKey,
                HumanTaskPin = pin,
                Status = HumanTaskInstanceStatus.Completed,
                Input = stateRegistry.Capture(input),
                Output = stateRegistry.Capture(decision),
                Outcome = "Approve",
                CompletionEventId = completionEventId,
                Revision = 1
            });

            var approve = CompleteEvent(taskKey, pin, completionEventId, "Approve", "reviewer-a", stateRegistry.Capture(decision));
            var approveResult = handler.ConsumeAsync(approve, DeliveryContext(approve)).AsTask().GetAwaiter().GetResult();
            var rejectKey = new RuntimeInstanceKey("tenant-a", "task-reject");
            var rejectedDecision = decision with { Decision = DescriptorActivationReviewOutcome.Rejected };
            taskStore.Set(CompletedTask(rejectKey, pin, stateRegistry.Capture(input), stateRegistry.Capture(rejectedDecision), "Reject", "completion-reject"));
            var reject = CompleteEvent(rejectKey, pin, "completion-reject", "Reject", "reviewer-a", stateRegistry.Capture(rejectedDecision));
            var rejectResult = handler.ConsumeAsync(reject, DeliveryContext(reject)).AsTask().GetAwaiter().GetResult();
            var requestMismatchKey = new RuntimeInstanceKey("tenant-a", "task-request-mismatch");
            var requestMismatchDecision = decision with { ActivationRequestId = "activation-b" };
            taskStore.Set(CompletedTask(requestMismatchKey, pin, stateRegistry.Capture(input), stateRegistry.Capture(requestMismatchDecision), "Approve", "completion-request-mismatch"));
            var requestMismatch = CompleteEvent(requestMismatchKey, pin, "completion-request-mismatch", "Approve", "reviewer-a", stateRegistry.Capture(requestMismatchDecision));
            var requestMismatchResult = handler.ConsumeAsync(requestMismatch, DeliveryContext(requestMismatch)).AsTask().GetAwaiter().GetResult();
            var mismatchKey = new RuntimeInstanceKey("tenant-a", "task-outcome-mismatch");
            taskStore.Set(CompletedTask(mismatchKey, pin, stateRegistry.Capture(input), stateRegistry.Capture(decision), "Reject", "completion-mismatch"));
            var mismatch = CompleteEvent(mismatchKey, pin, "completion-mismatch", "Reject", "reviewer-a", stateRegistry.Capture(decision));
            var mismatchResult = handler.ConsumeAsync(mismatch, DeliveryContext(mismatch)).AsTask().GetAwaiter().GetResult();
            var actorMismatchKey = new RuntimeInstanceKey("tenant-a", "task-actor-mismatch");
            taskStore.Set(CompletedTask(actorMismatchKey, pin, stateRegistry.Capture(input), stateRegistry.Capture(decision), "Approve", "completion-actor"));
            var actorMismatch = CompleteEvent(actorMismatchKey, pin, "completion-actor", "Approve", "creator-a", stateRegistry.Capture(decision));
            var actorMismatchResult = handler.ConsumeAsync(actorMismatch, DeliveryContext(actorMismatch)).AsTask().GetAwaiter().GetResult();

            var passed = approveResult.Outcome == OutboxDeliveryOutcome.Accepted
                && rejectResult.Outcome == OutboxDeliveryOutcome.Accepted
                && requestMismatchResult.Outcome == OutboxDeliveryOutcome.Conflict
                && mismatchResult.Outcome == OutboxDeliveryOutcome.Conflict
                && actorMismatchResult.Outcome == OutboxDeliveryOutcome.Conflict
                && orchestrator.DispatchCount == 2;
            Console.WriteLine($"ActivationReviewCallbackBoundary:{(passed ? "PASS" : "FAIL")}");
            if (!passed)
            {
                Console.Error.WriteLine($"FAIL: approve={approveResult.Outcome}, reject={rejectResult.Outcome}, requestMismatch={requestMismatchResult.Outcome}, outcomeMismatch={mismatchResult.Outcome}, actorMismatch={actorMismatchResult.Outcome}, dispatches={orchestrator.DispatchCount}");
            }
            return passed;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL [ActivationReviewCallbackBoundary]: {ex.Message}");
            return false;
        }
    }

    private static HumanTaskCompletedEvent CompleteEvent(
        RuntimeInstanceKey key,
        RuntimeDescriptorPin pin,
        string eventId,
        string outcome,
        string actorId,
        RuntimeStateValue result)
        => new()
        {
            EventId = eventId,
            HumanTaskKey = key,
            HumanTaskPin = pin,
            Outcome = outcome,
            ActorId = actorId,
            Result = result
        };

    private static HumanTaskInstance CompletedTask(
        RuntimeInstanceKey key,
        RuntimeDescriptorPin pin,
        RuntimeStateValue input,
        RuntimeStateValue output,
        string outcome,
        string completionEventId)
        => new()
        {
            Key = key,
            HumanTaskPin = pin,
            Status = HumanTaskInstanceStatus.Completed,
            Input = input,
            Output = output,
            Outcome = outcome,
            CompletionEventId = completionEventId,
            Revision = 1
        };

    private static OutboxDeliveryContext DeliveryContext(HumanTaskCompletedEvent completed)
        => new()
        {
            Message = new OutboxMessage
            {
                Metadata = new OutboxMessageMetadata
                {
                    MessageId = completed.EventId,
                    TenantId = completed.HumanTaskKey.TenantId,
                    ContractId = "humantask.completed",
                    RequiredConsumerIds = [DescriptorActivationReviewHumanTaskEventHandler.ConsumerIdValue],
                    OccurredAt = DateTimeOffset.UtcNow,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                Payload = [],
                Integrity = FixtureHash
            },
            Lease = new OutboxDeliveryLease
            {
                OwnerId = "aot-fixture",
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1),
                Attempt = 1,
                Fence = 1
            },
            AttemptDeadline = DateTimeOffset.UtcNow.AddMinutes(1),
            Services = new EmptyServiceProvider()
        };

    private sealed class RecordingActivationReviewOrchestrator : IActivationReviewOrchestrator
    {
        public int DispatchCount { get; private set; }

        public Task<AgentToolResult<string>> CreateActivationReviewTaskAsync(
            AgentToolInvocationContext context, ActivationRequest activationRequest,
            DescriptorActivationPolicy policy, CancellationToken ct = default)
            => Task.FromResult(AgentToolResult<string>.Success("aot-task"));

        public Task<ActivationReviewDispatchOutcome> ProcessReviewDecisionAsync(
            DescriptorActivationReviewDecision reviewDecision,
            string completionEventId,
            CancellationToken ct = default)
        {
            DispatchCount++;
            return Task.FromResult(ActivationReviewDispatchOutcome.Accepted);
        }
    }

    private sealed class FixtureHumanTaskStore : IHumanTaskInstanceStore
    {
        private readonly Dictionary<RuntimeInstanceKey, HumanTaskInstance> _items = [];

        public void Set(HumanTaskInstance instance) => _items[instance.Key] = instance;
        public Task AddAsync(HumanTaskInstance instance, CancellationToken cancellationToken = default) { Set(instance); return Task.CompletedTask; }
        public Task UpdateAsync(HumanTaskInstance instance, long expectedRevision, CancellationToken cancellationToken = default) { Set(instance); return Task.CompletedTask; }
        public Task<HumanTaskInstance?> GetAsync(RuntimeInstanceKey key, CancellationToken cancellationToken = default) => Task.FromResult(_items.GetValueOrDefault(key));
        public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByAssigneeAsync(RuntimeTenantScope scope, string assigneeUserId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<HumanTaskInstance>>([]);
        public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByWorkflowAsync(RuntimeInstanceKey workflowKey, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<HumanTaskInstance>>([]);
        public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateUserAsync(RuntimeTenantScope scope, string userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<HumanTaskInstance>>([]);
        public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateRoleAsync(RuntimeTenantScope scope, string roleId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<HumanTaskInstance>>([]);
        public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByOrganizationAsync(RuntimeTenantScope scope, string organizationUnitId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<HumanTaskInstance>>([]);
        public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByPositionAsync(RuntimeTenantScope scope, string positionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<HumanTaskInstance>>([]);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static bool RoundTrip<T>(T value, string label) where T : notnull
    {
        try
        {
            var context = AgentControlPlaneToolJsonSerializerContext.Default;
            var json = JsonSerializer.Serialize(value, typeof(T), context);
            var deserialized = JsonSerializer.Deserialize(json, typeof(T), context);
            if (deserialized is null)
            {
                Console.Error.WriteLine($"FAIL [{label}]: deserialized null.");
                return false;
            }

            var reJson = JsonSerializer.Serialize(deserialized, typeof(T), context);
            if (json != reJson)
            {
                Console.Error.WriteLine($"FAIL [{label}]: round-trip mismatch.");
                Console.Error.WriteLine($"  Original:  {json}");
                Console.Error.WriteLine($"  RoundTrip: {reJson}");
                return false;
            }

            Console.WriteLine($"  OK [{label}]");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL [{label}]: {ex.Message}");
            return false;
        }
    }

    private sealed class KeyReturningLocalizationService(string currentCulture) : ILocalizationService
    {
        public string CurrentCulture { get; } = currentCulture;
        public string GetString(string key) => key;
        public string GetString(string key, params object[] arguments) => key;
        public string GetString(string key, string cultureName) => key;
        public string GetString(string key, string cultureName, params object[] arguments) => key;
        public Task<string?> GetStringAsync(string key) => Task.FromResult<string?>(key);
        public Task<string?> GetStringAsync(string key, params object[] arguments) => Task.FromResult<string?>(key);
        public Task<string?> GetStringAsync(string key, string cultureName) => Task.FromResult<string?>(key);
        public Task<string?> GetStringAsync(string key, string cultureName, params object[] arguments) => Task.FromResult<string?>(key);
        public IDisposable ChangeCulture(string cultureName) => throw new NotSupportedException();
        public Task<IDisposable> ChangeCultureAsync(string cultureName) => throw new NotSupportedException();
    }
}
