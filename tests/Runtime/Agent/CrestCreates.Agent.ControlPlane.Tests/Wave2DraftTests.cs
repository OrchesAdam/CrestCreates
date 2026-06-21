using Xunit;
using Moq;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.DraftContracts.Projection;
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using CrestCreates.Agent.DraftContracts.Dto;
using AgentDraftContractErrorCodes = CrestCreates.Agent.DraftContracts.Dto.AgentDraftContractErrorCodes;

using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;
using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Tests;

public class Wave2DraftTests : AgentControlPlaneTestBase
{
    [Fact]
    public async Task CreateDescriptorDraft_Creates_Draft_With_Correct_Properties()
    {
        var service = CreateService();
        var context = CreateContext("CreateDescriptorDraft");
        var request = new CreateDescriptorDraftRequest
        {
            DescriptorKind = DescriptorKind.Event,
            DescriptorId = "test.desc-001",
            Operation = DraftAbstractions.DescriptorDraftOperation.Create,
            Payload = CreateTestPayloadDto(DescriptorKind.Event, "test.desc-001", "TestEvent"),
            ProposedVersion = "1",
            Intent = "Create new event"
        };

        DraftStoreMock.Setup(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await service.CreateDescriptorDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value.Should().NotBeNull();
        result.Value!.DescriptorKind.Should().Be(DescriptorKind.Event);
        result.Value.DescriptorId.Should().Be("test.desc-001");
        result.Value.Operation.Should().Be(DraftAbstractions.DescriptorDraftOperation.Create);
        result.Value.TenantId.Should().Be(TestTenantId);
        result.Value.Status.Should().Be(DraftAbstractions.DescriptorDraftStatus.Created);
        result.Value.DraftId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateDescriptorDraft_Audit_Records_TouchedDraftIds()
    {
        var service = CreateService();
        var context = CreateContext("CreateDescriptorDraft");
        var request = new CreateDescriptorDraftRequest
        {
            DescriptorKind = DescriptorKind.Event,
            DescriptorId = "test.desc-001",
            Operation = DraftAbstractions.DescriptorDraftOperation.Create,
            Payload = CreateTestPayloadDto(DescriptorKind.Event, "test.desc-001", "Test")
        };

        DraftStoreMock.Setup(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await service.CreateDescriptorDraftAsync(context, request);

	        InMemoryAuditor.GetAllRecords().Should().Contain(r =>
	            r.TouchedDraftIds != null && r.TouchedDraftIds.Count > 0);
    }

    [Fact]
    public async Task UpdateDescriptorDraft_Updates_Existing_Draft()
    {
        var service = CreateService();
        var context = CreateContext("UpdateDescriptorDraft");
        var existing = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(existing));
        DraftStoreMock.Setup(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new UpdateDescriptorDraftRequest
        {
            DraftId = "draft-001",
            Intent = "Updated intent",
            Rationale = "Fixed something"
        };

        var result = await service.UpdateDescriptorDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Intent.Should().Be("Updated intent");
        result.Value.Rationale.Should().Be("Fixed something");
    }

    [Fact]
    public async Task UpdateDescriptorDraft_Returns_NotFound_When_Draft_Missing()
    {
        var service = CreateService();
        var context = CreateContext("UpdateDescriptorDraft");

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "nonexistent", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(null));

        var request = new UpdateDescriptorDraftRequest { DraftId = "nonexistent" };

        var result = await service.UpdateDescriptorDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.NotFound);
    }

    [Fact]
    public async Task UpdateDescriptorDraft_Preserves_Unchanged_Fields()
    {
        var service = CreateService();
        var context = CreateContext("UpdateDescriptorDraft");
        var existing = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(existing));
        DraftStoreMock.Setup(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new UpdateDescriptorDraftRequest { DraftId = "draft-001", Intent = "Updated intent" };

        var result = await service.UpdateDescriptorDraftAsync(context, request);

        result.Value!.Intent.Should().Be("Updated intent");
        result.Value.Rationale.Should().Be(existing.Rationale);
    }

    [Fact]
    public async Task GetDescriptorDraft_Returns_Draft_When_Found()
    {
        var service = CreateService();
        var context = CreateContext("GetDescriptorDraft");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));

