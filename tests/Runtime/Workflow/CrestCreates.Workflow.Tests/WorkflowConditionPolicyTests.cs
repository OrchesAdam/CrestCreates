using CrestCreates.Capability.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Runtime.Persistence;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public sealed class WorkflowConditionPolicyTests
{
    [Fact]
    public void PublicTokens_AreCanonicalAndExact()
    {
        WorkflowConditionTokens.PreviousHumanTaskApproved.Should().Be("previous-human-task-approved");
        WorkflowConditionTokens.PreviousHumanTaskRejected.Should().Be("previous-human-task-rejected");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Previous-Human-Task-Approved")]
    [InlineData("workflow.input == true")]
    public void CompatibilityValidation_RejectsUnsupportedCondition(string condition)
    {
        var descriptor = Descriptor(
            new WorkflowStep
            {
                Id = "conditional",
                Target = HumanTaskTarget("final"),
                Condition = condition
            });
        var validator = new WorkflowCompatibilityValidator(
            new Mock<ICapabilityRegistry>().Object,
            HumanTaskRegistry("initial", "final"));

        var act = () => validator.Validate(descriptor);

        act.Should().Throw<WorkflowValidationException>()
            .WithMessage("*unsupported condition*");
    }

    [Fact]
    public void CompatibilityValidation_RejectsFirstStepCondition()
    {
        var descriptor = Descriptor(new WorkflowStep
        {
            Id = "first",
            Target = HumanTaskTarget("initial"),
            Condition = WorkflowConditionTokens.PreviousHumanTaskApproved
        });
        var validator = new WorkflowCompatibilityValidator(
            new Mock<ICapabilityRegistry>().Object,
            HumanTaskRegistry("initial"));

        var act = () => validator.Validate(descriptor);

        act.Should().Throw<WorkflowValidationException>()
            .WithMessage("*immediately preceding step*HumanTask*");
    }

    [Fact]
    public void BindingStatus_RejectsConditionAfterCapabilityStep()
    {
        var wfRegistry = new Mock<IWorkflowRegistry>();
        var schemaRegistry = new Mock<ISchemaRegistry>();
        var capabilities = new Mock<ICapabilityRegistry>();
        capabilities.Setup(registry => registry.GetByVersion("cap", 1))
            .Returns(new CapabilityDescriptor { Id = "cap", Name = "cap", Version = 1 });
        var tasks = new Mock<IHumanTaskRegistry>();
        tasks.Setup(registry => registry.GetByVersion("final", 1))
            .Returns(new HumanTaskDescriptor { Id = "final", Name = "final", Version = 1 });
        var contributor = new WorkflowBindingStatusContributor(
            wfRegistry.Object, schemaRegistry.Object, capabilities.Object, tasks.Object);
        var descriptor = Descriptor(
            new WorkflowStep
            {
                Id = "capability",
                Target = new CapabilityTarget
                {
                    Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap", 1)
                }
            },
            new WorkflowStep
            {
                Id = "conditional",
                Target = HumanTaskTarget("final"),
                Condition = WorkflowConditionTokens.PreviousHumanTaskApproved
            });

        var report = contributor.Evaluate(descriptor);

        report.Status.Should().Be(DescriptorBindingStatus.Invalid);
        report.Issues.Should().Contain(issue => issue.Code == "INVALID_CONDITION");
    }

    [Fact]
    public void Evaluation_DoesNotTreatSeededOutcomeAsCompletedPredecessor()
    {
        var descriptor = Descriptor(
            new WorkflowStep { Id = "initial", Target = HumanTaskTarget("initial") },
            new WorkflowStep
            {
                Id = "conditional",
                Target = HumanTaskTarget("final"),
                Condition = WorkflowConditionTokens.PreviousHumanTaskApproved
            });
        var instance = new WorkflowInstance { StepIndex = 1 };
        instance.Variables["lastStepOutcome"] = StateRegistry().Capture("Approve");

        var act = () => WorkflowConditionPolicy.Evaluate(descriptor, instance, 1, StateRegistry());

        act.Should().Throw<WorkflowValidationException>()
            .WithMessage("*completed result*immediately preceding HumanTask*");
    }

    [Fact]
    public void Evaluation_RejectsMalformedPersistedOutcome()
    {
        var descriptor = Descriptor(
            new WorkflowStep { Id = "initial", Target = HumanTaskTarget("initial") },
            new WorkflowStep
            {
                Id = "conditional",
                Target = HumanTaskTarget("final"),
                Condition = WorkflowConditionTokens.PreviousHumanTaskApproved
            });
        var stateRegistry = StateRegistry();
        var instance = new WorkflowInstance { StepIndex = 1 };
        instance.StepResults.Add(new WorkflowStepResult
        {
            StepId = "initial",
            Status = StepExecutionStatus.Completed
        });
        instance.Variables["lastStepOutcome"] = stateRegistry.Capture(42);

        var act = () => WorkflowConditionPolicy.Evaluate(descriptor, instance, 1, stateRegistry);

        act.Should().Throw<WorkflowValidationException>()
            .WithMessage("*malformed persisted lastStepOutcome*");
    }

    private static WorkflowDescriptor Descriptor(params WorkflowStep[] steps) => new()
    {
        Id = "workflow",
        Name = "workflow",
        Version = 1,
        Steps = steps
    };

    private static HumanTaskTarget HumanTaskTarget(string id) => new()
    {
        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>(id, 1)
    };

    private static IHumanTaskRegistry HumanTaskRegistry(params string[] ids)
    {
        var registry = new Mock<IHumanTaskRegistry>();
        registry.Setup(value => value.GetById(It.IsAny<string>()))
            .Returns((string id) => ids.Contains(id, StringComparer.Ordinal)
                ? new HumanTaskDescriptor { Id = id, Name = id, Version = 1 }
                : null);
        return registry.Object;
    }

    private static IRuntimeStateContractRegistry StateRegistry()
    {
        var services = new ServiceCollection();
        services.AddRuntimePersistence();
        return services.BuildServiceProvider().GetRequiredService<IRuntimeStateContractRegistry>();
    }
}
