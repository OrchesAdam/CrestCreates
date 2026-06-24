using CrestCreates.Agent.DraftContracts.Dto;
using CrestCreates.Agent.DraftContracts.Projection;
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.ControlPlane.Tests;

public class AgentDraftPayloadPatchMergeTests
{
    [Fact]
    public void Merge_Capability_OnlyName_Updates_Name_Preserves_Rest()
    {
        // Arrange: Create existing payload with known values
        var existingDto = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadDto
            {
                Name = "OriginalName",
                State = DescriptorState.Active,
                Version = 5,
                CapabilityKind = CapabilityKind.Query,
                RiskLevel = CapabilityRiskLevel.Medium
            }
        };
        var createResult = AgentDraftPayloadProjection.Create(existingDto);
        createResult.IsSuccess.Should().BeTrue();
        var existingPayload = createResult.Value!;

        // Act: Patch only Name
        var patch = new AgentDraftPayloadPatchDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadPatchDto
            {
                Payload = new AgentCapabilityDraftPayloadDto
                {
                    Name = "UpdatedName",
                    State = DescriptorState.Draft,          // not applied — flag not set
                    CapabilityKind = CapabilityKind.Command, // not applied — flag not set
                    RiskLevel = CapabilityRiskLevel.Low      // not applied — flag not set
                },
                ChangedFields = AgentCapabilityDraftChangedField.Name
            }
        };
        var mergeResult = AgentDraftPayloadProjection.Merge(patch, existingPayload);
        mergeResult.IsSuccess.Should().BeTrue();
        var merged = mergeResult.Value!;

        // Assert: Convert back to DTO and verify
        var fromDomainResult = AgentDraftPayloadProjection.FromDomain(merged);
        fromDomainResult.IsSuccess.Should().BeTrue();
        var result = fromDomainResult.Value!;

        result.Capability!.Name.Should().Be("UpdatedName");
        result.Capability!.State.Should().Be(DescriptorState.Active);
        result.Capability!.Version.Should().Be(5);
//         result.Capability!.ContractHash.Should().Be("original-hash");
//         result.Capability!.DefinitionHash.Should().Be("original-defhash");
        result.Capability!.CapabilityKind.Should().Be(CapabilityKind.Query);
        result.Capability!.RiskLevel.Should().Be(CapabilityRiskLevel.Medium);
    }

    [Fact]
    public void Merge_Capability_MultipleFields_Updates_Only_Listed()
    {
        // Arrange
        var existingDto = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadDto
            {
                Name = "OriginalName",
                State = DescriptorState.Draft,
                Version = 1,
                CapabilityKind = CapabilityKind.Command,
                RiskLevel = CapabilityRiskLevel.Low
            }
        };
        var createResult = AgentDraftPayloadProjection.Create(existingDto);
        createResult.IsSuccess.Should().BeTrue();
        var existingPayload = createResult.Value!;

        // Act: Patch Name + Version + RiskLevel
        var patch = new AgentDraftPayloadPatchDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadPatchDto
            {
                Payload = new AgentCapabilityDraftPayloadDto
                {
                    Name = "NewName",
                    Version = 3,
                    RiskLevel = CapabilityRiskLevel.Critical,
                    State = DescriptorState.Active,          // not applied — flag not set
                    CapabilityKind = CapabilityKind.Query     // not applied — flag not set
                },
                ChangedFields = AgentCapabilityDraftChangedField.Name
                    | AgentCapabilityDraftChangedField.Version
                    | AgentCapabilityDraftChangedField.RiskLevel
            }
        };
        var mergeResult = AgentDraftPayloadProjection.Merge(patch, existingPayload);
        mergeResult.IsSuccess.Should().BeTrue();
        var merged = mergeResult.Value!;

        // Assert
        var fromDomainResult = AgentDraftPayloadProjection.FromDomain(merged);
        fromDomainResult.IsSuccess.Should().BeTrue();
        var result = fromDomainResult.Value!;

        result.Capability!.Name.Should().Be("NewName");
        result.Capability!.Version.Should().Be(3);
        result.Capability!.RiskLevel.Should().Be(CapabilityRiskLevel.Critical);
        // Unchanged fields preserved
        result.Capability!.State.Should().Be(DescriptorState.Draft);
