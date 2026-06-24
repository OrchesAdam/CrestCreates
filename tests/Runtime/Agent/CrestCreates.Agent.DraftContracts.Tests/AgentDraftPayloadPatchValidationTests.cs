using CrestCreates.Agent.DraftContracts.Dto;
using CrestCreates.Agent.DraftContracts.Projection;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.DraftContracts.Tests;

/// <summary>
/// Validation tests for the Merge projection method, covering:
/// - P0-4: Unknown ChangedFields bits detection (ADPC005)
/// - P0-5: Null handling for nullable vs non-nullable fields (ADPC007, clear-to-null)
/// </summary>
public class AgentDraftPayloadPatchValidationTests
{
    // ═══════════════════════════════════════════════════════════
    //  P0-4: Unknown ChangedFields bits
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Merge_UnknownChangedFieldBits_Returns_ADPC005()
    {
        var existing = CreateExistingCapability();

        var patchDto = new AgentCapabilityDraftPayloadDto
        {
            CapabilityKind = CapabilityKind.Command,
            RiskLevel = CapabilityRiskLevel.Low,
            State = DescriptorState.Active,
            Name = "Test",
        };

        var patch = new AgentDraftPayloadPatchDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadPatchDto
            {
                Payload = patchDto,
                // A value not defined in AgentCapabilityDraftChangedField
                ChangedFields = (AgentCapabilityDraftChangedField)2048,
            },
        };

