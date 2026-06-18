using Xunit;
using Moq;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;

using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Tests;

public class Wave4FixProposalTests : AgentControlPlaneTestBase
{
    [Fact]
    public async Task SuggestDescriptorDraftFixes_Generates_Proposals_From_Diagnostics()
    {
        var service = CreateService();
        var context = CreateContext("SuggestDescriptorDraftFixes");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var validationResult = DraftAbstractions.DescriptorDraftValidationResult.Failure(
            new DraftAbstractions.DescriptorDraftDiagnostic
            {
                Code = "DRAFT_ID_EMPTY",
                Severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Error,
                Message = "Draft ID must not be empty"
            });
        DraftValidatorMock.Setup(v => v.Validate(draft)).Returns(validationResult);

        var result = await service.SuggestDescriptorDraftFixesAsync(context, "draft-001");

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Proposals.Should().NotBeEmpty();
        result.Value.Proposals[0].DraftId.Should().Be("draft-001");
        result.Value.Proposals[0].RiskLevel.Should().Be(FixProposalRiskLevel.High);
        result.Value.Proposals[0].RequiresHumanApproval.Should().BeTrue();
    }

    [Fact]
    public async Task SuggestDescriptorDraftFixes_Returns_Empty_When_No_Diagnostics()
    {
        var service = CreateService();
        var context = CreateContext("SuggestDescriptorDraftFixes");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        DraftValidatorMock.Setup(v => v.Validate(draft))
            .Returns(DraftAbstractions.DescriptorDraftValidationResult.Success());

        var result = await service.SuggestDescriptorDraftFixesAsync(context, "draft-001");

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Proposals.Should().BeEmpty();
    }

    [Fact]
    public async Task SuggestDescriptorDraftFixes_Returns_NotFound_When_Draft_Missing()
    {
        var service = CreateService();
        var context = CreateContext("SuggestDescriptorDraftFixes");

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "nonexistent", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(null));

        var result = await service.SuggestDescriptorDraftFixesAsync(context, "nonexistent");

