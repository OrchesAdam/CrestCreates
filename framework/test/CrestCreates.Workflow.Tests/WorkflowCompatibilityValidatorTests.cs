using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class WorkflowCompatibilityValidatorTests
{
    private static WorkflowDescriptor CreateDescriptorWithStep(InteractionTarget target,
        StepErrorBehavior onError = StepErrorBehavior.Fail,
        IReadOnlyList<string>? transitions = null)
    {
        return new WorkflowDescriptor
        {
            Id = "wf_test", Name = "test.wf", Version = 1,
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    Id = "step_01", Name = "Test Step",
                    Target = target,
                    OnError = onError,
                    Transitions = transitions ?? Array.Empty<string>()
                }
            }
        };
    }

    [Fact]
    public void Validate_SubWorkflowTarget_Throws()
    {
        var descriptor = CreateDescriptorWithStep(
            new SubWorkflowTarget
            {
                SubWorkflow = new VersionedDescriptorRef<WorkflowDescriptor>("wf_sub", 1)
            });

        var validator = new WorkflowCompatibilityValidator();
        var act = () => validator.Validate(descriptor);

        act.Should().Throw<WorkflowValidationException>()
            .WithMessage("*SubWorkflowTarget*");
    }

    [Fact]
    public void Validate_RetryErrorBehavior_Throws()
    {
        var descriptor = CreateDescriptorWithStep(
            new HumanTaskTarget
            {
                HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
            },
            onError: StepErrorBehavior.Retry);

        var validator = new WorkflowCompatibilityValidator();
        var act = () => validator.Validate(descriptor);

        act.Should().Throw<WorkflowValidationException>()
            .WithMessage("*StepErrorBehavior.Retry*");
    }

    [Fact]
    public void Validate_CompensateErrorBehavior_Throws()
    {
        var descriptor = CreateDescriptorWithStep(
            new HumanTaskTarget
            {
                HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
            },
            onError: StepErrorBehavior.Compensate);

        var validator = new WorkflowCompatibilityValidator();
        var act = () => validator.Validate(descriptor);

        act.Should().Throw<WorkflowValidationException>()
            .WithMessage("*StepErrorBehavior.Compensate*");
    }

    [Fact]
    public void Validate_Transitions_Throws()
    {
        var descriptor = CreateDescriptorWithStep(
            new HumanTaskTarget
            {
                HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
            },
            transitions: new List<string> { "step_02" });

        var validator = new WorkflowCompatibilityValidator();
        var act = () => validator.Validate(descriptor);

        act.Should().Throw<WorkflowValidationException>()
            .WithMessage("*transition*");
    }

    [Fact]
    public void Validate_ValidDescriptor_DoesNotThrow()
    {
        var descriptor = CreateDescriptorWithStep(
            new CapabilityTarget
            {
                Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_01", 1)
            },
            onError: StepErrorBehavior.Skip);

        var validator = new WorkflowCompatibilityValidator();
        var act = () => validator.Validate(descriptor);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_FailErrorBehavior_DoesNotThrow()
    {
        var descriptor = CreateDescriptorWithStep(
            new CapabilityTarget
            {
                Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_01", 1)
            },
            onError: StepErrorBehavior.Fail);

        var validator = new WorkflowCompatibilityValidator();
        var act = () => validator.Validate(descriptor);

        act.Should().NotThrow();
    }
}
