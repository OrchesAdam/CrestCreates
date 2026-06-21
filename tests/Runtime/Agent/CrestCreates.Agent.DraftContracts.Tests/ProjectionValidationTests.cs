using CrestCreates.Agent.DraftContracts.Dto;
using CrestCreates.Agent.DraftContracts.Projection;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Agent.DraftContracts.Tests;

/// <summary>
/// Tests for TryValidatePayload validation logic.
/// </summary>
public class ProjectionValidationTests
{
    [Fact]
    public void TryValidatePayload_NullDto_Returns_Invalid()
    {
        var (isValid, error) = AgentDraftPayloadProjection.TryValidatePayload(null!);

        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.Code.Should().Be(AgentDraftContractErrorCodes.NullPayload);
    }

    [Fact]
    public void TryValidatePayload_NoBranchPopulated_Returns_Invalid()
    {
        var dto = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = null,
            Event = null,
            Form = null,
            HumanTask = null,
            Schema = null,
            Workflow = null,
        };

        var (isValid, error) = AgentDraftPayloadProjection.TryValidatePayload(dto);

        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.Code.Should().Be(AgentDraftContractErrorCodes.DiscriminatorMismatch);
        error.Message.Should().Contain("no matching payload branch");
    }

    [Fact]
    public void TryValidatePayload_MultipleBranchesPopulated_Returns_Invalid()
    {
        var dto = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadDto
            {
                CapabilityKind = CapabilityKind.Command,
                RiskLevel = CapabilityRiskLevel.Medium,
                State = DescriptorState.Active,
            },
            Workflow = new AgentWorkflowDraftPayloadDto
            {
                State = DescriptorState.Active,
            },
        };

        var (isValid, error) = AgentDraftPayloadProjection.TryValidatePayload(dto);

        isValid.Should().BeFalse();
        error.Should().NotBeNull();
        error!.Code.Should().Be(AgentDraftContractErrorCodes.DiscriminatorMismatch);
        error.Message.Should().Contain("Multiple payload branches");
    }

    [Fact]
    public void TryValidatePayload_SingleBranchPopulated_Returns_Valid()
    {
        var dto = new AgentDraftPayloadDto
        {
            Discriminator = DescriptorKind.Capability,
            Capability = new AgentCapabilityDraftPayloadDto
            {
                CapabilityKind = CapabilityKind.Command,
                RiskLevel = CapabilityRiskLevel.Medium,
                State = DescriptorState.Active,
            },
        };

        var (isValid, error) = AgentDraftPayloadProjection.TryValidatePayload(dto);

        isValid.Should().BeTrue();
        error.Should().BeNull();
    }
}
