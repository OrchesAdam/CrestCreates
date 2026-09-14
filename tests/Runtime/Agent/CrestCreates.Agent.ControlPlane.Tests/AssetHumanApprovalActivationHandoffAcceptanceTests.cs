using System.Text.Json;
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Parsing;
using CrestCreates.Agent.ControlPlane;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Activation;
using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.Event.Abstractions;
using CrestCreates.Event;
using CrestCreates.Form;
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Bootstrap;
using CrestCreates.Metadata.ContextPack;
using CrestCreates.Metadata.DescriptorImpact;
using CrestCreates.Metadata.Registry;
using CrestCreates.Runtime.Persistence;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Runtime.Persistence.InMemory;
using CrestCreates.Runtime.Delivery;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Sample.AssetManagement.Host;
using CrestCreates.Schema;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Bounded proof that the authoring parser's complete Asset candidate can enter the
/// real control-plane review, package, evidence, human approval/rejection, outbox,
/// and activation-handoff pipeline. Host loading and durable reboot are outside this
/// acceptance boundary.
/// </summary>
public sealed class AssetHumanApprovalActivationHandoffAcceptanceTests
{
    private const string TenantId = "asset-approval-tenant";
    private const string AuthorId = "asset-author-agent";

    [Fact]
    public async Task AssetCandidate_ReviewEvidenceSubmit_CreatesUnderReviewTask()
    {
        await using var fixture = await Fixture.CreateAsync();
        var draft = ParseAssetCandidate();
        await fixture.DraftStore.SaveAsync(draft);

        var service = fixture.Services.GetRequiredService<IAgentControlPlaneToolService>();

        var review = await service.ReviewDescriptorDraftAsync(
            fixture.Context(AgentToolName.ReviewDescriptorDraft), draft.DraftId);
        review.Status.Should().Be(AgentToolResultStatus.Success,
            string.Join(" | ", review.Diagnostics.Select(d => d.Message)));
        review.Value!.StableHashes.Should().NotBeNull();
        review.Value.GovernanceSummary.Should().NotBeNull();

        var reviewResultId = fixture.Auditor.GetAllRecords()
            .Last(record => record.TouchedReviewResultIds is { Count: 1 })
            .TouchedReviewResultIds![0];

        var preview = await service.PreviewDescriptorPackageAsync(
            fixture.Context(AgentToolName.PreviewDescriptorPackage), draft.DraftId);
        preview.Status.Should().Be(AgentToolResultStatus.Success,
            string.Join(" | ", preview.Diagnostics.Select(d => d.Message)));
        preview.Value.Should().NotBeNull();

        var packagePreviewId = fixture.Auditor.GetAllRecords()
            .Last(record => record.TouchedPackagePreviewIds is { Count: 1 }
                && record.Context.ToolName == AgentToolName.PreviewDescriptorPackage)
            .TouchedPackagePreviewIds![0];

        var evidence = await service.BuildPackageEvidencePreviewAsync(
            fixture.Context(AgentToolName.BuildPackageEvidencePreview), draft.DraftId);
        evidence.Status.Should().Be(AgentToolResultStatus.Success,
            string.Join(" | ", evidence.Diagnostics.Select(d => d.Message)));
        evidence.Value.Should().NotBeNull();

        var evidencePreviewId = fixture.Auditor.GetAllRecords()
            .Last(record => record.TouchedPackagePreviewIds is { Count: 1 }
                && record.Context.ToolName == AgentToolName.BuildPackageEvidencePreview)
            .TouchedPackagePreviewIds![0];

        var resolved = await fixture.ArtifactResolver.ResolveAsync(
            TenantId,
            new ActivationBindingSnapshot
            {
                TenantId = TenantId,
                DraftId = draft.DraftId,
                DraftVersion = 1,
                ReviewResultId = reviewResultId,
                PackagePreviewId = packagePreviewId,
                EvidencePreviewId = evidencePreviewId,
                Hashes = null!,
                CreatedAt = DateTimeOffset.UtcNow
            });
        var packageHashes = resolved.CurrentPackageHashes;
        packageHashes.Should().NotBeNull();
        var evidenceHashes = resolved.CurrentEvidenceHashes;
        evidenceHashes.Should().NotBeNull();
        var sourceReviewHash = resolved.CurrentSourceReviewHash;
        sourceReviewHash.Should().NotBeNull();
        var reviewManifestHash = resolved.CurrentReviewManifestHash;
        reviewManifestHash.Should().NotBeNull();

        var submit = await service.SubmitActivationRequestAsync(
            fixture.Context(AgentToolName.SubmitActivationRequest),
            new SubmitActivationRequestRequest
            {
                DraftId = draft.DraftId,
                BindingSnapshot = new ActivationBindingSnapshot
                {
                    TenantId = TenantId,
                    DraftId = draft.DraftId,
                    DraftVersion = 1,
                    ReviewResultId = reviewResultId,
                    PackagePreviewId = packagePreviewId,
                    EvidencePreviewId = evidencePreviewId,
                    Hashes = new BindingHashes
                    {
                        SourceReviewHash = sourceReviewHash!,
                        ReviewManifestHash = reviewManifestHash!,
                        PackageManifestHash = packageHashes!.PackageManifestHash,
                        PackageEvidenceHash = packageHashes.PackageEvidenceHash,
                        PackageEvidenceEnvelopeHash = packageHashes.PackageEvidenceEnvelopeHash,
                        ContractHash = review.Value.StableHashes!.ContractHash,
                        DefinitionHash = review.Value.StableHashes.DefinitionHash
                    },
                    CreatedAt = DateTimeOffset.UtcNow
                }
            });

        submit.Status.Should().Be(AgentToolResultStatus.Success,
            string.Join(" | ", submit.Diagnostics.Select(d => d.Message)));
        submit.Value.Should().NotBeNull();
        submit.Value!.Status.Should().Be(ActivationRequestStatus.UnderReview);
        submit.Value.Policy!.RequireHumanReviewForAll.Should().BeTrue();
        submit.Value.Policy.ForbidSelfApproval.Should().BeTrue();

        var reviewTaskId = fixture.ReviewOrchestrator.LastTaskId;
        reviewTaskId.Should().NotBeNullOrWhiteSpace();
        var reviewTask = await fixture.HumanTasks.GetAsync(
            new RuntimeInstanceKey(TenantId, reviewTaskId!))
            ?? throw new InvalidOperationException("Activation review HumanTask was not persisted.");
        reviewTask.HumanTaskPin.Ref.Id.Should().Be(DescriptorActivationHumanTaskIds.ActivationReview.RequireValue());
        reviewTask.HumanTaskPin.Ref.Version.Should().Be(1);
        reviewTask.Status.Should().Be(HumanTaskInstanceStatus.Created);
        reviewTask.RequiredCompletionConsumerIds.Should().Contain(
            DescriptorActivationReviewHumanTaskEventHandler.ConsumerIdValue);

        var taskInput = fixture.State.Restore<DescriptorActivationReviewTaskInput>(reviewTask.Input!);
        taskInput.ActivationRequestId.Should().Be(submit.Value.RequestId);
        taskInput.DraftId.Should().Be(draft.DraftId);
        taskInput.Eligibility.Should().Be(DescriptorActivationEligibility.RequiresHumanReview);
        taskInput.GovernanceDecision.Should().Be(DescriptorLifecycleDecisionKind.Allowed.ToString());
    }

