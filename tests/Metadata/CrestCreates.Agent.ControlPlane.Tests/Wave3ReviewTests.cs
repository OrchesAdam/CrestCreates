using Xunit;
using Moq;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;

using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Tests;

public class Wave3ReviewTests : AgentControlPlaneTestBase
{
    [Fact]
    public async Task ValidateDescriptorDraft_Returns_ValidationResult()
    {
        var service = CreateService();
        var context = CreateContext("ValidateDescriptorDraft");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var validationResult = DraftAbstractions.DescriptorDraftValidationResult.Success();
        DraftValidatorMock.Setup(v => v.Validate(draft)).Returns(validationResult);

        var result = await service.ValidateDescriptorDraftAsync(context, "draft-001");

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateDescriptorDraft_Returns_Invalid_When_Validation_Fails()
    {
        var service = CreateService();
        var context = CreateContext("ValidateDescriptorDraft");
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

        var result = await service.ValidateDescriptorDraftAsync(context, "draft-001");

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateDescriptorDraft_Returns_NotFound_When_Draft_Missing()
    {
        var service = CreateService();
        var context = CreateContext("ValidateDescriptorDraft");

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "nonexistent", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(null));

        var result = await service.ValidateDescriptorDraftAsync(context, "nonexistent");

        result.Status.Should().Be(AgentToolResultStatus.NotFound);
    }

    [Fact]
    public async Task ReviewDescriptorDraft_Returns_ReviewResult()
    {
        var service = CreateService();
        var context = CreateContext("ReviewDescriptorDraft");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DraftStoreMock.Setup(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns(new List<IDescriptor>());

        var reviewResult = new DraftAbstractions.DescriptorDraftReviewResult
        {
            DraftId = "draft-001",
            TenantId = TestTenantId,
            ValidationResult = DraftAbstractions.DescriptorDraftValidationResult.Success(),
            Diagnostics = Array.Empty<DraftAbstractions.DescriptorDraftDiagnostic>(),
            IsActivationEligible = true
        };
        DraftReviewServiceMock.Setup(r => r.ReviewAsync(draft, It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reviewResult);

        var result = await service.ReviewDescriptorDraftAsync(context, "draft-001");

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.IsActivationEligible.Should().BeTrue();
    }

    [Fact]
    public async Task ReviewDescriptorDraft_Updates_Draft_Status_To_Reviewed()
    {
        var service = CreateService();
        var context = CreateContext("ReviewDescriptorDraft");
        var draft = CreateTestDraft();

        Draft? savedDraft = null;
        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DraftStoreMock.Setup(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()))
            .Callback<Draft, CancellationToken>((d, _) => savedDraft = d)
            .Returns(Task.CompletedTask);
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns(new List<IDescriptor>());

        var reviewResult = new DraftAbstractions.DescriptorDraftReviewResult
        {
            DraftId = "draft-001",
            TenantId = TestTenantId,
            ValidationResult = DraftAbstractions.DescriptorDraftValidationResult.Success(),
            Diagnostics = Array.Empty<DraftAbstractions.DescriptorDraftDiagnostic>(),
            IsActivationEligible = true
        };
        DraftReviewServiceMock.Setup(r => r.ReviewAsync(draft, It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reviewResult);

        await service.ReviewDescriptorDraftAsync(context, "draft-001");

        savedDraft.Should().NotBeNull();
        savedDraft!.Status.Should().Be(DraftAbstractions.DescriptorDraftStatus.Reviewed);
    }

    [Fact]
    public async Task ReviewDescriptorDraft_Returns_NotFound_When_Draft_Missing()
    {
        var service = CreateService();
        var context = CreateContext("ReviewDescriptorDraft");

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "nonexistent", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(null));

        var result = await service.ReviewDescriptorDraftAsync(context, "nonexistent");

        result.Status.Should().Be(AgentToolResultStatus.NotFound);
    }

    [Fact]
    public async Task ReviewDescriptorDraft_ReviewPass_Does_Not_Mean_Activation_Approval()
    {
        var service = CreateService();
        var context = CreateContext("ReviewDescriptorDraft");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DraftStoreMock.Setup(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns(new List<IDescriptor>());

        var reviewResult = new DraftAbstractions.DescriptorDraftReviewResult
        {
            DraftId = "draft-001",
            TenantId = TestTenantId,
            ValidationResult = DraftAbstractions.DescriptorDraftValidationResult.Success(),
            Diagnostics = Array.Empty<DraftAbstractions.DescriptorDraftDiagnostic>(),
            IsActivationEligible = true
        };
        DraftReviewServiceMock.Setup(r => r.ReviewAsync(draft, It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reviewResult);

        var result = await service.ReviewDescriptorDraftAsync(context, "draft-001");

        result.Value!.IsActivationEligible.Should().BeTrue();
        // IsActivationEligible ≠ approved. Activation requires a separate activation request.
    }

    [Fact]
    public async Task GetDraftReviewResult_Returns_Stored_Result()
    {
        var service = CreateService();
        var context = CreateContext("ReviewDescriptorDraft");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DraftStoreMock.Setup(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns(new List<IDescriptor>());

        var reviewResult = new DraftAbstractions.DescriptorDraftReviewResult
        {
            DraftId = "draft-001",
            TenantId = TestTenantId,
            ValidationResult = DraftAbstractions.DescriptorDraftValidationResult.Success(),
            Diagnostics = Array.Empty<DraftAbstractions.DescriptorDraftDiagnostic>(),
            IsActivationEligible = true
        };
        DraftReviewServiceMock.Setup(r => r.ReviewAsync(draft, It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reviewResult);

        var reviewOutcome = await service.ReviewDescriptorDraftAsync(context, "draft-001");
        reviewOutcome.Status.Should().Be(AgentToolResultStatus.Success);

        var auditRecord = InMemoryAuditor.GetAllRecords().First(r =>
            r.Context.ToolName == "ReviewDescriptorDraft" &&
            r.TouchedReviewResultIds is not null);

        var reviewResultId = auditRecord.TouchedReviewResultIds!.First();
        var getResult = await service.GetDraftReviewResultAsync(context, reviewResultId);

        getResult.Status.Should().Be(AgentToolResultStatus.Success);
        getResult.Value!.DraftId.Should().Be("draft-001");
    }

    [Fact]
    public async Task GetDraftReviewResult_Returns_NotFound_When_Missing()
    {
        var service = CreateService();
        var context = CreateContext("GetDraftReviewResult");

        var result = await service.GetDraftReviewResultAsync(context, "nonexistent");

        result.Status.Should().Be(AgentToolResultStatus.NotFound);
    }

    [Fact]
    public async Task ListDraftReviewResults_Returns_Results_For_Tenant()
    {
        var service = CreateService();
        var context = CreateContext("ReviewDescriptorDraft");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DraftStoreMock.Setup(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        DescriptorCatalogMock.Setup(c => c.GetAll()).Returns(new List<IDescriptor>());

        var reviewResult = new DraftAbstractions.DescriptorDraftReviewResult
        {
            DraftId = "draft-001",
            TenantId = TestTenantId,
            ValidationResult = DraftAbstractions.DescriptorDraftValidationResult.Success(),
            Diagnostics = Array.Empty<DraftAbstractions.DescriptorDraftDiagnostic>(),
            IsActivationEligible = true
        };
        DraftReviewServiceMock.Setup(r => r.ReviewAsync(draft, It.IsAny<IReadOnlyList<IDescriptor>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(reviewResult);

        await service.ReviewDescriptorDraftAsync(context, "draft-001");

        var listResult = await service.ListDraftReviewResultsAsync(context, null);

        listResult.Status.Should().Be(AgentToolResultStatus.Success);
        listResult.Value!.Results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExplainDiagnostics_Returns_Explanations()
    {
        var service = CreateService();
        var context = CreateContext("ExplainDiagnostics");

        var request = new ExplainDiagnosticsRequest
        {
            Diagnostics = new List<AgentToolDiagnostic>
            {
                new() { Code = "DRAFT_ID_EMPTY", Severity = AgentToolDiagnosticSeverity.Error, Message = "Draft ID is empty" },
                new() { Code = "UNKNOWN_CODE", Severity = AgentToolDiagnosticSeverity.Warning, Message = "Something unknown" }
            },
            DraftId = "draft-001"
        };

        var result = await service.ExplainDiagnosticsAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Explanations.Should().HaveCount(2);
        result.Value.Explanations[0].Code.Should().Be("DRAFT_ID_EMPTY");
        result.Value.Explanations[0].Explanation.Should().NotBeNullOrEmpty();
        result.Value.Explanations[0].Remediation.Should().NotBeNullOrEmpty();
        result.Value.Explanations[1].Explanation.Should().Contain("No detailed explanation");
    }

    [Fact]
    public async Task ExplainDiagnostics_Suggests_Fix_Tools_For_Known_Codes()
    {
        var service = CreateService();
        var context = CreateContext("ExplainDiagnostics");

        var request = new ExplainDiagnosticsRequest
        {
            Diagnostics = [new AgentToolDiagnostic { Code = "DRAFT_ID_EMPTY", Severity = AgentToolDiagnosticSeverity.Error, Message = "Empty" }]
        };

        var result = await service.ExplainDiagnosticsAsync(context, request);

        result.Value!.Explanations[0].SuggestedFixToolNames.Should().Contain("SuggestDescriptorDraftFixes");
    }
}