        var result = await service.GetDescriptorDraftAsync(context, "draft-001");

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.DraftId.Should().Be("draft-001");
    }

    [Fact]
    public async Task GetDescriptorDraft_Returns_NotFound_When_Missing()
    {
        var service = CreateService();
        var context = CreateContext("GetDescriptorDraft");

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "nonexistent", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(null));

        var result = await service.GetDescriptorDraftAsync(context, "nonexistent");

        result.Status.Should().Be(AgentToolResultStatus.NotFound);
    }

    [Fact]
    public async Task ListDescriptorDrafts_Returns_Drafts_For_Tenant()
    {
        var service = CreateService();
        var context = CreateContext("ListDescriptorDrafts");
        var drafts = new List<Draft> { CreateTestDraft(), CreateTestDraft(draftId: "draft-002") };

        DraftStoreMock.Setup(s => s.ListAsync(TestTenantId, null, It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<IReadOnlyList<Draft>>(drafts.AsReadOnly()));

        var result = await service.ListDescriptorDraftsAsync(context, null);

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task CancelDescriptorDraft_Sets_Status_To_Cancelled()
    {
        var service = CreateService();
        var context = CreateContext("CancelDescriptorDraft");
        var draft = CreateTestDraft();

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DraftStoreMock.Setup(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await service.CancelDescriptorDraftAsync(context, "draft-001");

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Status.Should().Be(DraftAbstractions.DescriptorDraftStatus.Cancelled);
    }

    [Fact]
    public async Task CancelDescriptorDraft_Returns_NotFound_When_Missing()
    {
        var service = CreateService();
        var context = CreateContext("CancelDescriptorDraft");

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "nonexistent", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(null));

        var result = await service.CancelDescriptorDraftAsync(context, "nonexistent");

        result.Status.Should().Be(AgentToolResultStatus.NotFound);
    }

    [Fact]
    public async Task CompareDescriptorDraft_Returns_Comparison_With_Active()
    {
        var service = CreateService();
        var context = CreateContext("CompareDescriptorDraft");
        var draft = CreateTestDraft();
        var activeDescriptor = CreateTestDescriptor(ns: "event");

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draft));
        DescriptorCatalogMock.Setup(c => c.Get(draft.DescriptorId)).Returns(activeDescriptor);

        var result = await service.CompareDescriptorDraftAsync(context, "draft-001");

        result.Status.Should().Be(AgentToolResultStatus.Success);
        result.Value!.Draft.DraftId.Should().Be(draft.DraftId);
        result.Value!.Draft.DescriptorKind.Should().Be(draft.DescriptorKind);
        result.Value.CurrentActiveDescriptor.Should().NotBeNull();
        result.Value.CurrentActiveDescriptor!.Name.Should().Be(activeDescriptor.Name);
    }

    [Fact]
    public async Task CompareDescriptorDraft_Returns_NotFound_When_Draft_Missing()
    {
        var service = CreateService();
        var context = CreateContext("CompareDescriptorDraft");

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "nonexistent", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(null));

        var result = await service.CompareDescriptorDraftAsync(context, "nonexistent");

        result.Status.Should().Be(AgentToolResultStatus.NotFound);
    }

    [Fact]
    public async Task CreateDescriptorDraft_KindDiscriminatorMismatch_Returns_InvalidRequest()
    {
        var service = CreateService();
        var context = CreateContext("CreateDescriptorDraft");
        var request = new CreateDescriptorDraftRequest
        {
            DescriptorKind = DescriptorKind.Event,
            DescriptorId = "test.desc-001",
            Operation = DraftAbstractions.DescriptorDraftOperation.Create,
            Payload = CreateTestPayloadDto(DescriptorKind.Schema, "test.desc-001", "TestSchema"),
            ProposedVersion = "1",
            Intent = "Create with mismatched payload"
        };

        var result = await service.CreateDescriptorDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "KindDiscriminatorMismatch");
    }

    [Fact]
    public async Task UpdateDescriptorDraft_KindDiscriminatorMismatch_Returns_InvalidRequest()
    {
        var service = CreateService();
        var context = CreateContext("UpdateDescriptorDraft");
        var existing = CreateTestDraft(kind: DescriptorKind.Event);

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(existing));

        var request = new UpdateDescriptorDraftRequest
        {
            DraftId = "draft-001",
            Payload = new AgentDraftPayloadPatchDto
            {
                Discriminator = DescriptorKind.Schema,
                Schema = new AgentSchemaDraftPayloadPatchDto
                {
                    Payload = CreateTestPayloadDto(DescriptorKind.Schema, "test.desc-001", "TestSchema").Schema!,
                    ChangedFields = AgentSchemaDraftChangedField.Name,
                },
            },
        };

        var result = await service.UpdateDescriptorDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == "KindDiscriminatorMismatch");
    }

    [Fact]
    public async Task CreateDescriptorDraft_MatchingKindDiscriminator_Succeeds()
    {
        var service = CreateService();
        var context = CreateContext("CreateDescriptorDraft");
        var request = new CreateDescriptorDraftRequest
        {
            DescriptorKind = DescriptorKind.Event,
            DescriptorId = "test.desc-001",
            Operation = DraftAbstractions.DescriptorDraftOperation.Create,
            Payload = CreateTestPayloadDto(DescriptorKind.Event, "test.desc-001", "TestEvent"),
            ProposedVersion = "1",
            Intent = "Create with matching payload"
        };

        DraftStoreMock.Setup(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await service.CreateDescriptorDraftAsync(context, request);

        result.Status.Should().NotBe(AgentToolResultStatus.InvalidRequest);
    }

    [Fact]
    public async Task UpdateDescriptorDraft_KindDiscriminatorMismatch_DoesNot_SaveAsync()
    {
        var service = CreateService();
        var context = CreateContext("UpdateDescriptorDraft");
        var existing = CreateTestDraft(kind: DescriptorKind.Event);

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(existing));

        var request = new UpdateDescriptorDraftRequest
        {
            DraftId = "draft-001",
            Payload = new AgentDraftPayloadPatchDto
            {
                Discriminator = DescriptorKind.Schema,
                Schema = new AgentSchemaDraftPayloadPatchDto
                {
                    Payload = CreateTestPayloadDto(DescriptorKind.Schema, "test.desc-001", "TestSchema").Schema!,
                    ChangedFields = AgentSchemaDraftChangedField.Name,
                },
            },
        };

        _ = await service.UpdateDescriptorDraftAsync(context, request);

        DraftStoreMock.Verify(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateDescriptorDraft_MissingMatchingPayloadBranch_Returns_InvalidRequest()
    {
        var service = CreateService();
        var context = CreateContext("CreateDescriptorDraft");
        var request = new CreateDescriptorDraftRequest
        {
            DescriptorKind = DescriptorKind.Event,
            DescriptorId = "test.desc-001",
            Operation = DraftAbstractions.DescriptorDraftOperation.Create,
            Payload = new AgentDraftPayloadDto
            {
                Discriminator = DescriptorKind.Event,
                // Event is null — no sub-record populated despite Discriminator=Event
            },
            ProposedVersion = "1",
            Intent = "Create with missing payload branch"
        };

        var result = await service.CreateDescriptorDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == AgentDraftContractErrorCodes.DiscriminatorMismatch);
    }

    [Fact]
    public async Task CreateDescriptorDraft_MixedPayloadBranches_Returns_InvalidRequest()
    {
        var service = CreateService();
        var context = CreateContext("CreateDescriptorDraft");
        var request = new CreateDescriptorDraftRequest
        {
            DescriptorKind = DescriptorKind.Event,
            DescriptorId = "test.desc-001",
            Operation = DraftAbstractions.DescriptorDraftOperation.Create,
            Payload = new AgentDraftPayloadDto
            {
                Discriminator = DescriptorKind.Event,
                Event = new AgentEventDraftPayloadDto { Name = "TestEvent", State = DescriptorState.Active, Category = EventCategory.Domain, Semantic = EventSemantic.Fact, Importance = EventImportance.Operational, ChangeKind = SchemaChangeKind.Additive },
                Schema = new AgentSchemaDraftPayloadDto { Name = "TestSchema", State = DescriptorState.Active, ChangeKind = SchemaChangeKind.Additive },
                // Both Event and Schema populated — violates one-of invariant
            },
            ProposedVersion = "1",
            Intent = "Create with mixed payload branches"
        };

        var result = await service.CreateDescriptorDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == AgentDraftContractErrorCodes.DiscriminatorMismatch);
    }

    [Fact]
    public async Task UpdateDescriptorDraft_MixedPayloadBranches_Returns_InvalidRequest()
    {
        var service = CreateService();
        var context = CreateContext("UpdateDescriptorDraft");
        var existing = CreateTestDraft(kind: DescriptorKind.Workflow);

        DraftStoreMock.Setup(s => s.GetAsync(TestTenantId, "draft-001", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(existing));

        var request = new UpdateDescriptorDraftRequest
        {
            DraftId = "draft-001",
            Payload = new AgentDraftPayloadPatchDto
            {
                Discriminator = DescriptorKind.Workflow,
                Capability = new AgentCapabilityDraftPayloadPatchDto
                {
                    Payload = new AgentCapabilityDraftPayloadDto { Name = "test", CapabilityKind = CapabilityKind.Command, RiskLevel = CapabilityRiskLevel.Low, State = DescriptorState.Active },
                    ChangedFields = AgentCapabilityDraftChangedField.Name,
                },
                // Capability branch populated but Discriminator is Workflow — violates ValidatePatchDiscriminator
            },
        };

        var result = await service.UpdateDescriptorDraftAsync(context, request);

        result.Status.Should().Be(AgentToolResultStatus.InvalidRequest);
        result.Diagnostics.Should().Contain(d => d.Code == AgentDraftContractErrorCodes.DiscriminatorMismatch);
    }

    [Fact]
    public async Task InvalidPayload_DoesNot_SaveAsync()
    {
        var service = CreateService();
        var context = CreateContext("CreateDescriptorDraft");
        var request = new CreateDescriptorDraftRequest
        {
            DescriptorKind = DescriptorKind.Event,
            DescriptorId = "test.desc-001",
            Operation = DraftAbstractions.DescriptorDraftOperation.Create,
            Payload = new AgentDraftPayloadDto
            {
                Discriminator = DescriptorKind.Event,
                // Event is null — missing matching branch
            },
            ProposedVersion = "1",
            Intent = "Create with invalid one-of"
        };

        _ = await service.CreateDescriptorDraftAsync(context, request);

        // SaveAsync must NOT be called when validation fails
        DraftStoreMock.Verify(s => s.SaveAsync(It.IsAny<Draft>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Drafts_Are_Tenant_Isolated()
    {
        var service = CreateService();
        var contextA = CreateContext("GetDescriptorDraft", tenantId: "tenant-A");
        var contextB = CreateContext("GetDescriptorDraft", tenantId: "tenant-B");
        var draftA = CreateTestDraft(draftId: "draft-A", tenantId: "tenant-A");

        DraftStoreMock.Setup(s => s.GetAsync("tenant-A", "draft-A", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(draftA));
        DraftStoreMock.Setup(s => s.GetAsync("tenant-B", "draft-A", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Draft?>(null));

        var resultA = await service.GetDescriptorDraftAsync(contextA, "draft-A");
        var resultB = await service.GetDescriptorDraftAsync(contextB, "draft-A");

        resultA.Status.Should().Be(AgentToolResultStatus.Success);
        resultB.Status.Should().Be(AgentToolResultStatus.NotFound);
    }

    // ── Discriminator-aware TryValidatePayload tests ──

    [Fact]
    public void Create_DiscriminatorWorkflow_WithCapabilityBranch_Returns_DiscriminatorMismatch()
    {
        var dto = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.Workflow,
            Capability = new AgentCapabilityDraftPayloadDto
            {
                Name = "test",
                CapabilityKind = CapabilityKind.Command,
                RiskLevel = CapabilityRiskLevel.Low,
                State = DescriptorState.Active
            }
        };
        var (isValid, error) = AgentDraftPayloadProjection.TryValidatePayload(dto);
        isValid.Should().BeFalse();
        error!.Code.Should().Be(AgentDraftContractErrorCodes.DiscriminatorMismatch);
    }

    [Fact]
    public void Create_DiscriminatorSet_ButMatchingBranchMissing_Returns_DiscriminatorMismatch()
    {
        var dto = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.Workflow,
            // Workflow is null, no branches populated
        };
        var (isValid, error) = AgentDraftPayloadProjection.TryValidatePayload(dto);
        isValid.Should().BeFalse();
        error!.Code.Should().Be(AgentDraftContractErrorCodes.DiscriminatorMismatch);
    }

    [Fact]
    public void Merge_DiscriminatorWorkflow_WithCapabilityPatchBranch_Returns_DiscriminatorMismatch()
    {
        var patch = new global::CrestCreates.Agent.DraftContracts.Dto.AgentDraftPayloadPatchDto
        {
            Discriminator = DescriptorKind.Workflow,
            Capability = new global::CrestCreates.Agent.DraftContracts.Dto.AgentCapabilityDraftPayloadPatchDto
            {
                Payload = new AgentCapabilityDraftPayloadDto
                {
                    Name = "test",
                    CapabilityKind = CapabilityKind.Command,
                    RiskLevel = CapabilityRiskLevel.Low,
                    State = DescriptorState.Active
                },
                ChangedFields = global::CrestCreates.Agent.DraftContracts.Dto.AgentCapabilityDraftChangedField.Name
            }
        };
        var existing = new DraftAbstractions.WorkflowDescriptorDraftPayload(new WorkflowDescriptor { Name = "existing" });
        var result = AgentDraftPayloadProjection.Merge(patch, existing);
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == AgentDraftContractErrorCodes.DiscriminatorMismatch);
    }

    [Fact]
    public void Merge_DiscriminatorSet_ButMatchingPatchBranchMissing_Returns_DiscriminatorMismatch()
    {
        var patch = new global::CrestCreates.Agent.DraftContracts.Dto.AgentDraftPayloadPatchDto
        {
            Discriminator = DescriptorKind.Workflow,
            // Workflow patch is null
        };
        var existing = new DraftAbstractions.WorkflowDescriptorDraftPayload(new WorkflowDescriptor { Name = "existing" });
        var result = AgentDraftPayloadProjection.Merge(patch, existing);
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == AgentDraftContractErrorCodes.DiscriminatorMismatch);
    }
}