    [Fact]
    public async Task AssetCandidate_HumanApprove_CompletesThroughOutbox_AndActivates()
    {
        await using var fixture = await Fixture.CreateAsync();
        var submitted = await SubmitCandidateAsync(fixture);
        var request = submitted.Request;
        var taskInput = AssertTaskInputBinding(fixture, submitted);

        var decision = new DescriptorActivationReviewDecision
        {
            ActivationRequestId = request.RequestId,
            TenantId = taskInput.TenantId,
            CorrelationId = taskInput.CorrelationId ?? string.Empty,
            Decision = DescriptorActivationReviewOutcome.Approved,
            ActorKind = DescriptorActivationActorKind.Human,
            ActorId = "asset-human-reviewer",
            Reason = "Asset candidate approved by the human reviewer.",
            DecidedAt = DateTimeOffset.UtcNow,
            BoundEvidenceHash = taskInput.BoundHashes!.PackageEvidenceHash,
            BoundEnvelopeHash = taskInput.BoundHashes.PackageEvidenceEnvelopeHash
        };

        using (var scope = fixture.Services.CreateScope())
        {
            var runtime = scope.ServiceProvider.GetRequiredService<IHumanTaskRuntime>();
            var completed = await runtime.CompleteAsync(new HumanTaskCompletionRequest
            {
                HumanTaskKey = submitted.Task.Key,
                Outcome = "Approve",
                ActorId = decision.ActorId,
                ActorRoles = ["AssetReviewer"],
                Result = fixture.State.Capture(decision)
            });
            completed.Status.Should().Be(HumanTaskInstanceStatus.Completed);
            completed.Outcome.Should().Be(CompletionCondition.Approve.ToString());
        }

        var activated = await WaitForActivationStatusAsync(fixture, request.RequestId, ActivationRequestStatus.Activated);
        activated.BindingSnapshot.Should().BeEquivalentTo(request.BindingSnapshot);
        fixture.ActivationGate.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task AssetCandidate_HumanReject_CompletesThroughOutbox_AndRemainsRejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        var submitted = await SubmitCandidateAsync(fixture);
        var request = submitted.Request;
        var taskInput = AssertTaskInputBinding(fixture, submitted);

        var decision = new DescriptorActivationReviewDecision
        {
            ActivationRequestId = request.RequestId,
            TenantId = taskInput.TenantId,
            CorrelationId = taskInput.CorrelationId ?? string.Empty,
            Decision = DescriptorActivationReviewOutcome.Rejected,
            ActorKind = DescriptorActivationActorKind.Human,
            ActorId = "asset-human-rejecter",
            Reason = "Asset candidate rejected by the human reviewer.",
            DecidedAt = DateTimeOffset.UtcNow,
            BoundEvidenceHash = taskInput.BoundHashes!.PackageEvidenceHash,
            BoundEnvelopeHash = taskInput.BoundHashes.PackageEvidenceEnvelopeHash
        };

        using (var scope = fixture.Services.CreateScope())
        {
            var runtime = scope.ServiceProvider.GetRequiredService<IHumanTaskRuntime>();
            var completed = await runtime.CompleteAsync(new HumanTaskCompletionRequest
            {
                HumanTaskKey = submitted.Task.Key,
                Outcome = "Reject",
                ActorId = decision.ActorId,
                ActorRoles = ["AssetReviewer"],
                Result = fixture.State.Capture(decision)
            });
            completed.Status.Should().Be(HumanTaskInstanceStatus.Completed);
            completed.Outcome.Should().Be(CompletionCondition.Reject.ToString());
        }

        var rejected = await WaitForActivationStatusAsync(fixture, request.RequestId, ActivationRequestStatus.Rejected);
        rejected.Status.Should().Be(ActivationRequestStatus.Rejected);
        rejected.BindingSnapshot.Should().BeEquivalentTo(request.BindingSnapshot);
        fixture.ActivationGate.CallCount.Should().Be(0);
    }

