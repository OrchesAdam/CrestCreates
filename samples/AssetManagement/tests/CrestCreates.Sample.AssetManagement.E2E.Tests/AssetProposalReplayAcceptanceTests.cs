using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Agent.Authoring.Parsing;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.DescriptorDraft;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Runtime.Persistence.PostgreSql;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using DescriptorDraftModel = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

public sealed class AssetProposalReplayAcceptanceTests : IAsyncLifetime
{
    private readonly AssetProposalReplayPostgreSqlFixture _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task AgentOwnedHumanTaskDraft_Should_SurvivePostgreSqlProviderRestart_AndReplayReview()
    {
        const string tenantId = "asset-proposal-replay-tenant";
        const string authorId = "asset-proposal-replay-agent";
        const string promptHash = "asset-proposal-replay-prompt-hash";
        const string intent = "Add the initial Asset maintenance human review.";

        var parsed = new JsonDescriptorAuthoringOutputParser().Parse(
            AssetApprovedInventoryHandoffAcceptanceTests.CreateAssetCandidateJson(promptHash, intent),
            new DescriptorAuthoringParseContext
            {
                TenantId = tenantId,
                AuthorId = authorId,
                AuthorKind = DescriptorDraftAuthorKind.Agent,
                CreatedAt = DateTimeOffset.UnixEpoch,
                IntentText = intent,
                ExpectedPromptInputHash = promptHash
            });

        parsed.Status.Should().Be(DescriptorAuthoringStatus.Succeeded);
        var draft = parsed.DraftSet.Drafts.Should().ContainSingle().Which;
        draft.AuthorKind.Should().Be(DescriptorDraftAuthorKind.Agent);
        draft.DescriptorKind.Should().Be(DescriptorKind.HumanTask);
        draft.Payload.Should().BeOfType<HumanTaskDescriptorDraftPayload>();

        DescriptorDraftModel reviewedDraft;
        DescriptorStableHashes expectedHashes;
        await using (var initialHarness = await AssetControlPlaneApprovalHarness.CreateAsync(tenantId, authorId))
        {
            var harnessStore = initialHarness.Services.GetRequiredService<IDescriptorDraftStore>();
            await harnessStore.SaveAsync(draft);
            var initialReview = await ReviewAsync(initialHarness.Services, tenantId, authorId, draft.DraftId);
            initialReview.Status.Should().Be(AgentToolResultStatus.Success);
            var initialReviewValue = initialReview.Value!;
            initialReviewValue.ValidationResult.IsValid.Should().BeTrue();
            initialReviewValue.MaterializationSummary.Should().NotBeNull();
            initialReviewValue.MaterializationSummary!.IsMaterialized.Should().BeTrue();
            initialReviewValue.GovernanceSummary!.Decision.Should().NotBe("Blocked");

            reviewedDraft = (await harnessStore.GetAsync(tenantId, draft.DraftId))!;
            reviewedDraft.Status.Should().Be(DescriptorDraftStatus.Reviewed);
            reviewedDraft.Payload.Should().BeOfType<HumanTaskDescriptorDraftPayload>();

            var hashBuilder = initialHarness.Services.GetRequiredService<IDescriptorStableHashBuilder>();
            expectedHashes = hashBuilder.Build(reviewedDraft.Payload.GetDescriptor());
            initialReviewValue.StableHashes.Should().BeEquivalentTo(expectedHashes);
        }

        await using (var persistenceProvider = BuildPersistenceProvider(_database.Options))
        {
            var persistenceStore = persistenceProvider.GetRequiredService<IDescriptorDraftStore>();
            await persistenceStore.SaveAsync(reviewedDraft);
        }

        DescriptorDraftModel reloadedDraft;
        await using (var restartedProvider = BuildPersistenceProvider(_database.Options))
        {
            var restartedStore = restartedProvider.GetRequiredService<IDescriptorDraftStore>();
            reloadedDraft = (await restartedStore.GetAsync(tenantId, draft.DraftId))!;

            reloadedDraft.Should().BeEquivalentTo(reviewedDraft);
            reloadedDraft.TenantId.Should().Be(tenantId);
            reloadedDraft.DraftId.Should().Be(draft.DraftId);
            reloadedDraft.DescriptorKind.Should().Be(DescriptorKind.HumanTask);
            reloadedDraft.DescriptorId.Should().Be(draft.DescriptorId);
            reloadedDraft.Operation.Should().Be(draft.Operation);
            reloadedDraft.AuthorKind.Should().Be(DescriptorDraftAuthorKind.Agent);
            reloadedDraft.AuthorId.Should().Be(authorId);
            reloadedDraft.ProposedVersion.Should().Be(draft.ProposedVersion);
            reloadedDraft.BaseVersion.Should().Be(draft.BaseVersion);
            reloadedDraft.Status.Should().Be(DescriptorDraftStatus.Reviewed);
            reloadedDraft.Payload.Should().BeOfType<HumanTaskDescriptorDraftPayload>()
                .Which.Descriptor.Should().BeEquivalentTo(
                    ((HumanTaskDescriptorDraftPayload)reviewedDraft.Payload).Descriptor);

            var hashBuilder = new DescriptorStableHashBuilder(new DefaultCanonicalHashComputer());
            var reloadedHashes = hashBuilder.Build(reloadedDraft.Payload.GetDescriptor());
            reloadedHashes.ContractHash.Should().BeEquivalentTo(expectedHashes.ContractHash);
            reloadedHashes.DefinitionHash.Should().BeEquivalentTo(expectedHashes.DefinitionHash);

            (await restartedStore.GetAsync("asset-proposal-replay-other-tenant", draft.DraftId))
                .Should().BeNull();
        }

        await using var replayHarness = await AssetControlPlaneApprovalHarness.CreateAsync(tenantId, authorId);
        var replayStore = replayHarness.Services.GetRequiredService<IDescriptorDraftStore>();
        await replayStore.SaveAsync(reloadedDraft);
        var replayedReview = await ReviewAsync(replayHarness.Services, tenantId, authorId, reloadedDraft.DraftId);
        replayedReview.Status.Should().Be(AgentToolResultStatus.Success);
        var replayedReviewValue = replayedReview.Value!;
        replayedReviewValue.ValidationResult.IsValid.Should().BeTrue();
        replayedReviewValue.MaterializationSummary!.IsMaterialized.Should().BeTrue();
        replayedReviewValue.GovernanceSummary!.Decision.Should().NotBe("Blocked");
        replayedReviewValue.StableHashes.Should().BeEquivalentTo(expectedHashes);
    }

    private static ServiceProvider BuildPersistenceProvider(PostgreSqlRuntimePersistenceOptions options)
        => new ServiceCollection()
            .AddCrestCreatesPostgreSqlRuntimePersistence(options)
            .AddCrestCreatesPostgreSqlControlPlaneAndReferenceDataPersistence()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    private static Task<AgentToolResult<AgentReviewResultDto>> ReviewAsync(
        IServiceProvider services,
        string tenantId,
        string authorId,
        string draftId)
        => services.GetRequiredService<IAgentControlPlaneToolService>().ReviewDescriptorDraftAsync(
            new AgentToolInvocationContext
            {
                TenantId = tenantId,
                ActorId = authorId,
                ActorKind = AgentToolActorKind.Agent,
                CorrelationId = "asset-proposal-replay-review",
                ToolName = AgentToolName.ReviewDescriptorDraft,
                InvocationSource = AgentToolInvocationSource.Direct
            },
            draftId);
}
