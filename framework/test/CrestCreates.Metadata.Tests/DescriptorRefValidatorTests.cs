using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorRefValidatorTests
{
    [Fact]
    public void Validate_ValidCapability_Passes()
    {
        var registry = new GlobalDescriptorRegistry();
        registry.Register(new SchemaDescriptor
            { Id = "schema_01", Name = "Test", Version = 1, State = DescriptorState.Active });

        var cap = new CapabilityDescriptor
        {
            Id = "cap_01", Name = "test.cap", Version = 1, State = DescriptorState.Active,
            InputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            OutputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1)
        };

        var report = DescriptorRefValidator.Validate(cap, registry);
        report.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_UnresolvedRef_ReportsError()
    {
        var registry = new GlobalDescriptorRegistry();

        var cap = new CapabilityDescriptor
        {
            Id = "cap_01", Name = "test.cap", Version = 1, State = DescriptorState.Active,
            InputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_missing", 1),
            OutputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1)
        };

        var report = DescriptorRefValidator.Validate(cap, registry);
        report.IsValid.Should().BeFalse();
        report.Errors.Should().Contain(e => e.Contains("schema_missing"));
    }

    [Fact]
    public void Validate_Workflow_ChecksStepTargets()
    {
        var registry = new GlobalDescriptorRegistry();
        registry.Register(new SchemaDescriptor
            { Id = "wf_01", Name = "test", Version = 1, State = DescriptorState.Active });

        var wf = new WorkflowDescriptor
        {
            Id = "wf_01", Name = "test.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    Id = "step_01", Name = "Step",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_missing", 1)
                    }
                }
            }
        };

        var report = DescriptorRefValidator.Validate(wf, registry);
        report.Errors.Should().Contain(e => e.Contains("cap_missing"));
    }

    [Fact]
    public void Validate_ValidWorkflow_AllResolved()
    {
        var registry = new GlobalDescriptorRegistry();
        registry.Register(new CapabilityDescriptor
            { Id = "cap_01", Name = "test", Version = 1, State = DescriptorState.Active });

        var wf = new WorkflowDescriptor
        {
            Id = "wf_01", Name = "test.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    Id = "step_01", Name = "Step",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1)
                    }
                }
            }
        };

        var report = DescriptorRefValidator.Validate(wf, registry);
        report.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_HumanTask_ChecksOutcomeRefs()
    {
        var registry = new GlobalDescriptorRegistry();

        var ht = new HumanTask.Abstractions.HumanTaskDescriptor
        {
            Id = "ht_01", Name = "task", Version = 1, State = DescriptorState.Active,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("form_01", 1),
            Outcomes = new List<HumanTask.Abstractions.CompletionOutcome>
            {
                new()
                {
                    Condition = HumanTask.Abstractions.CompletionCondition.Approve,
                    Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_missing", 1)
                }
            }
        };

        var report = DescriptorRefValidator.Validate(ht, registry);
        report.Errors.Should().Contain(e => e.Contains("cap_missing"));
    }
}