        result.Status.Should().Be(AgentToolResultStatus.NotFound);
    }

    [Fact]
    public async Task GetFixProposal_Returns_Stored_Proposal()
    {
        var service = CreateService();
        var context = CreateContext("SuggestDescriptorDraftFixes");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var validationResult = DraftAbstractions.DescriptorDraftValidationResult.Failure(
            new DraftAbstractions.DescriptorDraftDiagnostic { Code = "DRAFT_ID_EMPTY", Severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Error, Message = "Empty" });
        DraftValidatorMock.Setup(v => v.Validate(draft)).Returns(validationResult);

        var suggestResult = await service.SuggestDescriptorDraftFixesAsync(context, "draft-001");
        var proposalId = suggestResult.Value!.Proposals[0].ProposalId;

        var getResult = await service.GetFixProposalAsync(context, proposalId);

        getResult.Status.Should().Be(AgentToolResultStatus.Success);
        getResult.Value!.ProposalId.Should().Be(proposalId);
    }

    [Fact]
    public async Task GetFixProposal_Returns_NotFound_When_Missing()
    {
        var service = CreateService();
        var context = CreateContext("GetFixProposal");

        var result = await service.GetFixProposalAsync(context, "nonexistent");

        result.Status.Should().Be(AgentToolResultStatus.NotFound);
    }

    [Fact]
    public async Task ListFixProposals_Returns_Proposals_For_Draft()
    {
        var service = CreateService();
        var context = CreateContext("SuggestDescriptorDraftFixes");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var validationResult = DraftAbstractions.DescriptorDraftValidationResult.Failure(
            new DraftAbstractions.DescriptorDraftDiagnostic { Code = "DRAFT_ID_EMPTY", Severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Error, Message = "Empty" });
        DraftValidatorMock.Setup(v => v.Validate(draft)).Returns(validationResult);

        await service.SuggestDescriptorDraftFixesAsync(context, "draft-001");

        var listResult = await service.ListFixProposalsAsync(context, "draft-001");

        listResult.Status.Should().Be(AgentToolResultStatus.Success);
        listResult.Value!.Proposals.Should().NotBeEmpty();
        listResult.Value.Proposals.All(p => p.DraftId == "draft-001").Should().BeTrue();
    }

    [Fact]
    public async Task ApplyFixProposalToDraft_Updates_Draft_Only()
    {
        var service = CreateService();
        var context = CreateContext("SuggestDescriptorDraftFixes");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DraftStoreMock.Setup(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var validationResult = DraftAbstractions.DescriptorDraftValidationResult.Failure(
            new DraftAbstractions.DescriptorDraftDiagnostic { Code = "DRAFT_ID_EMPTY", Severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Error, Message = "Empty" });
        DraftValidatorMock.Setup(v => v.Validate(draft)).Returns(validationResult);

        var suggestResult = await service.SuggestDescriptorDraftFixesAsync(context, "draft-001");
        var proposalId = suggestResult.Value!.Proposals[0].ProposalId;

        var applyRequest = new ApplyFixProposalRequest { ProposalId = proposalId, DraftId = "draft-001" };
        var applyResult = await service.ApplyFixProposalToDraftAsync(context, applyRequest);

        applyResult.Status.Should().Be(AgentToolResultStatus.Success);
        applyResult.Value!.DraftId.Should().Be("draft-001");
    }

    [Fact]
    public async Task ApplyFixProposalToDraft_Applies_Scalar_Field_Actions()
    {
        var service = CreateService();
        var context = CreateContext("SuggestDescriptorDraftFixes");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DraftStoreMock.Setup(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Use RATIONALE_EMPTY — generates a fix for Rationale (a mutable scalar field)
        var validationResult = DraftAbstractions.DescriptorDraftValidationResult.Failure(
            new DraftAbstractions.DescriptorDraftDiagnostic { Code = "RATIONALE_EMPTY", Severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Warning, Message = "Rationale is empty" });
        DraftValidatorMock.Setup(v => v.Validate(draft)).Returns(validationResult);

        var suggestResult = await service.SuggestDescriptorDraftFixesAsync(context, "draft-001");
        var proposalId = suggestResult.Value!.Proposals[0].ProposalId;

        var applyRequest = new ApplyFixProposalRequest { ProposalId = proposalId, DraftId = "draft-001" };
        var applyResult = await service.ApplyFixProposalToDraftAsync(context, applyRequest);

        applyResult.Status.Should().Be(AgentToolResultStatus.Success);
        applyResult.Value!.DraftId.Should().Be("draft-001");
        // Rationale should have been updated by the fix action
        applyResult.Value.Rationale.Should().Be("(provide rationale)");
        // FIX_ACTIONS_APPLIED diagnostic should be present
        applyResult.Diagnostics.Should().Contain(d => d.Code == "FIX_ACTIONS_APPLIED");
    }

    [Fact]
    public async Task ApplyFixProposalToDraft_Records_Applied_And_Skipped_Diagnostics()
    {
        var service = CreateService();
        var context = CreateContext("SuggestDescriptorDraftFixes");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DraftStoreMock.Setup(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // DRAFT_ID_EMPTY generates a fix for DraftId (identity field — will be skipped)
        var validationResult = DraftAbstractions.DescriptorDraftValidationResult.Failure(
            new DraftAbstractions.DescriptorDraftDiagnostic { Code = "DRAFT_ID_EMPTY", Severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Error, Message = "Empty" });
        DraftValidatorMock.Setup(v => v.Validate(draft)).Returns(validationResult);

        var suggestResult = await service.SuggestDescriptorDraftFixesAsync(context, "draft-001");
        var proposalId = suggestResult.Value!.Proposals[0].ProposalId;

        var applyRequest = new ApplyFixProposalRequest { ProposalId = proposalId, DraftId = "draft-001" };
        var applyResult = await service.ApplyFixProposalToDraftAsync(context, applyRequest);

        // DraftId is an identity field, so the action should be skipped
        applyResult.Diagnostics.Should().Contain(d => d.Code == "FIX_ACTIONS_SKIPPED");
    }

    [Fact]
    public async Task ApplyFixProposalToDraft_Returns_NotFound_When_Proposal_Missing()
    {
        var service = CreateService();
        var context = CreateContext("ApplyFixProposalToDraft");
        var request = new ApplyFixProposalRequest { ProposalId = "nonexistent", DraftId = "draft-001" };

        var result = await service.ApplyFixProposalToDraftAsync(context, request);
        result.Status.Should().Be(AgentToolResultStatus.NotFound);
    }

    [Fact]
    public async Task ApplyFixProposalToDraft_Rejects_Proposal_DraftId_Mismatch()
    {
        var service = CreateService();
        var context = CreateContext("SuggestDescriptorDraftFixes");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var validationResult = DraftAbstractions.DescriptorDraftValidationResult.Failure(
            new DraftAbstractions.DescriptorDraftDiagnostic { Code = "DRAFT_ID_EMPTY", Severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Error, Message = "Empty" });
        DraftValidatorMock.Setup(v => v.Validate(draft)).Returns(validationResult);

        var suggestResult = await service.SuggestDescriptorDraftFixesAsync(context, "draft-001");
        var proposalId = suggestResult.Value!.Proposals[0].ProposalId;

        // Make draft disappear for apply
        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-002", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(CreateTestDraft(draftId: "draft-002")));

        var request = new ApplyFixProposalRequest { ProposalId = proposalId, DraftId = "draft-002" };
        var result = await service.ApplyFixProposalToDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "PROPOSAL_DRAFT_MISMATCH");
    }

    [Fact]
    public async Task ApplyFixProposal_Never_Patches_Active_Descriptors()
    {
        var service = CreateService();
        var context = CreateContext("SuggestDescriptorDraftFixes");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DraftStoreMock.Setup(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var validationResult = DraftAbstractions.DescriptorDraftValidationResult.Failure(
            new DraftAbstractions.DescriptorDraftDiagnostic { Code = "DRAFT_ID_EMPTY", Severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Error, Message = "Empty" });
        DraftValidatorMock.Setup(v => v.Validate(draft)).Returns(validationResult);

        var suggestResult = await service.SuggestDescriptorDraftFixesAsync(context, "draft-001");
        var proposalId = suggestResult.Value!.Proposals[0].ProposalId;

        var request = new ApplyFixProposalRequest { ProposalId = proposalId, DraftId = "draft-001" };
        await service.ApplyFixProposalToDraftAsync(context, request);

        DraftStoreMock.Verify(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        // DescriptorCatalog has no mutation methods — structural invariant
    }
}