    private static DescriptorActivationReviewTaskInput AssertTaskInputBinding(
        Fixture fixture,
        (Draft Draft, ActivationRequest Request, HumanTaskInstance Task) submitted)
    {
        var taskInput = fixture.State.Restore<DescriptorActivationReviewTaskInput>(submitted.Task.Input!);
        taskInput.TenantId.Should().Be(TenantId);
        taskInput.DraftId.Should().Be(submitted.Draft.DraftId);
        taskInput.ActivationRequestId.Should().Be(submitted.Request.RequestId);
        taskInput.Eligibility.Should().Be(DescriptorActivationEligibility.RequiresHumanReview);
        taskInput.GovernanceDecision.Should().Be(DescriptorLifecycleDecisionKind.Allowed.ToString());
        taskInput.BoundHashes.Should().BeEquivalentTo(submitted.Request.BindingSnapshot.Hashes);
        submitted.Task.TenantId.Should().Be(TenantId);
        return taskInput;
    }

    private static async Task<ActivationRequest> WaitForActivationStatusAsync(
        Fixture fixture, string requestId, ActivationRequestStatus expected)
    {
        var service = fixture.Services.GetRequiredService<IAgentControlPlaneToolService>();
        AgentToolResult<ActivationRequest>? last = null;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (true)
        {
            last = await service.GetActivationRequestStatusAsync(
                fixture.Context(AgentToolName.GetActivationRequestStatus), requestId);
            if (last.Value?.Status == expected)
                return last.Value;

            if (DateTimeOffset.UtcNow >= deadline)
                break;

            await Task.Delay(20);
        }

        var diagnostics = last is null
            ? "no status result"
            : string.Join(" | ", last.Diagnostics.Select(d => d.Message));
        throw new TimeoutException(
            $"Activation request '{requestId}' did not reach '{expected}'. " +
            $"Last status: {last?.Value?.Status}; diagnostics: {diagnostics}");
    }

