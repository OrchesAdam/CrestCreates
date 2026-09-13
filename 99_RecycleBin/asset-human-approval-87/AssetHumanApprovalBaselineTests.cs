using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.DraftContracts.Dto;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Agent.ControlPlane.Tests;

/// <summary>
/// Baseline for the real control-plane create path used by the Asset candidate.
/// The generated draft contract intentionally defaults preserved Outcomes to an
/// empty collection; the final assertion records the required Asset contract and
/// remains failing until that path can carry Approve/Reject outcomes.
/// </summary>
public sealed class AssetHumanApprovalBaselineTests : AgentControlPlaneTestBase
{
    private const string CandidateId = "ht_asset_maintenance_initial_review";
    private const string InteractionId = "form_asset_maintenance_review";

    [Fact]
    public async Task CreateDescriptorDraft_AssetHumanTask_PreservesApprovalContractBaseline()
    {
        var service = CreateService();
        Draft? savedDraft = null;

        DraftStoreMock
            .Setup(store => store.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()))
            .Callback<Draft, CancellationToken>((draft, _) => savedDraft = draft)
            .Returns(Task.CompletedTask);

        var result = await service.CreateDescriptorDraftAsync(
            CreateContext(AgentToolName.CreateDescriptorDraft),
            new CreateDescriptorDraftRequest
            {
                DescriptorKind = DescriptorKind.HumanTask,
                DescriptorId = CandidateId,
                Operation = DescriptorDraftOperation.Create,
                Payload = new AgentDraftPayloadDto
                {
                    Discriminator = DescriptorKind.HumanTask,
                    HumanTask = new AgentHumanTaskDraftPayloadDto
                    {
                        Name = "Asset management initial human review",
                        Version = 1,
                        State = DescriptorState.Active,
                        AssigneeStrategy = AssigneeStrategy.CandidateGroup,
                        Interaction = new DescriptorRef("form", InteractionId, 1)
                    }
                },
                ProposedVersion = "1",
                Intent = "Asset management initial human review",
                Rationale = "Require a human reviewer before selecting the candidate Asset contract.",
                Source = "asset-human-approval-baseline"
            });

        result.Status.Should().Be(AgentToolResultStatus.Success);
        savedDraft.Should().NotBeNull();

        var candidate = savedDraft!.Payload.GetDescriptor()
            .Should().BeOfType<HumanTaskDescriptor>().Subject;
        candidate.Id.Should().Be(CandidateId);
        candidate.Interaction.Id.Should().Be(InteractionId);
        candidate.Interaction.Version.Should().Be(1);
        candidate.Version.Should().Be(1);
        candidate.AssigneeStrategy.Should().Be(AssigneeStrategy.CandidateGroup);

        candidate.Outcomes.Should().BeEquivalentTo(
            new[]
            {
                new CompletionOutcome { Condition = CompletionCondition.Approve },
                new CompletionOutcome { Condition = CompletionCondition.Reject }
            },
            "the Asset human-task contract requires Approve and Reject outcomes");
    }
}
