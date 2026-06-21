using CrestCreates.Agent.DraftContracts.Dto;
using CrestCreates.Agent.DraftContracts.Projection;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.DraftContracts.Tests;

/// <summary>
/// Tests for the Merge projection method.
/// </summary>
public class MergeProjectionTests
{
    private static CapabilityDescriptorDraftPayload CreateExistingCapability()
    {
        var descriptor = new CapabilityDescriptor
        {
            Name = "ExistingName",
            State = DescriptorState.Active,
            ContractHash = "existing-ch",
            DefinitionHash = "existing-dh",
            Version = 1,
            CapabilityKind = CapabilityKind.Query,
            RiskLevel = CapabilityRiskLevel.Low,
            SupersededById = "old-supersede-ref",
            InputSchema = new VersionedDescriptorRef<SchemaDescriptor>("existing-is", 1),
        };
        return new CapabilityDescriptorDraftPayload(descriptor);
    }

    /// <summary>
    /// When a field is marked as Changed and has a new value,
    /// the merge should overwrite the existing value.
    /// </summary>
    [Fact]
    public void Merge_ChangedField_Overwrites_Existing()
    {
        var existing = CreateExistingCapability();

        var patchDto = new AgentCapabilityDraftPayloadDto
        {
            CapabilityKind = CapabilityKind.Command,
            RiskLevel = CapabilityRiskLevel.High,
            State = DescriptorState.Active,
            Name = "NewName",
        };

        var patch = new AgentDraftPayloadPatchDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadPatchDto
            {
                Payload = patchDto,
                ChangedFields = AgentCapabilityDraftChangedField.Name,
            },
        };

        var result = AgentDraftPayloadProjection.Merge(patch, existing);
        result.IsSuccess.Should().BeTrue();

        var merged = (CapabilityDescriptorDraftPayload)result.Value!;
        merged.Descriptor.Name.Should().Be("NewName");                // overwritten
        merged.Descriptor.ContractHash.Should().Be("existing-ch");    // preserved
    }

    /// <summary>
    /// When a field is NOT marked as Changed, the merge preserves the existing value.
    /// </summary>
    [Fact]
    public void Merge_UnchangedField_Preserves_Existing()
    {
        var existing = CreateExistingCapability();

        var patchDto = new AgentCapabilityDraftPayloadDto
        {
            CapabilityKind = CapabilityKind.Query,
            RiskLevel = CapabilityRiskLevel.Low,
            State = DescriptorState.Active,
            Name = "DifferentName",       // different but NOT in ChangedFields
        };

        var patch = new AgentDraftPayloadPatchDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadPatchDto
            {
                Payload = patchDto,
                ChangedFields = AgentCapabilityDraftChangedField.ContractHash,  // only ContractHash
            },
        };

        var result = AgentDraftPayloadProjection.Merge(patch, existing);
        result.IsSuccess.Should().BeTrue();

        var merged = (CapabilityDescriptorDraftPayload)result.Value!;
        merged.Descriptor.Name.Should().Be("ExistingName");       // preserved (not changed)
        merged.Descriptor.ContractHash.Should().Be(string.Empty);  // changed, patch has default null → ""
    }

    /// <summary>
    /// PreserveFields (like SupersededById) are always copied from existing,
    /// even if ChangedFields is set for other fields.
    /// </summary>
    [Fact]
    public void Merge_PreserveFields_AlwaysCopiedFromExisting()
    {
        var existing = CreateExistingCapability();

        var patchDto = new AgentCapabilityDraftPayloadDto
        {
            CapabilityKind = CapabilityKind.Command,
            RiskLevel = CapabilityRiskLevel.High,
            State = DescriptorState.Active,
        };

        var patch = new AgentDraftPayloadPatchDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadPatchDto
            {
                Payload = patchDto,
                ChangedFields = AgentCapabilityDraftChangedField.Name | AgentCapabilityDraftChangedField.Version,
            },
        };

        var result = AgentDraftPayloadProjection.Merge(patch, existing);
        result.IsSuccess.Should().BeTrue();

        var merged = (CapabilityDescriptorDraftPayload)result.Value!;
        merged.Descriptor.SupersededById.Should().Be("old-supersede-ref");  // always preserved
    }

    /// <summary>
    /// When ALL editable fields are changed, the merge result should be equivalent
    /// to a Create from the same DTO.
    /// </summary>
    [Fact]
    public void Merge_AllFieldsChanged_EquivalentToCreate()
    {
        var existing = CreateExistingCapability();

        var fullDto = new AgentCapabilityDraftPayloadDto
        {
            CapabilityKind = CapabilityKind.Command,
            RiskLevel = CapabilityRiskLevel.Critical,
            State = DescriptorState.Deprecated,
            Name = "FullNewName",
            ContractHash = "full-ch",
            DefinitionHash = "full-dh",
            Version = 42,
            InputSchema = new DescriptorRef("schema", "merge-is", 9),
            OutputSchema = new DescriptorRef("schema", "merge-os", 10),
            Consumes = new[] { new DescriptorRef("event", "merge-consume", 1) },
            Produces = new[] { new DescriptorRef("event", "merge-produce", 2) },
        };

        var patch = new AgentDraftPayloadPatchDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadPatchDto
            {
                Payload = fullDto,
                ChangedFields =
                    AgentCapabilityDraftChangedField.CapabilityKind |
                    AgentCapabilityDraftChangedField.Consumes |
                    AgentCapabilityDraftChangedField.ContractHash |
                    AgentCapabilityDraftChangedField.DefinitionHash |
                    AgentCapabilityDraftChangedField.InputSchema |
                    AgentCapabilityDraftChangedField.Name |
                    AgentCapabilityDraftChangedField.OutputSchema |
                    AgentCapabilityDraftChangedField.Produces |
                    AgentCapabilityDraftChangedField.RiskLevel |
                    AgentCapabilityDraftChangedField.State |
                    AgentCapabilityDraftChangedField.Version,
            },
        };

        var mergeResult = AgentDraftPayloadProjection.Merge(patch, existing);
        mergeResult.IsSuccess.Should().BeTrue();

        // Create from the same full DTO
        var createPayload = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = fullDto,
        };
        var createResult = AgentDraftPayloadProjection.Create(createPayload);
        createResult.IsSuccess.Should().BeTrue();

        var merged = (CapabilityDescriptorDraftPayload)mergeResult.Value!;
        var created = (CapabilityDescriptorDraftPayload)createResult.Value!;

        // All editable fields should match
        var m = merged.Descriptor;
        var c = created.Descriptor;
        m.Name.Should().Be(c.Name);
        m.State.Should().Be(c.State);
        m.ContractHash.Should().Be(c.ContractHash);
        m.DefinitionHash.Should().Be(c.DefinitionHash);
        m.Version.Should().Be(c.Version);
        m.CapabilityKind.Should().Be(c.CapabilityKind);
        m.RiskLevel.Should().Be(c.RiskLevel);
        m.InputSchema.Should().Be(c.InputSchema);
        m.OutputSchema.Should().Be(c.OutputSchema);

        m.Consumes.Should().HaveCount(c.Consumes.Count);
        for (int i = 0; i < m.Consumes.Count; i++)
            m.Consumes[i].Should().Be(c.Consumes[i]);

        m.Produces.Should().HaveCount(c.Produces.Count);
        for (int i = 0; i < m.Produces.Count; i++)
            m.Produces[i].Should().Be(c.Produces[i]);

        // Preserve fields differ
        m.SupersededById.Should().Be("old-supersede-ref");
    }
}