    private static async Task<(Draft Draft, ActivationRequest Request, HumanTaskInstance Task)> SubmitCandidateAsync(
        Fixture fixture)
    {
        var draft = ParseAssetCandidate();
        await fixture.DraftStore.SaveAsync(draft);
        var service = fixture.Services.GetRequiredService<IAgentControlPlaneToolService>();

        var review = await service.ReviewDescriptorDraftAsync(
            fixture.Context(AgentToolName.ReviewDescriptorDraft), draft.DraftId);
        review.Status.Should().Be(AgentToolResultStatus.Success,
            string.Join(" | ", review.Diagnostics.Select(d => d.Message)));
        review.Value!.StableHashes.Should().NotBeNull();

        var reviewResultId = fixture.Auditor.GetAllRecords()
            .Last(record => record.TouchedReviewResultIds is { Count: 1 })
            .TouchedReviewResultIds![0];
        var preview = await service.PreviewDescriptorPackageAsync(
            fixture.Context(AgentToolName.PreviewDescriptorPackage), draft.DraftId);
        preview.Status.Should().Be(AgentToolResultStatus.Success,
            string.Join(" | ", preview.Diagnostics.Select(d => d.Message)));
        var packagePreviewId = fixture.Auditor.GetAllRecords()
            .Last(record => record.TouchedPackagePreviewIds is { Count: 1 }
                && record.Context.ToolName == AgentToolName.PreviewDescriptorPackage)
            .TouchedPackagePreviewIds![0];
        var evidence = await service.BuildPackageEvidencePreviewAsync(
            fixture.Context(AgentToolName.BuildPackageEvidencePreview), draft.DraftId);
        evidence.Status.Should().Be(AgentToolResultStatus.Success,
            string.Join(" | ", evidence.Diagnostics.Select(d => d.Message)));
        var evidencePreviewId = fixture.Auditor.GetAllRecords()
            .Last(record => record.TouchedPackagePreviewIds is { Count: 1 }
                && record.Context.ToolName == AgentToolName.BuildPackageEvidencePreview)
            .TouchedPackagePreviewIds![0];

        var resolved = await fixture.ArtifactResolver.ResolveAsync(TenantId, new ActivationBindingSnapshot
        {
            TenantId = TenantId, DraftId = draft.DraftId, DraftVersion = 1,
            ReviewResultId = reviewResultId, PackagePreviewId = packagePreviewId,
            EvidencePreviewId = evidencePreviewId, Hashes = null!, CreatedAt = DateTimeOffset.UtcNow
        });
        var packageHashes = resolved.CurrentPackageHashes ?? throw new InvalidOperationException("Package hashes missing.");
        var sourceReviewHash = resolved.CurrentSourceReviewHash ?? throw new InvalidOperationException("Source review hash missing.");
        var reviewManifestHash = resolved.CurrentReviewManifestHash ?? throw new InvalidOperationException("Review manifest hash missing.");
        var submit = await service.SubmitActivationRequestAsync(
            fixture.Context(AgentToolName.SubmitActivationRequest), new SubmitActivationRequestRequest
            {
                DraftId = draft.DraftId,
                BindingSnapshot = new ActivationBindingSnapshot
                {
                    TenantId = TenantId, DraftId = draft.DraftId, DraftVersion = 1,
                    ReviewResultId = reviewResultId, PackagePreviewId = packagePreviewId,
                    EvidencePreviewId = evidencePreviewId,
                    Hashes = new BindingHashes
                    {
                        SourceReviewHash = sourceReviewHash,
                        ReviewManifestHash = reviewManifestHash,
                        PackageManifestHash = packageHashes.PackageManifestHash,
                        PackageEvidenceHash = packageHashes.PackageEvidenceHash,
                        PackageEvidenceEnvelopeHash = packageHashes.PackageEvidenceEnvelopeHash,
                        ContractHash = review.Value.StableHashes!.ContractHash,
                        DefinitionHash = review.Value.StableHashes.DefinitionHash
                    },
                    CreatedAt = DateTimeOffset.UtcNow
                }
            });
        submit.Status.Should().Be(AgentToolResultStatus.Success,
            string.Join(" | ", submit.Diagnostics.Select(d => d.Message)));
        var request = submit.Value ?? throw new InvalidOperationException("Activation request was not created.");
        request.Status.Should().Be(ActivationRequestStatus.UnderReview);
        var taskId = fixture.ReviewOrchestrator.LastTaskId ?? throw new InvalidOperationException("Review task id missing.");
        var task = await fixture.HumanTasks.GetAsync(new RuntimeInstanceKey(TenantId, taskId))
            ?? throw new InvalidOperationException("Activation review HumanTask was not persisted.");
        return (draft, request, task);
    }

