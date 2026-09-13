using System.Collections.Concurrent;
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
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Sample.AssetManagement.Host;
using CrestCreates.Schema;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Bounded proof that the authoring parser's complete Asset candidate can enter the
/// real control-plane review, package, evidence, and activation-handoff pipeline.
/// Approval, completion, outbox delivery, and host loading are intentionally outside
/// this acceptance boundary.
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

        fixture.HumanTasks.Items.Should().ContainSingle();
        var reviewTask = fixture.HumanTasks.Items.Single().Value;
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
            IntentText = "Asset management initial human review",
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
                        name = "Asset management initial human review",
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
        return parsed.DraftSet.Drafts.Should().ContainSingle().Which;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required ServiceProvider Services { get; init; }
        public required InMemoryAgentToolInvocationAuditor Auditor { get; init; }
        public required InMemoryActivationBindingArtifactResolver ArtifactResolver { get; init; }
        public required InMemoryHumanTaskStore HumanTasks { get; init; }
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
            services.AddSingleton<IRuntimeActivationGate, InMemoryRuntimeActivationGate>();
            services.AddSingleton<IGlobalDescriptorRegistry>(globalRegistry);
            services.AddSingleton<IDescriptorDependencyGraph>(sp =>
                new DescriptorDependencyGraphAdapter(sp.GetRequiredService<IDescriptorTopologyBuilder>(), baseline));
            services.AddSingleton<IDescriptorCatalog, DescriptorCatalog>();
            services.AddSingleton<IHumanTaskInstanceStore, InMemoryHumanTaskStore>();
            services.AddSingleton<ILocalEventBus, NoopLocalEventBus>();
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
            services.AddHumanTaskRuntime();
            services.AddAgentControlPlane(AgentToolAuthorizationOptions.DevelopmentDefaults);

            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var fixture = new Fixture
            {
                Services = provider,
                Auditor = provider.GetRequiredService<IAgentToolInvocationAuditor>() as InMemoryAgentToolInvocationAuditor
                    ?? throw new InvalidOperationException("In-memory auditor was not registered."),
                ArtifactResolver = provider.GetRequiredService<IActivationBindingArtifactResolver>() as InMemoryActivationBindingArtifactResolver
                    ?? throw new InvalidOperationException("In-memory artifact resolver was not registered."),
                HumanTasks = provider.GetRequiredService<IHumanTaskInstanceStore>() as InMemoryHumanTaskStore
                    ?? throw new InvalidOperationException("In-memory HumanTask store was not registered."),
                State = provider.GetRequiredService<IRuntimeStateContractRegistry>()
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

        public ValueTask DisposeAsync() => Services.DisposeAsync();

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

    private sealed class InMemoryHumanTaskStore : IHumanTaskInstanceStore
    {
        public ConcurrentDictionary<RuntimeInstanceKey, HumanTaskInstance> Items { get; } = new();

        public Task AddAsync(HumanTaskInstance instance, CancellationToken cancellationToken = default)
        {
            Items[instance.Key] = instance;
            return Task.CompletedTask;
        }

        public Task<HumanTaskInstance?> GetAsync(RuntimeInstanceKey key, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.TryGetValue(key, out var item) ? item : null);

        public Task UpdateAsync(HumanTaskInstance instance, long expectedRevision, CancellationToken cancellationToken = default)
        {
            Items[instance.Key] = instance;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByAssigneeAsync(RuntimeTenantScope scope, string assigneeUserId, CancellationToken cancellationToken = default) => Empty();
        public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByWorkflowAsync(RuntimeInstanceKey workflowKey, CancellationToken cancellationToken = default) => Empty();
        public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateUserAsync(RuntimeTenantScope scope, string userId, CancellationToken cancellationToken = default) => Empty();
        public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateRoleAsync(RuntimeTenantScope scope, string roleId, CancellationToken cancellationToken = default) => Empty();
        public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByOrganizationAsync(RuntimeTenantScope scope, string organizationUnitId, CancellationToken cancellationToken = default) => Empty();
        public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByPositionAsync(RuntimeTenantScope scope, string positionId, CancellationToken cancellationToken = default) => Empty();

        private static Task<IReadOnlyList<HumanTaskInstance>> Empty()
            => Task.FromResult<IReadOnlyList<HumanTaskInstance>>([]);
    }

    private sealed class NoopLocalEventBus : ILocalEventBus
    {
        public Task PublishAsync(ILocalEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : ILocalEvent => Task.CompletedTask;
    }
}