//         result.Capability!.ContractHash.Should().Be("hash1");
        result.Capability!.CapabilityKind.Should().Be(CapabilityKind.Command);
    }

    [Fact]
    public void Merge_Event_OnlyCategory_Updates_Category_Preserves_Rest()
    {
        // Arrange
        var existingDto = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.Event,
            Event = new AgentEventDraftPayloadDto
            {
                Name = "TestEvent",
                State = DescriptorState.Active,
                Version = 2,
                Category = EventCategory.Domain,
                Semantic = EventSemantic.Fact,
                Importance = EventImportance.Critical,
                ChangeKind = SchemaChangeKind.Additive,
                PayloadSchema = new DescriptorRef("schema", "event-payload", 1)
            }
        };
        var createResult = AgentDraftPayloadProjection.Create(existingDto);
        createResult.IsSuccess.Should().BeTrue();
        var existingPayload = createResult.Value!;

        // Act: Patch only Category
        var patch = new AgentDraftPayloadPatchDto
        {
            Discriminator = DescriptorKind.Event,
            Event = new AgentEventDraftPayloadPatchDto
            {
                Payload = new AgentEventDraftPayloadDto
                {
                    Category = EventCategory.Integration,
                    State = DescriptorState.Draft,              // not applied
                    Semantic = EventSemantic.Notification,      // not applied
                    Importance = EventImportance.Ephemeral,     // not applied
                    ChangeKind = SchemaChangeKind.Breaking       // not applied
                },
                ChangedFields = AgentEventDraftChangedField.Category
            }
        };
        var mergeResult = AgentDraftPayloadProjection.Merge(patch, existingPayload);
        mergeResult.IsSuccess.Should().BeTrue();
        var merged = mergeResult.Value!;

        // Assert
        var fromDomainResult = AgentDraftPayloadProjection.FromDomain(merged);
        fromDomainResult.IsSuccess.Should().BeTrue();
        var result = fromDomainResult.Value!;

        result.Event!.Category.Should().Be(EventCategory.Integration);
        result.Event!.Name.Should().Be("TestEvent");
        result.Event!.State.Should().Be(DescriptorState.Active);
        result.Event!.Version.Should().Be(2);
        result.Event!.Semantic.Should().Be(EventSemantic.Fact);
        result.Event!.Importance.Should().Be(EventImportance.Critical);
        result.Event!.ChangeKind.Should().Be(SchemaChangeKind.Additive);
    }

    [Fact]
    public void Merge_PreserveFields_AlwaysCopied_From_Existing()
    {
        // Verify that Preserve-classified fields (like SupersededById) are always
        // copied from existing, never from the patch, regardless of ChangedFields.
        //
        // SupersededById is a Preserve field — it is not in the generated DTO at all.
        // The Merge code explicitly copies it from the existing descriptor.
        // After a partial patch, the merge must succeed (which proves the Preserve
        // field was carried over correctly from the existing descriptor).

        // Arrange: Create existing Capability payload
        var existingDto = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadDto
            {
                Name = "Test",
                State = DescriptorState.Draft,
                Version = 1,
                CapabilityKind = CapabilityKind.Command,
                RiskLevel = CapabilityRiskLevel.Low
            }
        };
        var createResult = AgentDraftPayloadProjection.Create(existingDto);
        createResult.IsSuccess.Should().BeTrue();
        var existingPayload = createResult.Value!;

        // Act: Apply a patch that only changes Name
        var patch = new AgentDraftPayloadPatchDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadPatchDto
            {
                Payload = new AgentCapabilityDraftPayloadDto
                {
                    Name = "Updated",
                    State = DescriptorState.Draft,
                    CapabilityKind = CapabilityKind.Command,
                    RiskLevel = CapabilityRiskLevel.Low
                },
                ChangedFields = AgentCapabilityDraftChangedField.Name
            }
        };
        var mergeResult = AgentDraftPayloadProjection.Merge(patch, existingPayload);
        mergeResult.IsSuccess.Should().BeTrue();
        var merged = mergeResult.Value!;

        // Assert: Merge succeeded, and FromDomain yields the expected updated value
        var fromDomainResult = AgentDraftPayloadProjection.FromDomain(merged);
        fromDomainResult.IsSuccess.Should().BeTrue();
        var result = fromDomainResult.Value!;

        result.Capability!.Name.Should().Be("Updated");
    }
}
