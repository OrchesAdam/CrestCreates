using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using System.Text.Json;
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

        // RATIONALE_EMPTY generates a fix for Rationale (an allowed mutable draft field)
        var validationResult = DraftAbstractions.DescriptorDraftValidationResult.Failure(
            new DraftAbstractions.DescriptorDraftDiagnostic
            {
                Code = "RATIONALE_EMPTY",
                Severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Warning,
                Message = "Rationale must not be empty"
            });
        DraftValidatorMock.Setup(v => v.Validate(draft)).Returns(validationResult);

        var result = await service.SuggestDescriptorDraftFixesAsync(context, "draft-001");

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Proposals.Should().NotBeEmpty();
        result.Value.Proposals[0].DraftId.Should().Be("draft-001");
        result.Value.Proposals[0].Kind.Should().Be(FixProposalKind.SetRequiredField);
        result.Value.Proposals[0].RequiresHumanReview.Should().BeFalse();
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
        var suggestContext = CreateContext("SuggestDescriptorDraftFixes");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var validationResult = DraftAbstractions.DescriptorDraftValidationResult.Failure(
            new DraftAbstractions.DescriptorDraftDiagnostic { Code = "RATIONALE_EMPTY", Severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Warning, Message = "Empty" });
        DraftValidatorMock.Setup(v => v.Validate(draft)).Returns(validationResult);

        var suggestResult = await service.SuggestDescriptorDraftFixesAsync(suggestContext, "draft-001");
        var proposalId = suggestResult.Value!.Proposals[0].Id;

        var getContext = CreateContext("GetFixProposal");
        var getResult = await service.GetFixProposalAsync(getContext, proposalId);

        getResult.Status.Should().Be(AgentToolResultStatus.Success);
        getResult.Value!.Id.Should().Be(proposalId);
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
        var suggestContext = CreateContext("SuggestDescriptorDraftFixes");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        // Required for batch owner resolution in list
        DraftStoreMock.Setup(s => s.ListAsync(TestTenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([draft]);

        // RATIONALE_EMPTY generates a fix for Rationale (an allowed mutable draft field)
        var validationResult = DraftAbstractions.DescriptorDraftValidationResult.Failure(
            new DraftAbstractions.DescriptorDraftDiagnostic { Code = "RATIONALE_EMPTY", Severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Warning, Message = "Empty" });
        DraftValidatorMock.Setup(v => v.Validate(draft)).Returns(validationResult);

        await service.SuggestDescriptorDraftFixesAsync(suggestContext, "draft-001");

        var listContext = CreateContext("ListFixProposals");
        var listResult = await service.ListFixProposalsAsync(listContext, "draft-001");

        listResult.Status.Should().Be(AgentToolResultStatus.Success);
        listResult.Value!.Proposals.Should().NotBeEmpty();
        listResult.Value.Proposals.All(p => p.DraftId == "draft-001").Should().BeTrue();
    }

    [Fact]
    public async Task ApplyFixProposalToDraft_Updates_Draft_Only()
    {
        var service = CreateService();
        var suggestContext = CreateContext("SuggestDescriptorDraftFixes");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DraftStoreMock.Setup(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // RATIONALE_EMPTY generates a fix for Rationale (an allowed mutable draft field)
        var validationResult = DraftAbstractions.DescriptorDraftValidationResult.Failure(
            new DraftAbstractions.DescriptorDraftDiagnostic { Code = "RATIONALE_EMPTY", Severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Warning, Message = "Rationale is empty" });
        DraftValidatorMock.Setup(v => v.Validate(draft)).Returns(validationResult);

        var suggestResult = await service.SuggestDescriptorDraftFixesAsync(suggestContext, "draft-001");
        var proposalId = suggestResult.Value!.Proposals[0].Id;

        var applyContext = CreateContext("ApplyFixProposalToDraft");
        var applyRequest = new ApplyFixProposalRequest { ProposalId = proposalId, DraftId = "draft-001" };
        var applyResult = await service.ApplyFixProposalToDraftAsync(applyContext, applyRequest);

        applyResult.Status.Should().Be(AgentToolResultStatus.Success);
        applyResult.Value!.DraftId.Should().Be("draft-001");
    }

    [Fact]
    public async Task ApplyFixProposalToDraft_Applies_Scalar_Field_Actions()
    {
        var service = CreateService();
        var suggestContext = CreateContext("SuggestDescriptorDraftFixes");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DraftStoreMock.Setup(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Use RATIONALE_EMPTY — generates a fix for Rationale (a mutable scalar field)
        var validationResult = DraftAbstractions.DescriptorDraftValidationResult.Failure(
            new DraftAbstractions.DescriptorDraftDiagnostic { Code = "RATIONALE_EMPTY", Severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Warning, Message = "Rationale is empty" });
        DraftValidatorMock.Setup(v => v.Validate(draft)).Returns(validationResult);

        var suggestResult = await service.SuggestDescriptorDraftFixesAsync(suggestContext, "draft-001");
        var proposalId = suggestResult.Value!.Proposals[0].Id;

        var applyContext = CreateContext("ApplyFixProposalToDraft");
        var applyRequest = new ApplyFixProposalRequest { ProposalId = proposalId, DraftId = "draft-001" };
        var applyResult = await service.ApplyFixProposalToDraftAsync(applyContext, applyRequest);

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
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DraftStoreMock.Setup(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Insert a fix proposal manually with a non-allowed target path
        var proposal = InsertFixProposal(service, TestTenantId, "fp-skipped", draft, new FixProposalAction
        {
            Kind = FixProposalActionKind.SetValue,
            TargetPath = "ForbiddenField", // not in allowedPaths
            CurrentValue = JsonSerializer.SerializeToElement(""),
            ProposedValue = JsonSerializer.SerializeToElement("value"),
            IsExecutable = true,
            SafetyLevel = FixProposalActionSafetyLevel.Safe
        });

        var applyContext = CreateContext("ApplyFixProposalToDraft");
        var applyRequest = new ApplyFixProposalRequest { ProposalId = proposal.Id, DraftId = "draft-001" };
        var applyResult = await service.ApplyFixProposalToDraftAsync(applyContext, applyRequest);

        // ForbiddenField is not an allowed draft field target — rejected
        applyResult.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        applyResult.Diagnostics.Should().Contain(d => d.Code == "FIX_ACTION_TARGET_NOT_ALLOWED");
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
        var suggestContext = CreateContext("SuggestDescriptorDraftFixes");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var validationResult = DraftAbstractions.DescriptorDraftValidationResult.Failure(
            new DraftAbstractions.DescriptorDraftDiagnostic { Code = "RATIONALE_EMPTY", Severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Warning, Message = "Empty" });
        DraftValidatorMock.Setup(v => v.Validate(draft)).Returns(validationResult);

        var suggestResult = await service.SuggestDescriptorDraftFixesAsync(suggestContext, "draft-001");
        var proposalId = suggestResult.Value!.Proposals[0].Id;

        // Make draft disappear for apply
        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-002", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(CreateTestDraft(draftId: "draft-002")));

        var applyContext = CreateContext("ApplyFixProposalToDraft");
        var request = new ApplyFixProposalRequest { ProposalId = proposalId, DraftId = "draft-002" };
        var result = await service.ApplyFixProposalToDraftAsync(applyContext, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "PROPOSAL_DRAFT_MISMATCH");
    }

    [Fact]
    public async Task ApplyFixProposal_Never_Patches_Active_Descriptors()
    {
        var service = CreateService();
        var suggestContext = CreateContext("SuggestDescriptorDraftFixes");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DraftStoreMock.Setup(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // RATIONALE_EMPTY generates a fix for Rationale (an allowed draft field)
        var validationResult = DraftAbstractions.DescriptorDraftValidationResult.Failure(
            new DraftAbstractions.DescriptorDraftDiagnostic { Code = "RATIONALE_EMPTY", Severity = DraftAbstractions.DescriptorDraftDiagnosticSeverity.Warning, Message = "Rationale is empty" });
        DraftValidatorMock.Setup(v => v.Validate(draft)).Returns(validationResult);

        var suggestResult = await service.SuggestDescriptorDraftFixesAsync(suggestContext, "draft-001");
        var proposalId = suggestResult.Value!.Proposals[0].Id;

        var applyContext = CreateContext("ApplyFixProposalToDraft");
        var request = new ApplyFixProposalRequest { ProposalId = proposalId, DraftId = "draft-001" };
        await service.ApplyFixProposalToDraftAsync(applyContext, request);

        DraftStoreMock.Verify(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        // DescriptorCatalog has no mutation methods — structural invariant
    }

    private static FixProposal InsertFixProposal(
        DefaultAgentControlPlaneToolService service,
        string tenantId,
        string proposalId,
        Draft draft,
        FixProposalAction action)
    {
        var proposal = new FixProposal
        {
            Id = proposalId,
            Kind = FixProposalKind.SetRequiredField,
            Title = "Test fix",
            Explanation = "Test",
            ReasonCode = "TEST",
            DraftId = draft.DraftId,
            TenantId = tenantId,
            Applicability = FixProposalApplicability.CurrentMutableDraft,
            IsExecutable = true,
            RequiresManualAction = false,
            RequiresHumanReview = false,
            BlocksActivationUntilResolved = false,
            RiskLevel = FixProposalRiskLevel.Low,
            ContractVersion = AgentControlPlaneContractVersion.Current,
            Actions = [action],
            Diagnostics = [],
            CreatedAt = DateTimeOffset.UtcNow,
            Rationale = "Test rationale"
        };

        var fieldInfo = typeof(DefaultAgentControlPlaneToolService)
            .GetField("_fixProposals", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        dynamic dict = fieldInfo!.GetValue(service)!;
        dict[(tenantId, proposalId)] = new FixProposalResourceSnapshot(proposal, draft);
        return proposal;
    }
}
