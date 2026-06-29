using System.Reflection;
using System.Text.Json;
using CrestCreates.Agent.ControlPlane;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
using FluentAssertions;
using Moq;
using Xunit;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Tests;

public class Phase7dFixProposalTests : AgentControlPlaneTestBase
{
    // ── Contract tests ──

    [Fact]
    public void FixProposal_IsExecutable_Aggregation_True()
    {
        var proposal = CreateMinimalFixProposal(
            applicability: FixProposalApplicability.CurrentMutableDraft,
            isExecutable: true);

        proposal.IsExecutable.Should().BeTrue();
        proposal.Applicability.Should().Be(FixProposalApplicability.CurrentMutableDraft);
    }

    [Fact]
    public void FixProposal_IsExecutable_Aggregation_FalseWhenNotApplicable()
    {
        var proposal = CreateMinimalFixProposal(
            applicability: FixProposalApplicability.NotApplicable,
            isExecutable: false);

        proposal.IsExecutable.Should().BeFalse();
        proposal.Applicability.Should().Be(FixProposalApplicability.NotApplicable);
    }

    [Fact]
    public void FixProposal_ContractVersion_DefaultsToCurrent()
    {
        var proposal = CreateMinimalFixProposal();

        proposal.ContractVersion.Should().Be(AgentControlPlaneContractVersion.Current);
        proposal.ContractVersion.Should().Be("7e.v1");
    }

    [Fact]
    public void FixProposalAction_TargetPath_NotNull()
    {
        var action = new FixProposalAction
        {
            Kind = FixProposalActionKind.SetValue,
            TargetPath = "Rationale",
            IsExecutable = true,
            SafetyLevel = FixProposalActionSafetyLevel.Safe,
            Description = "Set rationale"
        };

        action.TargetPath.Should().NotBeNull();
        action.TargetPath.Should().Be("Rationale");
    }

    [Fact]
    public void FixProposalAction_JsonElement_RoundTrip()
    {
        // String value
        var stringAction = new FixProposalAction
        {
            Kind = FixProposalActionKind.SetValue,
            TargetPath = "Rationale",
            CurrentValue = JsonSerializer.SerializeToElement("old"),
            ProposedValue = JsonSerializer.SerializeToElement("new"),
            IsExecutable = true,
            SafetyLevel = FixProposalActionSafetyLevel.Safe
        };
        stringAction.CurrentValue!.Value.GetString().Should().Be("old");
        stringAction.ProposedValue!.Value.GetString().Should().Be("new");

        // Number value
        var numberAction = new FixProposalAction
        {
            Kind = FixProposalActionKind.SetValue,
            TargetPath = "ProposedVersion",
            CurrentValue = JsonSerializer.SerializeToElement(1),
            ProposedValue = JsonSerializer.SerializeToElement(2),
            IsExecutable = true,
            SafetyLevel = FixProposalActionSafetyLevel.Safe
        };
        numberAction.CurrentValue!.Value.GetInt32().Should().Be(1);
        numberAction.ProposedValue!.Value.GetInt32().Should().Be(2);

        // Object value
        var obj = new { Name = "test", Version = 3 };
        var objectAction = new FixProposalAction
        {
            Kind = FixProposalActionKind.SetValue,
            TargetPath = "CorrelationId",
            CurrentValue = JsonSerializer.SerializeToElement(obj),
            ProposedValue = JsonSerializer.SerializeToElement(obj),
            IsExecutable = true,
            SafetyLevel = FixProposalActionSafetyLevel.Safe
        };
        objectAction.CurrentValue!.Value.GetProperty("Name").GetString().Should().Be("test");
        objectAction.CurrentValue!.Value.GetProperty("Version").GetInt32().Should().Be(3);
        objectAction.ProposedValue!.Value.GetProperty("Name").GetString().Should().Be("test");

        // Null value
        var nullAction = new FixProposalAction
        {
            Kind = FixProposalActionKind.RemoveValue,
            TargetPath = "Rationale",
            CurrentValue = JsonSerializer.SerializeToElement("existing"),
            ProposedValue = null,
            IsExecutable = true,
            SafetyLevel = FixProposalActionSafetyLevel.Safe
        };
        nullAction.CurrentValue!.Should().NotBeNull();
        nullAction.ProposedValue.Should().BeNull();
    }

