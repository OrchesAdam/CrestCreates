using System.Text.Json;
using CrestCreates.Agent.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Abstractions.Model;
using CrestCreates.Agent.Authoring.Authoring;
using CrestCreates.Agent.ControlPlane;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts;
using CrestCreates.Agent.Memory;
using CrestCreates.Agent.Prompting;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.ContextPack.Abstractions;
using CrestCreates.Sample.AssetManagement.Host.ProductionRelease;
using CrestCreates.Sample.AssetManagement.MicrosoftAgent;
using CrestCreates.Schema.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

public sealed class AssetProductionEvolutionTests
{
    private const string Tenant = "asset-production-evolution";

    [Fact]
    public async Task Compiled_metadata_and_scripted_authoring_enter_the_human_review_chain()
    {
        var provider = new AssetProductionEvolutionScriptedProvider();
        var expert = new AssetTrialEvidenceExpert();
        await using var harness = await AssetControlPlaneApprovalHarness.CreateForProductionEvolutionAsync(Tenant, services =>
        {
            Configure(services, provider);
            services.AddSingleton<IAssetProductionEvolutionExpert>(expert);
        });

        var proposal = await harness.ProposeProductionEvolutionAsync(AssetProductionEvolutionScriptedProvider.Intent);
        provider.CallCount.Should().Be(1);
        expert.CallCount.Should().Be(1);
        var prompt = provider.Request!.Prompt;
        prompt.ContractVersion.Should().Be("8a.v1");
        prompt.UserPrompt.Should().Contain(AssetTrialEvidenceExpert.Advice);
        proposal.Authoring.Status.Should().Be(DescriptorAuthoringStatus.Succeeded);
        proposal.Authoring.PromptInputEvidence.Should().NotBeNull();
        proposal.Authoring.PromptOutputEvidence.Should().NotBeNull();

        var hashes = harness.Services.GetRequiredService<IDescriptorStableHashBuilder>();
        var expected = AssetProductionEvolutionBaseline.GetDescriptors().Cast<IVersionedDescriptor>()
            .ToDictionary(descriptor => new DescriptorRef(descriptor.Namespace, descriptor.Id, descriptor.Version), hashes.Build);
        proposal.Baseline.Request.TenantId.Should().Be(Tenant);
        proposal.Baseline.Descriptors.Select(entry => entry.Ref).Should().BeEquivalentTo(expected.Keys);
        foreach (var entry in proposal.Baseline.Descriptors)
        {
            entry.Hashes.Should().Be(expected[entry.Ref]);
            prompt.UserPrompt.Should().Contain(expected[entry.Ref].ContractHash.Value);
        }
        proposal.Baseline.Relationships.Should().NotBeEmpty();
        prompt.UserPrompt.Should().Contain($"humantask/{AssetProductionDescriptors.TrialTaskId} v1");

        var submission = await harness.GetProductionEvolutionApprovalAsync(proposal);
        submission.Request.Status.Should().Be(ActivationRequestStatus.UnderReview);
        submission.Request.Eligibility.Should().Be(DescriptorActivationEligibility.RequiresHumanReview);
        submission.Draft.DraftId.Should().Be(proposal.SavedDraft!.DraftId);
        submission.Draft.Intent.Should().Be(AssetProductionEvolutionScriptedProvider.Intent);
        submission.Draft.AuthorId.Should().Be(harness.AuthorId);
        var review = await harness.Services.GetRequiredService<IAgentReviewArtifactStore>()
            .GetAsync(Tenant, submission.Request.BindingSnapshot.ReviewResultId);
        review!.ProjectedReview.ValidationResult.IsValid.Should().BeTrue();
        review.ProjectedReview.MaterializationSummary!.IsMaterialized.Should().BeTrue();
        review.ReportInput.GovernanceRequiresReview.Should().BeTrue();
        var packageStore = harness.Services.GetRequiredService<IAgentPackageArtifactStore>();
        var artifact = await packageStore.GetEvidenceAsync(new AgentEvidenceArtifactKey(Tenant, submission.EvidencePreviewId));
        artifact!.PackagePreviewId.Should().Be(submission.PackagePreviewId);
        harness.ReviewerTaskCreationCount.Should().Be(1);
        var gate = harness.Services.GetRequiredService<AssetActivationGateObserver>();
        gate.CallCount.Should().Be(0);

        const string fixtureSigningKey = "asset-production-evolution-fixture";
        var issuer = new AssetProductionReleaseIssuer(
            harness.Services.GetRequiredService<IAgentControlPlaneToolService>(),
            harness.Services.GetRequiredService<IActivationBindingArtifactResolver>(),
            packageStore,
            harness.Services.GetRequiredService<CrestCreates.Metadata.Abstractions.DescriptorPackage.IDescriptorPackageSerializer>(),
            harness.Services.GetRequiredService<IDescriptorDraftStore>(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AssetProductionRelease.SigningKeySetting] = fixtureSigningKey
            }).Build());
        var issueContext = new AgentToolInvocationContext
        {
            TenantId = Tenant,
            ActorId = "asset-release-issuer",
            ActorKind = AgentToolActorKind.Agent,
            CorrelationId = "asset-production-evolution",
            ToolName = AgentToolName.GetActivationRequestStatus,
            InvocationSource = AgentToolInvocationSource.Direct
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => issuer.IssueAsync(issueContext, submission.Request.RequestId));

        var activated = await harness.CompleteHumanAsync(submission, "asset-production-reviewer", "Approve");
        activated.Status.Should().Be(ActivationRequestStatus.Activated);
        gate.SuccessCount.Should().Be(1);
        var receipt = await issuer.IssueAsync(issueContext, activated.RequestId);
        var release = AssetProductionRelease.Load(receipt, fixtureSigningKey, AssetProductionEvolutionBaseline.GetDescriptors());
        release.WorkflowApproval.RequestId.Should().Be(activated.RequestId);
        release.Workflow.Version.Should().Be(AssetProductionDescriptors.WorkflowVersion);
    }

    [Theory]
    [InlineData(null, "1", "MES evidence")]
    [InlineData("all-prior-human-tasks-approved", "2", "compiled maintenance workflow")]
    public async Task Parseable_proposals_with_invalid_business_shape_stop_before_the_first_save(
        string? condition, string baseVersion, string expectedError)
    {
        var provider = new AssetProductionEvolutionScriptedProvider(hash =>
            AssetProductionEvolutionScriptedProvider.WriteProposal(hash, condition, baseVersion));
        await using var harness = await AssetControlPlaneApprovalHarness.CreateForProductionEvolutionAsync(Tenant,
            services => Configure(services, provider));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.ProposeProductionEvolutionAsync(AssetProductionEvolutionScriptedProvider.Intent));

        failure.Message.Should().Contain(expectedError);
        provider.CallCount.Should().Be(1);
        await AssertNoHandoffAsync(harness);
    }

    [Fact]
    public async Task Explicit_unsupported_outcome_is_returned_without_governance_side_effects()
    {
        var provider = new AssetProductionEvolutionScriptedProvider(hash => JsonSerializer.Serialize(new
        {
            contractVersion = "8a.v1",
            outcome = "unsupported",
            promptInputHash = hash,
            unsupported = new
            {
                reasons = new[]
                {
                    new { code = "CAPABILITY_UNSUPPORTED", message = "A runtime action is outside descriptor authoring." }
                }
            }
        }));
        await using var harness = await AssetControlPlaneApprovalHarness.CreateForProductionEvolutionAsync(Tenant,
            services => Configure(services, provider));

        var result = await harness.ProposeProductionEvolutionAsync(AssetProductionEvolutionScriptedProvider.Intent);

        result.Authoring.Status.Should().Be(DescriptorAuthoringStatus.UnsupportedIntent);
        result.Authoring.DraftSet.Drafts.Should().BeEmpty();
        result.SavedDraft.Should().BeNull();
        result.ActivationRequest.Should().BeNull();
        await AssertNoHandoffAsync(harness);
    }

    [Fact]
    public async Task Missing_compiled_baseline_stops_before_the_model_and_governance()
    {
        var provider = new AssetProductionEvolutionScriptedProvider();
        await using var harness = await AssetControlPlaneApprovalHarness.CreateAsync(Tenant, configureServices: services =>
        {
            services.AddAssetProductionEvolution();
            Configure(services, provider);
        });

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.ProposeProductionEvolutionAsync(AssetProductionEvolutionScriptedProvider.Intent));

        failure.Message.Should().Contain(AgentToolName.BuildMetadataContextPack);
        provider.CallCount.Should().Be(0);
        await AssertNoHandoffAsync(harness);
    }

    [Fact]
    public async Task Direct_control_plane_submission_cannot_open_a_reviewer_task_for_an_invalid_production_draft()
    {
        var provider = new AssetProductionEvolutionScriptedProvider();
        await using var harness = await AssetControlPlaneApprovalHarness.CreateForProductionEvolutionAsync(Tenant,
            services => Configure(services, provider));
        const string promptHash = "direct-control-plane-submission";
        var draft = await harness.ParseAndSaveDraftAsync(
            AssetProductionEvolutionScriptedProvider.WriteProposal(promptHash, qualityCondition: null),
            AssetProductionEvolutionScriptedProvider.Intent, promptHash);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.SubmitAsync(draft));

        failure.Message.Should().Contain("SubmitActivationRequest").And.Contain("MES evidence");
        var audit = harness.Services.GetRequiredService<IAgentToolInvocationAuditor>() as InMemoryAgentToolInvocationAuditor;
        var submission = audit!.GetAllRecords().Single(record => record.Context.ToolName == AgentToolName.SubmitActivationRequest);
        submission.Diagnostics.Should().Contain(diagnostic => diagnostic.Code.Value == "ASSET_PRODUCTION_EVOLUTION_INVALID");
        submission.TouchedActivationRequestIds.Should().BeNull();
        harness.ReviewerTaskCreationCount.Should().Be(0);
        harness.Services.GetRequiredService<AssetActivationGateObserver>().CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Additional_catalog_descriptor_blocks_the_review_task_when_the_package_differs_from_the_compiled_baseline()
    {
        var provider = new AssetProductionEvolutionScriptedProvider();
        var productionDescriptors = AssetProductionEvolutionBaseline.GetDescriptors()
            .Except(AssetControlPlaneApprovalHarness.BuildDeployedBaseline());
        var unrelatedSchema = new SchemaDescriptor
        {
            Id = "unrelated-catalog-schema", Name = "Unrelated catalog schema", Version = 1, State = DescriptorState.Active
        };
        await using var harness = await AssetControlPlaneApprovalHarness.CreateAsync(Tenant, configureServices: services =>
        {
            services.AddAssetProductionEvolution();
            Configure(services, provider);
        }, additionalBaseline: [..productionDescriptors, unrelatedSchema]);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.ProposeProductionEvolutionAsync(AssetProductionEvolutionScriptedProvider.Intent));

        failure.Message.Should().Contain("ASSET_PRODUCTION_EVOLUTION_INVALID");
        provider.CallCount.Should().Be(1);
        harness.ReviewerTaskCreationCount.Should().Be(0);
        harness.Services.GetRequiredService<AssetActivationGateObserver>().CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Draft_creation_permission_is_required_before_persisting_the_typed_proposal()
    {
        var provider = new AssetProductionEvolutionScriptedProvider();
        await using var harness = await AssetControlPlaneApprovalHarness.CreateForProductionEvolutionAsync(Tenant, services =>
        {
            Configure(services, provider);
            services.Replace(ServiceDescriptor.Singleton<IAgentToolAuthorizationService>(
                new DefaultAgentToolAuthorizationService(AgentToolAuthorizationOptions.DevelopmentDefaults with
                {
                    DeniedPermissions = [AgentToolPermissionNames.DraftCreate]
                })));
        });

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.ProposeProductionEvolutionAsync(AssetProductionEvolutionScriptedProvider.Intent));

        failure.Message.Should().Contain("draft creation was denied");
        provider.CallCount.Should().Be(1);
        await AssertNoHandoffAsync(harness);
    }

    private static void Configure(IServiceCollection services, AssetProductionEvolutionScriptedProvider model)
    {
        model.Register(services);
        services.AddSingleton<AssetActivationGateObserver>();
        services.Replace(ServiceDescriptor.Singleton<IRuntimeActivationGate>(sp =>
            sp.GetRequiredService<AssetActivationGateObserver>()));
    }

    [Fact]
    public async Task MicrosoftAgentModelClientAuthorsTheProductionProposalThroughTheSameGovernedChain()
    {
        using var chat = new MafAuthoringChatClient();
        await using var harness = await AssetControlPlaneApprovalHarness.CreateForProductionEvolutionAsync(Tenant, services =>
        {
            services.AddAgentPrompting();
            services.AddAgentMemoryReadRuntime();
            services.AddDescriptorAuthoring();
            services.AddSingleton<IDescriptorAuthoringModelClient>(new MicrosoftAgentAuthoringModelClient(chat));
            services.Configure<LlmDescriptorAuthoringAgentOptions>(options =>
            {
                options.AuthorId = "asset-author-agent";
                options.ProviderProfileRef = new AgentPromptProviderProfileRef("maf-authoring");
                options.ModelProfile = new DescriptorAuthoringModelProfile
                {
                    ProfileName = "maf-authoring",
                    ProviderName = "maf-authoring",
                    ModelName = "maf-asset-evolution",
                    SupportsJsonMode = true
                };
            });
            services.AddSingleton<IDescriptorAuthoringAgent, LlmDescriptorAuthoringAgent>();
            services.AddSingleton<AssetActivationGateObserver>();
            services.Replace(ServiceDescriptor.Singleton<IRuntimeActivationGate>(sp =>
                sp.GetRequiredService<AssetActivationGateObserver>()));
        });

        var proposal = await harness.ProposeProductionEvolutionAsync(AssetProductionEvolutionScriptedProvider.Intent);

        chat.Calls.Should().Be(1);
        proposal.Authoring.Status.Should().Be(DescriptorAuthoringStatus.Succeeded);
        proposal.ActivationRequest!.Status.Should().Be(ActivationRequestStatus.UnderReview);
        var submission = await harness.GetProductionEvolutionApprovalAsync(proposal);
        submission.Draft.DraftId.Should().Be(proposal.SavedDraft!.DraftId);
        harness.ReviewerTaskCreationCount.Should().Be(1);
        harness.Services.GetRequiredService<AssetActivationGateObserver>().CallCount.Should().Be(0);

        var activated = await harness.CompleteHumanAsync(submission, "asset-production-reviewer", "Approve");
        activated.Status.Should().Be(ActivationRequestStatus.Activated);
        harness.Services.GetRequiredService<AssetActivationGateObserver>().SuccessCount.Should().Be(1);
    }

    private sealed class MafAuthoringChatClient : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            var userPrompt = messages.Single(message => message.Role == ChatRole.User).Text;
            var lines = userPrompt.Split('\n');
            var marker = Array.FindIndex(lines, line => line.Trim() == "PromptInputHash:");
            if (marker < 0 || marker + 1 >= lines.Length)
            {
                throw new InvalidOperationException("The authoring prompt did not disclose its prompt input hash.");
            }

            var responseText = AssetProductionEvolutionScriptedProvider.WriteProposal(lines[marker + 1].Trim());
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static async Task AssertNoHandoffAsync(AssetControlPlaneApprovalHarness harness)
    {
        (await harness.Services.GetRequiredService<IDescriptorDraftStore>().ListAsync(Tenant)).Should().BeEmpty();
        (await harness.Services.GetRequiredService<IAgentReviewArtifactStore>().ListAsync(Tenant)).Should().BeEmpty();
        harness.ReviewerTaskCreationCount.Should().Be(0);
        harness.Services.GetRequiredService<AssetActivationGateObserver>().CallCount.Should().Be(0);
    }

    private sealed class AssetTrialEvidenceExpert : IAssetProductionEvolutionExpert
    {
        public const string Advice = "Treat the MES trial result as evidence for the quality reviewer.";
        public int CallCount { get; private set; }

        public Task<string?> AdviseAsync(string intent, MetadataContextPack baseline, CancellationToken ct = default)
        {
            intent.Should().Be(AssetProductionEvolutionScriptedProvider.Intent);
            baseline.Descriptors.Should().Contain(entry => entry.Ref.Id == AssetProductionDescriptors.TrialTaskId);
            CallCount++;
            return Task.FromResult<string?>(Advice);
        }
    }
}
