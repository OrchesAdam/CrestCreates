using CrestCreates.Agent.DraftContracts;
using CrestCreates.Agent.DraftContracts.Dto;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.DraftContracts.Tests;

/// <summary>
/// Tests that the generated manifest covers all expected kinds and contract types.
/// </summary>
public class ManifestCoverageTests
{
    [Fact]
    public void Manifest_Contains_All_Six_Kinds()
    {
        var kinds = GeneratedAgentDraftPayloadContractManifest.SupportedKinds;

        kinds.Should().HaveCount(6);
        kinds.Should().Contain(DescriptorKind.Capability);
        kinds.Should().Contain(DescriptorKind.Event);
        kinds.Should().Contain(DescriptorKind.Form);
        kinds.Should().Contain(DescriptorKind.HumanTask);
        kinds.Should().Contain(DescriptorKind.Schema);
        kinds.Should().Contain(DescriptorKind.Workflow);
    }

    [Fact]
    public void ContractTypes_Contains_All_DtoTypes()
    {
        var contractTypes = GeneratedAgentDraftPayloadContractManifest.ContractTypes;

        // All x6 DTO types must be present
        contractTypes.Should().Contain(typeof(AgentCapabilityDraftPayloadDto));
        contractTypes.Should().Contain(typeof(AgentCapabilityDraftPayloadPatchDto));
        contractTypes.Should().Contain(typeof(AgentCapabilityDraftChangedField));
        contractTypes.Should().Contain(typeof(AgentEventDraftPayloadDto));
        contractTypes.Should().Contain(typeof(AgentEventDraftPayloadPatchDto));
        contractTypes.Should().Contain(typeof(AgentEventDraftChangedField));
        contractTypes.Should().Contain(typeof(AgentFormDraftPayloadDto));
        contractTypes.Should().Contain(typeof(AgentFormDraftPayloadPatchDto));
        contractTypes.Should().Contain(typeof(AgentFormDraftChangedField));
        contractTypes.Should().Contain(typeof(AgentHumanTaskDraftPayloadDto));
        contractTypes.Should().Contain(typeof(AgentHumanTaskDraftPayloadPatchDto));
        contractTypes.Should().Contain(typeof(AgentHumanTaskDraftChangedField));
        contractTypes.Should().Contain(typeof(AgentSchemaDraftPayloadDto));
        contractTypes.Should().Contain(typeof(AgentSchemaDraftPayloadPatchDto));
        contractTypes.Should().Contain(typeof(AgentSchemaDraftChangedField));
        contractTypes.Should().Contain(typeof(AgentWorkflowDraftPayloadDto));
        contractTypes.Should().Contain(typeof(AgentWorkflowDraftPayloadPatchDto));
        contractTypes.Should().Contain(typeof(AgentWorkflowDraftChangedField));

        // 6 kinds × 3 types (Dto, PatchDto, ChangedField) = 18 types
        contractTypes.Should().HaveCount(18);
    }

    [Fact]
    public void EditableScalarFieldTypes_Does_Not_Contain_ForbiddenTypes()
    {
        var scalarTypes = GeneratedAgentDraftPayloadContractManifest.EditableScalarFieldTypes;

        // Must not contain IDescriptor, IServiceProvider, or similar boundary types
        scalarTypes.Should().NotContain(typeof(IDescriptor));
        scalarTypes.Should().NotContain(typeof(object));
        scalarTypes.Should().NotContain(typeof(System.Dynamic.ExpandoObject));

        // Expected scalar types should be present
        scalarTypes.Should().Contain(typeof(string));
        scalarTypes.Should().Contain(typeof(int));
        scalarTypes.Should().Contain(typeof(bool));
        scalarTypes.Should().Contain(typeof(DescriptorState));
        scalarTypes.Should().Contain(typeof(CapabilityKind));
        scalarTypes.Should().Contain(typeof(CapabilityRiskLevel));
        scalarTypes.Should().Contain(typeof(Event.Abstractions.EventCategory));
        scalarTypes.Should().Contain(typeof(Event.Abstractions.EventImportance));
        scalarTypes.Should().Contain(typeof(Event.Abstractions.EventSemantic));
        scalarTypes.Should().Contain(typeof(HumanTask.Abstractions.AssigneeStrategy));
        scalarTypes.Should().Contain(typeof(Schema.Abstractions.SchemaChangeKind));
    }
}