        var result = AgentDraftPayloadProjection.Merge(patch, existing);
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == AgentDraftContractErrorCodes.UnknownChangedField);
    }

    [Fact]
    public void Merge_UnknownChangedFieldBits_EventKind_Returns_ADPC005()
    {
        var existing = CreateExistingEvent();

        var patchDto = new AgentEventDraftPayloadDto
        {
            Category = EventCategory.Domain,
            ChangeKind = SchemaChangeKind.Additive,
            Importance = EventImportance.Critical,
            Semantic = EventSemantic.Fact,
            State = DescriptorState.Active,
            Name = "Test",
        };

        var patch = new AgentDraftPayloadPatchDto
        {
            Discriminator = DescriptorKind.Event,
            Event = new AgentEventDraftPayloadPatchDto
            {
                Payload = patchDto,
                ChangedFields = (AgentEventDraftChangedField)1024,
            },
        };

        var result = AgentDraftPayloadProjection.Merge(patch, existing);
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == AgentDraftContractErrorCodes.UnknownChangedField);
    }

    // ═══════════════════════════════════════════════════════════
    //  P0-5: Null handling — non-nullable fields return ADPC007
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Merge_NullForNonNullableString_Returns_ADPC007()
    {
        var existing = CreateExistingCapability();

        var patchDto = new AgentCapabilityDraftPayloadDto
        {
            CapabilityKind = CapabilityKind.Command,
            RiskLevel = CapabilityRiskLevel.Low,
            State = DescriptorState.Active,
            // Name is non-nullable string — null → ADPC007
            Name = null!,
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
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == AgentDraftContractErrorCodes.NonNullableFieldNull);
        result.Errors.Should().Contain(e => e.Message!.Contains("Name"));
    }

    [Fact]
    public void Merge_NullForNonNullableInt_Returns_ADPC007()
    {
        var existing = CreateExistingCapability();

        var patchDto = new AgentCapabilityDraftPayloadDto
        {
            CapabilityKind = CapabilityKind.Command,
            RiskLevel = CapabilityRiskLevel.Low,
            State = DescriptorState.Active,
            Name = "Test",
            // Version is non-nullable int — null → ADPC007
            Version = null,
        };

        var patch = new AgentDraftPayloadPatchDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadPatchDto
            {
                Payload = patchDto,
                ChangedFields = AgentCapabilityDraftChangedField.Version,
            },
        };

        var result = AgentDraftPayloadProjection.Merge(patch, existing);
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == AgentDraftContractErrorCodes.NonNullableFieldNull);
        result.Errors.Should().Contain(e => e.Message!.Contains("Version"));
    }

    [Fact]
    public void Merge_NullForNonNullableRef_Returns_ADPC007()
    {
        var existing = CreateExistingEvent();

        var patchDto = new AgentEventDraftPayloadDto
        {
            Category = EventCategory.Domain,
            ChangeKind = SchemaChangeKind.Additive,
            Importance = EventImportance.Critical,
            Semantic = EventSemantic.Fact,
            State = DescriptorState.Active,
            Name = "Test",
            // PayloadSchema is non-nullable VersionedDescriptorRef → null → ADPC007
            PayloadSchema = null,
        };

        var patch = new AgentDraftPayloadPatchDto
        {
            Discriminator = DescriptorKind.Event,
            Event = new AgentEventDraftPayloadPatchDto
            {
                Payload = patchDto,
                ChangedFields = AgentEventDraftChangedField.PayloadSchema,
            },
        };

        var result = AgentDraftPayloadProjection.Merge(patch, existing);
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == AgentDraftContractErrorCodes.NonNullableFieldNull);
        result.Errors.Should().Contain(e => e.Message!.Contains("PayloadSchema"));
    }

    // ═══════════════════════════════════════════════════════════
    //  P0-5: Null handling — nullable fields clear to null
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Merge_NullForNullableRef_ClearsToNull()
    {
        var existing = CreateExistingCapability();

        var patchDto = new AgentCapabilityDraftPayloadDto
        {
            CapabilityKind = CapabilityKind.Command,
            RiskLevel = CapabilityRiskLevel.Low,
            State = DescriptorState.Active,
            Name = "Test",
            // InputSchema is nullable VersionedDescriptorRef — null → clear to null
            InputSchema = null,
        };

        var patch = new AgentDraftPayloadPatchDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadPatchDto
            {
                Payload = patchDto,
                ChangedFields = AgentCapabilityDraftChangedField.InputSchema,
            },
        };

        var result = AgentDraftPayloadProjection.Merge(patch, existing);
        result.IsSuccess.Should().BeTrue();

        var merged = (CapabilityDescriptorDraftPayload)result.Value!;
        merged.Descriptor.InputSchema.Should().BeNull();
    }

    [Fact]
    public void Merge_NullForNullableRef_OutputSchema_ClearsToNull()
    {
        var existing = CreateExistingCapability();

        var patchDto = new AgentCapabilityDraftPayloadDto
        {
            CapabilityKind = CapabilityKind.Command,
            RiskLevel = CapabilityRiskLevel.Low,
            State = DescriptorState.Active,
            Name = "Test",
            OutputSchema = null,
        };

        var patch = new AgentDraftPayloadPatchDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadPatchDto
            {
                Payload = patchDto,
                ChangedFields = AgentCapabilityDraftChangedField.OutputSchema,
            },
        };

        var result = AgentDraftPayloadProjection.Merge(patch, existing);
        result.IsSuccess.Should().BeTrue();

        var merged = (CapabilityDescriptorDraftPayload)result.Value!;
        merged.Descriptor.OutputSchema.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════

    private static CapabilityDescriptorDraftPayload CreateExistingCapability()
    {
        var descriptor = new CapabilityDescriptor
        {
            Name = "ExistingName",
            State = DescriptorState.Active,
            Version = 1,
            CapabilityKind = CapabilityKind.Query,
            RiskLevel = CapabilityRiskLevel.Low,
            SupersededById = "old-supersede-ref",
            InputSchema = new VersionedDescriptorRef<SchemaDescriptor>("existing-is", 1),
            OutputSchema = new VersionedDescriptorRef<SchemaDescriptor>("existing-os", 2),
        };
        return new CapabilityDescriptorDraftPayload(descriptor);
    }

    private static EventDescriptorDraftPayload CreateExistingEvent()
    {
        var descriptor = new EventDescriptor
        {
            Name = "ExistingEvent",
            State = DescriptorState.Active,
            Version = 1,
            Category = EventCategory.Domain,
            Semantic = EventSemantic.Fact,
            Importance = EventImportance.Critical,
            ChangeKind = SchemaChangeKind.Additive,
            PayloadSchema = new VersionedDescriptorRef<SchemaDescriptor>("event-ps", 1),
        };
        return new EventDescriptorDraftPayload(descriptor);
    }
}