    private static Draft ParseAssetCandidate()
    {
        const string candidateId = AssetContractIds.MaintenanceInitialHumanTask;
        const string interactionId = AssetContractIds.MaintenanceForm;
        var context = new DescriptorAuthoringParseContext
        {
            TenantId = TenantId,
            AuthorId = AuthorId,
            AuthorKind = DescriptorDraftAuthorKind.Agent,
            CreatedAt = DateTimeOffset.UnixEpoch,
            IntentText = "Asset maintenance initial review",
            ExpectedPromptInputHash = "asset-authoring-prompt-hash"
        };
        var json = JsonSerializer.Serialize(new
        {
            contractVersion = "7g.v1",
            promptInputHash = context.ExpectedPromptInputHash,
            plan = new
            {
                planId = "asset-maintenance-initial-review",
                intentText = context.IntentText,
                plannedDescriptorRefs = new[]
                {
                    new { @namespace = "humantask", id = candidateId, version = 1 }
                }
            },
            items = new[]
            {
                new
                {
                    descriptorKind = "HumanTask",
                    descriptorId = candidateId,
                    operation = "Create",
                    rationale = "Require a human reviewer before selecting the candidate Asset contract.",
                    payload = new
                    {
                        id = candidateId,
                        name = "Asset maintenance initial review",
                        state = "Active",
                        version = 1,
                        assigneeStrategy = "CandidateGroup",
                        interaction = new { @namespace = "form", id = interactionId, version = 1 },
                        outcomes = new[]
                        {
                            new { condition = "Approve" },
                            new { condition = "Reject" }
                        }
                    }
                }
            }
        });

        var parsed = new JsonDescriptorAuthoringOutputParser().Parse(json, context);
        parsed.Status.Should().Be(DescriptorAuthoringStatus.Succeeded);
        var draft = parsed.DraftSet.Drafts.Should().ContainSingle().Which;
        draft.Payload.GetDescriptor().Should().BeEquivalentTo(AssetDescriptorCatalog.MaintenanceInitialHumanTask);
        return draft;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required ServiceProvider Services { get; init; }
        public required InMemoryAgentToolInvocationAuditor Auditor { get; init; }
        public required InMemoryActivationBindingArtifactResolver ArtifactResolver { get; init; }
        public required IHumanTaskInstanceStore HumanTasks { get; init; }
        public required CapturingActivationReviewOrchestrator ReviewOrchestrator { get; init; }
        public required CountingRuntimeActivationGate ActivationGate { get; init; }
        public required IReadOnlyList<IHostedService> HostedServices { get; init; }
        public required IRuntimeStateContractRegistry State { get; init; }
        public IDescriptorDraftStore DraftStore => Services.GetRequiredService<IDescriptorDraftStore>();

        public static async Task<Fixture> CreateAsync()
        {
            var form = AssetDescriptorCatalog.MaintenanceForm;
            var schema = AssetDescriptorCatalog.Schemas.Single(item => item.Id == form.Schema.Id);
            var baseline = new IDescriptor[] { schema, form };
            if (form.Id != AssetContractIds.MaintenanceForm
                || form.Schema.Id != schema.Id
                || form.Schema.Version != schema.Version
                || !baseline.Contains(form)
                || !baseline.Contains(schema))
            {
                throw new InvalidOperationException(
                    "The Asset baseline must contain the maintenance Form and its referenced Schema.");
            }
            var globalRegistry = new GlobalDescriptorRegistry();
            foreach (var descriptor in baseline)
                globalRegistry.Register(descriptor);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IAgentToolInvocationAuditor, InMemoryAgentToolInvocationAuditor>();
            services.AddSingleton<IActivationBindingArtifactResolver, InMemoryActivationBindingArtifactResolver>();
            services.AddSingleton<IDescriptorActivationAuditor, InMemoryDescriptorActivationAuditor>();
            services.AddSingleton<InMemoryRuntimeActivationGate>();
            services.AddSingleton<IRuntimeActivationGate>(sp =>
                new CountingRuntimeActivationGate(sp.GetRequiredService<InMemoryRuntimeActivationGate>()));
            services.AddSingleton<IGlobalDescriptorRegistry>(globalRegistry);
            services.AddSingleton<IDescriptorDependencyGraph>(sp =>
                new DescriptorDependencyGraphAdapter(sp.GetRequiredService<IDescriptorTopologyBuilder>(), baseline));
            services.AddSingleton<IDescriptorCatalog, DescriptorCatalog>();
            services.AddSingleton<IDescriptorActivationPolicyProvider, RequireHumanReviewPolicyProvider>();
            services.AddSingleton<IHumanTaskRegistry>(sp =>
                CreateHumanTaskRegistry(sp.GetRequiredService<IRegistryValidationEngine<HumanTaskDescriptor>>()));

            services.AddDescriptorDrafts();
            services.AddDescriptorPackaging();
            services.AddRelationshipKernel();
            services.AddTopologyKernel();
            services.AddDescriptorImpactAnalysis();
            services.AddDescriptorCompatibilityAnalysis();
            services.AddDescriptorLifecycleGovernance();
            services.AddMetadataContextPack();
            services.AddFormKernel();
            services.AddSchemaKernel();
            services.AddRuntimePersistence();
            services.AddCrestCreatesInMemoryRuntimePersistence();
            services.AddSingleton<IEventValidator, PassThroughEventValidator>();
            services.AddSingleton<LocalEventBusOptions>();
            services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
            services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
            services.AddHumanTaskRuntime();
            services.AddAgentControlPlane(AgentToolAuthorizationOptions.DevelopmentDefaults);
            services.AddSingleton<DefaultActivationReviewOrchestrator>();
            services.AddSingleton<IActivationReviewOrchestrator>(sp =>
                new CapturingActivationReviewOrchestrator(
                    sp.GetRequiredService<DefaultActivationReviewOrchestrator>()));
            services.AddRuntimeDelivery(options => options.PollingInterval = TimeSpan.FromMilliseconds(10));

            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var hostedServices = provider.GetServices<IHostedService>().ToArray();
            foreach (var hostedService in hostedServices)
                await hostedService.StartAsync(CancellationToken.None);

            var fixture = new Fixture
            {
                Services = provider,
                Auditor = provider.GetRequiredService<IAgentToolInvocationAuditor>() as InMemoryAgentToolInvocationAuditor
                    ?? throw new InvalidOperationException("In-memory auditor was not registered."),
                ArtifactResolver = provider.GetRequiredService<IActivationBindingArtifactResolver>() as InMemoryActivationBindingArtifactResolver
                    ?? throw new InvalidOperationException("In-memory artifact resolver was not registered."),
                HumanTasks = provider.GetRequiredService<IHumanTaskInstanceStore>(),
                ReviewOrchestrator = provider.GetRequiredService<IActivationReviewOrchestrator>() as CapturingActivationReviewOrchestrator
                    ?? throw new InvalidOperationException("Capturing activation review orchestrator was not registered."),
                ActivationGate = provider.GetRequiredService<IRuntimeActivationGate>() as CountingRuntimeActivationGate
                    ?? throw new InvalidOperationException("Counting activation gate was not registered."),
                State = provider.GetRequiredService<IRuntimeStateContractRegistry>(),
                HostedServices = hostedServices
            };

            _ = await Task.FromResult(fixture);
            return fixture;
        }

        public AgentToolInvocationContext Context(string toolName) => new()
        {
            TenantId = TenantId,
            ActorId = AuthorId,
            ActorKind = AgentToolActorKind.Agent,
            CorrelationId = "asset-approval-correlation",
            ToolName = toolName,
            InvocationSource = AgentToolInvocationSource.Direct
        };

        public async ValueTask DisposeAsync()
        {
            for (var index = HostedServices.Count - 1; index >= 0; index--)
                await HostedServices[index].StopAsync(CancellationToken.None);
            await Services.DisposeAsync();
        }

        private static IHumanTaskRegistry CreateHumanTaskRegistry(
            IRegistryValidationEngine<HumanTaskDescriptor> validationEngine)
        {
            var registry = new HumanTaskRegistry(validationEngine);
            registry.Build(
            [
                new SingleDescriptorProvider<HumanTaskDescriptor>(new HumanTaskDescriptor
                {
                    Id = DescriptorActivationHumanTaskIds.ActivationReview.RequireValue(),
                    Name = "Descriptor activation review",
                    Version = 1,
                    // This Form is supplied by the linked Asset baseline and registered at runtime.
#pragma warning disable CC1001
                    Interaction = new VersionedDescriptorRef<IInteractionDescriptor>(
                        AssetContractIds.MaintenanceForm, 1),
#pragma warning restore CC1001
                    AssigneeStrategy = AssigneeStrategy.CandidateGroup,
                    State = DescriptorState.Active,
                    Outcomes =
                    [
                        new CompletionOutcome { Condition = CompletionCondition.Approve },
                        new CompletionOutcome { Condition = CompletionCondition.Reject }
                    ]
                })
            ]);
            return registry;
        }
    }