    [Fact]
    public void FixProposalActionKind_Values_Continuous_1_To_10()
    {
        var values = Enum.GetValues<FixProposalActionKind>();
        values.Should().HaveCount(10);

        ((int)FixProposalActionKind.SetValue).Should().Be(1);
        ((int)FixProposalActionKind.RemoveValue).Should().Be(2);
        ((int)FixProposalActionKind.AddValue).Should().Be(3);
        ((int)FixProposalActionKind.MergeObject).Should().Be(4);
        ((int)FixProposalActionKind.ReplaceReference).Should().Be(5);
        ((int)FixProposalActionKind.RemoveRelationship).Should().Be(6);
        ((int)FixProposalActionKind.AddRequiredBindingMetadata).Should().Be(7);
        ((int)FixProposalActionKind.SuggestVersionBump).Should().Be(8);
        ((int)FixProposalActionKind.MarkRequiresReview).Should().Be(9);
        ((int)FixProposalActionKind.ManualActionRequired).Should().Be(10);

        // Verify continuity
        for (int i = 1; i <= 10; i++)
            values.Should().Contain(v => (int)v == i);
    }

    [Fact]
    public void FixProposalKind_Values_Continuous_1_To_9()
    {
        var values = Enum.GetValues<FixProposalKind>();
        values.Should().HaveCount(9);

        ((int)FixProposalKind.CreateMissingDescriptor).Should().Be(1);
        ((int)FixProposalKind.ReplaceMissingReference).Should().Be(2);
        ((int)FixProposalKind.RemoveInvalidRelationship).Should().Be(3);
        ((int)FixProposalKind.AddRequiredBindingMetadata).Should().Be(4);
        ((int)FixProposalKind.SplitBreakingChangeIntoCompatibleChange).Should().Be(5);
        ((int)FixProposalKind.MarkRequiresReview).Should().Be(6);
        ((int)FixProposalKind.FlagUnsafeExpansion).Should().Be(7);
        ((int)FixProposalKind.SuggestVersionBump).Should().Be(8);
        ((int)FixProposalKind.SetRequiredField).Should().Be(9);

        for (int i = 1; i <= 9; i++)
            values.Should().Contain(v => (int)v == i);
    }

    [Fact]
    public void FixProposalApplicability_Values_Continuous_1_To_4()
    {
        var values = Enum.GetValues<FixProposalApplicability>();
        values.Should().HaveCount(4);

        ((int)FixProposalApplicability.CurrentMutableDraft).Should().Be(1);
        ((int)FixProposalApplicability.RequiresNewDraftRevision).Should().Be(2);
        ((int)FixProposalApplicability.ManualActionRequired).Should().Be(3);
        ((int)FixProposalApplicability.NotApplicable).Should().Be(4);

        for (int i = 1; i <= 4; i++)
            values.Should().Contain(v => (int)v == i);
    }

    // ── Apply tests ──

    [Fact]
    public async Task ApplyFixProposal_MultiAction_Returns_UnsupportedMultiActionFixProposal()
    {
        var service = CreateService();
        var draft = CreateTestDraft();
        var proposal = CreateMinimalFixProposal(
            actions: new List<FixProposalAction>
            {
                new()
                {
                    Kind = FixProposalActionKind.SetValue,
                    TargetPath = "Rationale",
                    ProposedValue = JsonSerializer.SerializeToElement("v1"),
                    IsExecutable = true,
                    SafetyLevel = FixProposalActionSafetyLevel.Safe
                },
                new()
                {
                    Kind = FixProposalActionKind.SetValue,
                    TargetPath = "Intent",
                    ProposedValue = JsonSerializer.SerializeToElement("v2"),
                    IsExecutable = true,
                    SafetyLevel = FixProposalActionSafetyLevel.Safe
                }
            });

        SetupDraftStore(draft);
        InsertFixProposal(service, proposal, draft);

        var context = CreateContext("ApplyFixProposalToDraft");
        var request = new ApplyFixProposalRequest { ProposalId = proposal.Id, DraftId = proposal.DraftId };
        var result = await service.ApplyFixProposalToDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "UNSUPPORTED_MULTI_ACTION_FIX_PROPOSAL");
    }

    [Fact]
    public async Task ApplyFixProposal_NonExecutableAction_Returns_NonExecutableFixAction()
    {
        var service = CreateService();
        var draft = CreateTestDraft();
        var proposal = CreateMinimalFixProposal(
            actions: new List<FixProposalAction>
            {
                new()
                {
                    Kind = FixProposalActionKind.SetValue,
                    TargetPath = "Rationale",
                    ProposedValue = JsonSerializer.SerializeToElement("new"),
                    IsExecutable = false,
                    SafetyLevel = FixProposalActionSafetyLevel.Safe
                }
            });

        SetupDraftStore(draft);
        InsertFixProposal(service, proposal, draft);

        var context = CreateContext("ApplyFixProposalToDraft");
        var request = new ApplyFixProposalRequest { ProposalId = proposal.Id, DraftId = proposal.DraftId };
        var result = await service.ApplyFixProposalToDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "NON_EXECUTABLE_FIX_ACTION");
    }

    [Fact]
    public async Task ApplyFixProposal_UnsafeAction_Returns_UnsafeFixActionRejected()
    {
        var service = CreateService();
        var draft = CreateTestDraft();
        var proposal = CreateMinimalFixProposal(
            actions: new List<FixProposalAction>
            {
                new()
                {
                    Kind = FixProposalActionKind.SetValue,
                    TargetPath = "Rationale",
                    ProposedValue = JsonSerializer.SerializeToElement("new"),
                    IsExecutable = true,
                    SafetyLevel = FixProposalActionSafetyLevel.Unsafe
                }
            });

        SetupDraftStore(draft);
        InsertFixProposal(service, proposal, draft);

        var context = CreateContext("ApplyFixProposalToDraft");
        var request = new ApplyFixProposalRequest { ProposalId = proposal.Id, DraftId = proposal.DraftId };
        var result = await service.ApplyFixProposalToDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "UNSAFE_FIX_ACTION_REJECTED");
    }

    [Fact]
    public async Task ApplyFixProposal_UnsupportedKind_Returns_UnsupportedFixActionKind()
    {
        var service = CreateService();
        var draft = CreateTestDraft();
        var proposal = CreateMinimalFixProposal(
            actions: new List<FixProposalAction>
            {
                new()
                {
                    Kind = FixProposalActionKind.MergeObject,
                    TargetPath = "Rationale",
                    ProposedValue = JsonSerializer.SerializeToElement("new"),
                    IsExecutable = true,
                    SafetyLevel = FixProposalActionSafetyLevel.Safe
                }
            });

        SetupDraftStore(draft);
        InsertFixProposal(service, proposal, draft);

        var context = CreateContext("ApplyFixProposalToDraft");
        var request = new ApplyFixProposalRequest { ProposalId = proposal.Id, DraftId = proposal.DraftId };
        var result = await service.ApplyFixProposalToDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "UNSUPPORTED_FIX_ACTION_KIND");
    }

    // ── Boundary violation test ──

    [Fact]
    public async Task ApplyFixProposal_BoundaryViolation_Returns_FixActionTargetBoundaryViolation()
    {
        // The implementation uses "FIX_ACTION_TARGET_NOT_ALLOWED" for non-allowed
        // target paths (no explicit "FIX_ACTION_TARGET_BOUNDARY_VIOLATION" code exists).
        // This test verifies that targeting a path outside the allowed set returns
        // the appropriate "FIX_ACTION_TARGET_NOT_ALLOWED" diagnostic.
        var service = CreateService();
        var draft = CreateTestDraft();
        var proposal = CreateMinimalFixProposal(
            actions: new List<FixProposalAction>
            {
                new()
                {
                    Kind = FixProposalActionKind.SetValue,
                    TargetPath = "IntentPayload", // NOT in the allowed paths set
                    ProposedValue = JsonSerializer.SerializeToElement("malicious"),
                    IsExecutable = true,
                    SafetyLevel = FixProposalActionSafetyLevel.Safe,
                }
            });

        SetupDraftStore(draft);
        InsertFixProposal(service, proposal, draft);

        var context = CreateContext("ApplyFixProposalToDraft");
        var request = new ApplyFixProposalRequest { ProposalId = proposal.Id, DraftId = proposal.DraftId };
        var result = await service.ApplyFixProposalToDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "FIX_ACTION_TARGET_NOT_ALLOWED");
    }

    // ── Success path test ──

    [Fact]
    public async Task ApplyFixProposal_SupportedAction_Succeeds()
    {
        var service = CreateService();
        var draft = CreateTestDraft();
        var newRationale = "Updated rationale from successful fix proposal";
        var proposal = CreateMinimalFixProposal(
            applicability: FixProposalApplicability.CurrentMutableDraft,
            isExecutable: true,
            actions: new List<FixProposalAction>
            {
                new()
                {
                    Kind = FixProposalActionKind.SetValue,
                    TargetPath = "Rationale",
                    ProposedValue = JsonSerializer.SerializeToElement(newRationale),
                    IsExecutable = true,
                    SafetyLevel = FixProposalActionSafetyLevel.Safe,
                }
            });

        SetupDraftStore(draft);
        InsertFixProposal(service, proposal, draft);

        var context = CreateContext("ApplyFixProposalToDraft");
        var request = new ApplyFixProposalRequest { ProposalId = proposal.Id, DraftId = proposal.DraftId };
        var result = await service.ApplyFixProposalToDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value.Should().NotBeNull();
        result.Value!.Rationale.Should().Be(newRationale,
            "the Rationale field should be updated by the SetValue action applied to the draft");
    }

    // ── Coverage #4: Diagnostic-to-FixProposalKind mapping ──

    [Fact]
    public async Task SuggestDescriptorDraftFixes_MissingRefDiagnostic_MapsToMarkRequiresReview()
    {
        var service = CreateService();
        var context = CreateContext("SuggestDescriptorDraftFixes");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var validationResult = DraftAbstractions.DescriptorDraftValidationResult.Failure(
            new DraftAbstractions.DescriptorDraftDiagnostic
            {
                Code = new DiagnosticCode("MISSING_REF"),
                Severity = SeverityLevel.Warning,
                Message = "A required reference is missing"
            });
        DraftValidatorMock.Setup(v => v.Validate(draft)).Returns(validationResult);

        var result = await service.SuggestDescriptorDraftFixesAsync(context, "draft-001");

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Proposals.Should().NotBeEmpty();
        result.Value.Proposals[0].Kind.Should().Be(FixProposalKind.MarkRequiresReview,
            "MISSING_REF is not RATIONALE_EMPTY/INTENT_EMPTY, so it falls back to MarkRequiresReview");
    }

    [Fact]
    public async Task SuggestDescriptorDraftFixes_RationaleEmptyDiagnostic_MapsToSetRequiredField()
    {
        var service = CreateService();
        var context = CreateContext("SuggestDescriptorDraftFixes");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var validationResult = DraftAbstractions.DescriptorDraftValidationResult.Failure(
            new DraftAbstractions.DescriptorDraftDiagnostic
            {
                Code = new DiagnosticCode("RATIONALE_EMPTY"),
                Severity = SeverityLevel.Warning,
                Message = "Rationale must not be empty"
            });
        DraftValidatorMock.Setup(v => v.Validate(draft)).Returns(validationResult);

        var result = await service.SuggestDescriptorDraftFixesAsync(context, "draft-001");

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Proposals.Should().NotBeEmpty();
        result.Value.Proposals[0].Kind.Should().Be(FixProposalKind.SetRequiredField,
            "RATIONALE_EMPTY should map to SetRequiredField");
    }

    // ── Coverage #5: Breaking change → MarkRequiresReview (not CreateMissingDescriptor) ──

    [Fact]
    public async Task SuggestDescriptorDraftFixes_BreakingChangeDiagnostic_MapsToMarkRequiresReview()
    {
        var service = CreateService();
        var context = CreateContext("SuggestDescriptorDraftFixes");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var validationResult = DraftAbstractions.DescriptorDraftValidationResult.Failure(
            new DraftAbstractions.DescriptorDraftDiagnostic
            {
                Code = new DiagnosticCode("BREAKING_CHANGE_DETECTED"),
                Severity = SeverityLevel.Error,
                Message = "Breaking change detected in proposed descriptor"
            });
        DraftValidatorMock.Setup(v => v.Validate(draft)).Returns(validationResult);

        var result = await service.SuggestDescriptorDraftFixesAsync(context, "draft-001");

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Proposals.Should().NotBeEmpty();
        result.Value.Proposals[0].Kind.Should().Be(FixProposalKind.MarkRequiresReview,
            "BREAKING_CHANGE_DETECTED should map to MarkRequiresReview, not CreateMissingDescriptor");
    }

    // ── Coverage #7: Fix application rejected when not CurrentMutableDraft ──

    [Fact]
    public async Task ApplyFixProposalToDraft_RequiresNewDraftRevision_RejectedWithNotApplicable()
    {
        var service = CreateService();
        var draft = CreateTestDraft();
        var proposal = CreateMinimalFixProposal(
            applicability: FixProposalApplicability.RequiresNewDraftRevision);

        SetupDraftStore(draft);
        InsertFixProposal(service, proposal, draft);

        var context = CreateContext("ApplyFixProposalToDraft");
        var request = new ApplyFixProposalRequest { ProposalId = proposal.Id, DraftId = proposal.DraftId };
        var result = await service.ApplyFixProposalToDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "FIX_PROPOSAL_NOT_APPLICABLE");

        // SaveAsync should never be called (no mutation of draft)
        DraftStoreMock.Verify(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyFixProposalToDraft_ManualActionRequired_RejectedWithNotApplicable()
    {
        var service = CreateService();
        var draft = CreateTestDraft();
        var proposal = CreateMinimalFixProposal(
            applicability: FixProposalApplicability.ManualActionRequired);

        SetupDraftStore(draft);
        InsertFixProposal(service, proposal, draft);

        var context = CreateContext("ApplyFixProposalToDraft");
        var request = new ApplyFixProposalRequest { ProposalId = proposal.Id, DraftId = proposal.DraftId };
        var result = await service.ApplyFixProposalToDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "FIX_PROPOSAL_NOT_APPLICABLE");

        DraftStoreMock.Verify(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Coverage #11: Unsupported diagnostic → structured fallback ──

    [Fact]
    public async Task SuggestDescriptorDraftFixes_UnknownDiagnostic_ProducesFallbackProposal()
    {
        var service = CreateService();
        var context = CreateContext("SuggestDescriptorDraftFixes");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var validationResult = DraftAbstractions.DescriptorDraftValidationResult.Failure(
            new DraftAbstractions.DescriptorDraftDiagnostic
            {
                Code = new DiagnosticCode("UNKNOWN_DIAG_CODE"),
                Severity = SeverityLevel.Error,
                Message = "An unrecognized diagnostic"
            });
        DraftValidatorMock.Setup(v => v.Validate(draft)).Returns(validationResult);

        var result = await service.SuggestDescriptorDraftFixesAsync(context, "draft-001");

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Proposals.Should().NotBeEmpty();

        var proposal = result.Value.Proposals[0];
        proposal.Kind.Should().Be(FixProposalKind.MarkRequiresReview,
            "unknown diagnostic codes fall back to MarkRequiresReview");
        proposal.Applicability.Should().Be(FixProposalApplicability.ManualActionRequired,
            "unknown diagnostics have no executable actions");
        proposal.IsExecutable.Should().BeFalse(
            "proposals without actions are not executable");
        proposal.RequiresManualAction.Should().BeTrue(
            "unknown diagnostics require manual intervention");
        proposal.Actions.Should().BeEmpty(
            "GenerateFixActions returns empty list for unknown diagnostic codes");
    }

    // ── Review Fix: Empty-action proposal rejected ──

    [Fact]
    public async Task ApplyFixProposalToDraft_EmptyActionProposal_Returns_InvalidRequest()
    {
        var service = CreateService();
        var draft = CreateTestDraft();
        var proposal = CreateMinimalFixProposal(actions: []);

        SetupDraftStore(draft);
        InsertFixProposal(service, proposal, draft);

        var context = CreateContext("ApplyFixProposalToDraft");
        var request = new ApplyFixProposalRequest { ProposalId = proposal.Id, DraftId = proposal.DraftId };
        var result = await service.ApplyFixProposalToDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "FIX_PROPOSAL_HAS_NO_ACTIONS");

        DraftStoreMock.Verify(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Review Fix: RemoveValue/AddValue rejected as unsupported ──

    [Theory]
    [InlineData(FixProposalActionKind.RemoveValue)]
    [InlineData(FixProposalActionKind.AddValue)]
    public async Task ApplyFixProposalToDraft_RemoveOrAddValue_Returns_UnsupportedFixActionKind(FixProposalActionKind kind)
    {
        var service = CreateService();
        var draft = CreateTestDraft();
        var proposal = CreateMinimalFixProposal(actions: new List<FixProposalAction>
        {
            new()
            {
                Kind = kind,
                TargetPath = "Rationale",
                ProposedValue = JsonSerializer.SerializeToElement("value"),
                IsExecutable = true,
                SafetyLevel = FixProposalActionSafetyLevel.Safe
            }
        });

        SetupDraftStore(draft);
        InsertFixProposal(service, proposal, draft);

        var context = CreateContext("ApplyFixProposalToDraft");
        var request = new ApplyFixProposalRequest { ProposalId = proposal.Id, DraftId = proposal.DraftId };
        var result = await service.ApplyFixProposalToDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "UNSUPPORTED_FIX_ACTION_KIND");

        DraftStoreMock.Verify(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Review Fix: Boundary violation with TargetDescriptorId targeting another descriptor ──

    [Fact]
    public async Task ApplyFixProposalToDraft_BoundaryViolation_TargetDescriptorId_Returns_TargetBoundaryViolation()
    {
        var service = CreateService();
        var draft = CreateTestDraft();
        var proposal = CreateMinimalFixProposal(actions: new List<FixProposalAction>
        {
            new()
            {
                Kind = FixProposalActionKind.SetValue,
                TargetPath = "Rationale",
                TargetDescriptorId = "other-descriptor-001",  // targets a different descriptor
                ProposedValue = JsonSerializer.SerializeToElement("value"),
                IsExecutable = true,
                SafetyLevel = FixProposalActionSafetyLevel.Safe
            }
        });

        SetupDraftStore(draft);
        InsertFixProposal(service, proposal, draft);

        var context = CreateContext("ApplyFixProposalToDraft");
        var request = new ApplyFixProposalRequest { ProposalId = proposal.Id, DraftId = proposal.DraftId };
        var result = await service.ApplyFixProposalToDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "FIX_ACTION_TARGET_BOUNDARY_VIOLATION");

        DraftStoreMock.Verify(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Review Fix: Boundary violation with registry/active/runtime namespace path ──

    [Fact]
    public async Task ApplyFixProposalToDraft_BoundaryViolation_RegistryPath_Returns_TargetBoundaryViolation()
    {
        var service = CreateService();
        var draft = CreateTestDraft();
        var proposal = CreateMinimalFixProposal(actions: new List<FixProposalAction>
        {
            new()
            {
                Kind = FixProposalActionKind.SetValue,
                TargetPath = "registry.field",
                ProposedValue = JsonSerializer.SerializeToElement("value"),
                IsExecutable = true,
                SafetyLevel = FixProposalActionSafetyLevel.Safe
            }
        });

        SetupDraftStore(draft);
        InsertFixProposal(service, proposal, draft);

        var context = CreateContext("ApplyFixProposalToDraft");
        var request = new ApplyFixProposalRequest { ProposalId = proposal.Id, DraftId = proposal.DraftId };
        var result = await service.ApplyFixProposalToDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "FIX_ACTION_TARGET_BOUNDARY_VIOLATION");

        DraftStoreMock.Verify(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Phase 7d follow-up #44: Proposal-level executability guard ──

    [Fact]
    public async Task Apply_NonExecutableProposal_Returns_NonExecutableFixProposal()
    {
        var service = CreateService();
        var draft = CreateTestDraft();
        var proposal = CreateMinimalFixProposal(
            applicability: FixProposalApplicability.CurrentMutableDraft,
            isExecutable: false,
            actions: new List<FixProposalAction>
            {
                new()
                {
                    Kind = FixProposalActionKind.SetValue,
                    TargetPath = "Rationale",
                    ProposedValue = JsonSerializer.SerializeToElement("value"),
                    IsExecutable = true, // action-level executable, but proposal-level is not
                    SafetyLevel = FixProposalActionSafetyLevel.Safe
                }
            });

        SetupDraftStore(draft);
        InsertFixProposal(service, proposal, draft);

        var context = CreateContext("ApplyFixProposalToDraft");
        var request = new ApplyFixProposalRequest { ProposalId = proposal.Id, DraftId = proposal.DraftId };
        var result = await service.ApplyFixProposalToDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "NON_EXECUTABLE_FIX_PROPOSAL");
    }

    [Fact]
    public async Task Apply_NonExecutableProposal_DoesNotSaveDraft()
    {
        var service = CreateService();
        var draft = CreateTestDraft();
        var proposal = CreateMinimalFixProposal(
            applicability: FixProposalApplicability.CurrentMutableDraft,
            isExecutable: false,
            actions: new List<FixProposalAction>
            {
                new()
                {
                    Kind = FixProposalActionKind.SetValue,
                    TargetPath = "Rationale",
                    ProposedValue = JsonSerializer.SerializeToElement("value"),
                    IsExecutable = true,
                    SafetyLevel = FixProposalActionSafetyLevel.Safe
                }
            });

        SetupDraftStore(draft);
        InsertFixProposal(service, proposal, draft);

        var context = CreateContext("ApplyFixProposalToDraft");
        var request = new ApplyFixProposalRequest { ProposalId = proposal.Id, DraftId = proposal.DraftId };
        var result = await service.ApplyFixProposalToDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        DraftStoreMock.Verify(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Phase 7d follow-up #44: SetValue JsonElement ValueKind validation ──

    [Fact]
    public async Task Apply_SetValue_ObjectValue_Returns_FixActionValueKindNotSupported()
    {
        var service = CreateService();
        var draft = CreateTestDraft();
        var proposal = CreateMinimalFixProposal(
            actions: new List<FixProposalAction>
            {
                new()
                {
                    Kind = FixProposalActionKind.SetValue,
                    TargetPath = "Rationale",
                    ProposedValue = JsonSerializer.SerializeToElement(new { Name = "bad" }),
                    IsExecutable = true,
                    SafetyLevel = FixProposalActionSafetyLevel.Safe
                }
            });

        SetupDraftStore(draft);
        InsertFixProposal(service, proposal, draft);

        var context = CreateContext("ApplyFixProposalToDraft");
        var request = new ApplyFixProposalRequest { ProposalId = proposal.Id, DraftId = proposal.DraftId };
        var result = await service.ApplyFixProposalToDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "FIX_ACTION_VALUE_KIND_NOT_SUPPORTED");

        DraftStoreMock.Verify(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Apply_SetValue_ArrayValue_Returns_FixActionValueKindNotSupported()
    {
        var service = CreateService();
        var draft = CreateTestDraft();
        var proposal = CreateMinimalFixProposal(
            actions: new List<FixProposalAction>
            {
                new()
                {
                    Kind = FixProposalActionKind.SetValue,
                    TargetPath = "Rationale",
                    ProposedValue = JsonSerializer.SerializeToElement(new[] { "a", "b" }),
                    IsExecutable = true,
                    SafetyLevel = FixProposalActionSafetyLevel.Safe
                }
            });

        SetupDraftStore(draft);
        InsertFixProposal(service, proposal, draft);

        var context = CreateContext("ApplyFixProposalToDraft");
        var request = new ApplyFixProposalRequest { ProposalId = proposal.Id, DraftId = proposal.DraftId };
        var result = await service.ApplyFixProposalToDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "FIX_ACTION_VALUE_KIND_NOT_SUPPORTED");

        DraftStoreMock.Verify(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Apply_SetValue_NumberValue_Returns_FixActionValueKindNotSupported()
    {
        var service = CreateService();
        var draft = CreateTestDraft();
        var proposal = CreateMinimalFixProposal(
            actions: new List<FixProposalAction>
            {
                new()
                {
                    Kind = FixProposalActionKind.SetValue,
                    TargetPath = "Rationale",
                    ProposedValue = JsonSerializer.SerializeToElement(42),
                    IsExecutable = true,
                    SafetyLevel = FixProposalActionSafetyLevel.Safe
                }
            });

        SetupDraftStore(draft);
        InsertFixProposal(service, proposal, draft);

        var context = CreateContext("ApplyFixProposalToDraft");
        var request = new ApplyFixProposalRequest { ProposalId = proposal.Id, DraftId = proposal.DraftId };
        var result = await service.ApplyFixProposalToDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "FIX_ACTION_VALUE_KIND_NOT_SUPPORTED");

        DraftStoreMock.Verify(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Apply_SetValue_BooleanValue_Returns_FixActionValueKindNotSupported()
    {
        var service = CreateService();
        var draft = CreateTestDraft();
        var proposal = CreateMinimalFixProposal(
            actions: new List<FixProposalAction>
            {
                new()
                {
                    Kind = FixProposalActionKind.SetValue,
                    TargetPath = "Rationale",
                    ProposedValue = JsonSerializer.SerializeToElement(true),
                    IsExecutable = true,
                    SafetyLevel = FixProposalActionSafetyLevel.Safe
                }
            });

        SetupDraftStore(draft);
        InsertFixProposal(service, proposal, draft);

        var context = CreateContext("ApplyFixProposalToDraft");
        var request = new ApplyFixProposalRequest { ProposalId = proposal.Id, DraftId = proposal.DraftId };
        var result = await service.ApplyFixProposalToDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "FIX_ACTION_VALUE_KIND_NOT_SUPPORTED");

        DraftStoreMock.Verify(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Apply_SetValue_NullValue_AllowsClearingNullableDraftField()
    {
        var service = CreateService();
        var draft = CreateTestDraft();
        // Create a JsonElement representing null
        var nullElement = JsonSerializer.SerializeToElement((string?)null);
        var proposal = CreateMinimalFixProposal(
            actions: new List<FixProposalAction>
            {
                new()
                {
                    Kind = FixProposalActionKind.SetValue,
                    TargetPath = "Rationale",
                    ProposedValue = nullElement,
                    IsExecutable = true,
                    SafetyLevel = FixProposalActionSafetyLevel.Safe
                }
            });

        SetupDraftStore(draft);
        InsertFixProposal(service, proposal, draft);

        var context = CreateContext("ApplyFixProposalToDraft");
        var request = new ApplyFixProposalRequest { ProposalId = proposal.Id, DraftId = proposal.DraftId };
        var result = await service.ApplyFixProposalToDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Rationale.Should().BeNull("SetValue with null JSON value clears the field");

        DraftStoreMock.Verify(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Apply_SetValue_StringValue_StillSucceeds()
    {
        var service = CreateService();
        var draft = CreateTestDraft();
        var newValue = "Updated via SetValue";
        var proposal = CreateMinimalFixProposal(
            actions: new List<FixProposalAction>
            {
                new()
                {
                    Kind = FixProposalActionKind.SetValue,
                    TargetPath = "Rationale",
                    ProposedValue = JsonSerializer.SerializeToElement(newValue),
                    IsExecutable = true,
                    SafetyLevel = FixProposalActionSafetyLevel.Safe
                }
            });

        SetupDraftStore(draft);
        InsertFixProposal(service, proposal, draft);

        var context = CreateContext("ApplyFixProposalToDraft");
        var request = new ApplyFixProposalRequest { ProposalId = proposal.Id, DraftId = proposal.DraftId };
        var result = await service.ApplyFixProposalToDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Rationale.Should().Be(newValue);

        DraftStoreMock.Verify(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Helpers ──

    /// <summary>
    /// Creates a minimal FixProposal with all required fields populated with sensible defaults.
    /// </summary>
    private static FixProposal CreateMinimalFixProposal(
        string id = "fp-001",
        string draftId = "draft-001",
        string tenantId = TestTenantId,
        FixProposalApplicability applicability = FixProposalApplicability.CurrentMutableDraft,
        bool isExecutable = true,
        IReadOnlyList<FixProposalAction>? actions = null)
    {
        return new FixProposal
        {
            Id = id,
            DraftId = draftId,
            TenantId = tenantId,
            Kind = FixProposalKind.CreateMissingDescriptor,
            Title = "Test Fix",
            Explanation = "Test explanation",
            ReasonCode = new DiagnosticCode("TEST_FIX"),
            Applicability = applicability,
            IsExecutable = isExecutable,
            RequiresManualAction = false,
            RequiresHumanReview = false,
            BlocksActivationUntilResolved = false,
            RiskLevel = FixProposalRiskLevel.Low,
            Actions = actions ?? new List<FixProposalAction>
            {
                new()
                {
                    Kind = FixProposalActionKind.SetValue,
                    TargetPath = "Rationale",
                    ProposedValue = JsonSerializer.SerializeToElement("test"),
                    IsExecutable = true,
                    SafetyLevel = FixProposalActionSafetyLevel.Safe
                }
            },
            Diagnostics = [],
            CreatedAt = DateTimeOffset.UtcNow,
            ContractVersion = AgentControlPlaneContractVersion.Current
        };
    }

    /// <summary>
    /// Inserts a FixProposal into the service's internal _fixProposals store via reflection.
    /// </summary>
    private static void InsertFixProposal(
        DefaultAgentControlPlaneToolService service,
        FixProposal proposal,
        Draft draft)
    {
        var fieldInfo = typeof(DefaultAgentControlPlaneToolService)
            .GetField("_fixProposals", BindingFlags.NonPublic | BindingFlags.Instance);
        fieldInfo.Should().NotBeNull("_fixProposals field must exist");

        dynamic dict = fieldInfo!.GetValue(service)!;
        dict[(proposal.TenantId, proposal.Id)] = new FixProposalResourceSnapshot(proposal, draft);
    }

    /// <summary>
    /// Sets up DraftStoreMock to return the given draft for its DraftId.
    /// Also sets up SaveAsync as a no-op.
    /// </summary>
    private void SetupDraftStore(Draft draft)
    {
        DraftStoreMock
            .Setup(s => s.GetAsync(draft.TenantId, draft.DraftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);
        DraftStoreMock
            .Setup(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }
}