    private sealed class SingleDescriptorProvider<T>(T descriptor) : IDescriptorProvider<T>
        where T : IDescriptor
    {
        public IReadOnlyList<T> GetDescriptors() => [descriptor];
    }

    private sealed class RequireHumanReviewPolicyProvider : IDescriptorActivationPolicyProvider
    {
        public Task<DescriptorActivationPolicy> GetPolicyAsync(
            string tenantId, DescriptorKind? descriptorKind = null, CancellationToken ct = default)
            => Task.FromResult(new DescriptorActivationPolicy
            {
                RequireHumanReviewForAll = true,
                ForbidSelfApproval = true,
                AutoActivateAllowedWhenPolicyPermits = true
            });
    }

    private sealed class CapturingActivationReviewOrchestrator(
        DefaultActivationReviewOrchestrator inner) : IActivationReviewOrchestrator
    {
        public string? LastTaskId { get; private set; }

        public async Task<AgentToolResult<string>> CreateActivationReviewTaskAsync(
            AgentToolInvocationContext context, ActivationRequest activationRequest,
            DescriptorActivationPolicy policy, CancellationToken ct = default)
        {
            var result = await inner.CreateActivationReviewTaskAsync(context, activationRequest, policy, ct);
            LastTaskId = result.Value;
            return result;
        }

        public Task<ActivationReviewDispatchOutcome> ProcessReviewDecisionAsync(
            DescriptorActivationReviewDecision reviewDecision, string completionEventId,
            CancellationToken ct = default)
            => inner.ProcessReviewDecisionAsync(reviewDecision, completionEventId, ct);
    }

    private sealed class CountingRuntimeActivationGate(IRuntimeActivationGate inner)
        : IRuntimeActivationGate
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public async Task<AgentToolResult<RuntimeActivationGateResult>> ActivateAsync(
            AgentToolInvocationContext context, ActivationRequest request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _callCount);
            return await inner.ActivateAsync(context, request, ct);
        }
    }

}
