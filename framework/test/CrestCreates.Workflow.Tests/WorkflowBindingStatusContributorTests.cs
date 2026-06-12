using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class WorkflowBindingStatusContributorTests
{
    [Fact]
    public void Evaluate_MissingCapabilityTarget_ReturnsInvalid()
    {
        var wfRegistry = new Mock<IWorkflowRegistry>();
        var schemaRegistry = new Mock<ISchemaRegistry>();
        var capRegistry = new Mock<ICapabilityRegistry>();
        capRegistry.Setup(r => r.GetByVersion(It.IsAny<string>(), It.IsAny<int>())).Returns((CapabilityDescriptor?)null);
        var taskRegistry = new Mock<IHumanTaskRegistry>();

        var contributor = new WorkflowBindingStatusContributor(wfRegistry.Object, schemaRegistry.Object, capRegistry.Object, taskRegistry.Object);
        var wf = new WorkflowDescriptor
        {
            Id = "test.wf", Name = "Test", Version = 1,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "s1", Name = "Step1",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("missing", 1) },
                    OnError = StepErrorBehavior.Fail }
            }
        };

        var result = contributor.Evaluate(wf);
        result.Status.Should().Be(DescriptorBindingStatus.Invalid);
        result.Issues.Should().Contain(i => i.Code == "REF_MISSING_TARGET");
    }

    [Fact]
    public void Evaluate_SubWorkflowTarget_ReturnsUnsupported()
    {
        var wfRegistry = new Mock<IWorkflowRegistry>();
        var schemaRegistry = new Mock<ISchemaRegistry>();
        var capRegistry = new Mock<ICapabilityRegistry>();
        var taskRegistry = new Mock<IHumanTaskRegistry>();

        var contributor = new WorkflowBindingStatusContributor(wfRegistry.Object, schemaRegistry.Object, capRegistry.Object, taskRegistry.Object);
        var wf = new WorkflowDescriptor
        {
            Id = "test.wf", Name = "Test", Version = 1,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "s1", Name = "Step1",
                    Target = new SubWorkflowTarget { SubWorkflow = new VersionedDescriptorRef<WorkflowDescriptor>("child", 1) } }
            }
        };

        var result = contributor.Evaluate(wf);
        result.Status.Should().Be(DescriptorBindingStatus.Unsupported);
        result.Issues.Should().Contain(i => i.Code == "UNSUPPORTED_SUBWORKFLOW");
    }

    [Fact]
    public void Evaluate_Retry_ReturnsUnsupported()
    {
        var wfRegistry = new Mock<IWorkflowRegistry>();
        var schemaRegistry = new Mock<ISchemaRegistry>();
        var capRegistry = new Mock<ICapabilityRegistry>();
        capRegistry.Setup(r => r.GetByVersion("test.cap", 1))
            .Returns(new CapabilityDescriptor { Id = "test.cap", Name = "Test", Version = 1 });
        var taskRegistry = new Mock<IHumanTaskRegistry>();

        var contributor = new WorkflowBindingStatusContributor(wfRegistry.Object, schemaRegistry.Object, capRegistry.Object, taskRegistry.Object);
        var wf = new WorkflowDescriptor
        {
            Id = "test.wf", Name = "Test", Version = 1,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "s1", Name = "Step1",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("test.cap", 1) },
                    OnError = StepErrorBehavior.Retry }
            }
        };

        var result = contributor.Evaluate(wf);
        result.Status.Should().Be(DescriptorBindingStatus.Unsupported);
        result.Issues.Should().Contain(i => i.Code == "UNSUPPORTED_RETRY");
    }

    [Fact]
    public void Evaluate_Compensate_ReturnsUnsupported()
    {
        var wfRegistry = new Mock<IWorkflowRegistry>();
        var schemaRegistry = new Mock<ISchemaRegistry>();
        var capRegistry = new Mock<ICapabilityRegistry>();
        capRegistry.Setup(r => r.GetByVersion("test.cap", 1))
            .Returns(new CapabilityDescriptor { Id = "test.cap", Name = "Test", Version = 1 });
        var taskRegistry = new Mock<IHumanTaskRegistry>();

        var contributor = new WorkflowBindingStatusContributor(wfRegistry.Object, schemaRegistry.Object, capRegistry.Object, taskRegistry.Object);
        var wf = new WorkflowDescriptor
        {
            Id = "test.wf", Name = "Test", Version = 1,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "s1", Name = "Step1",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("test.cap", 1) },
                    OnError = StepErrorBehavior.Compensate }
            }
        };

        var result = contributor.Evaluate(wf);
        result.Status.Should().Be(DescriptorBindingStatus.Unsupported);
        result.Issues.Should().Contain(i => i.Code == "UNSUPPORTED_COMPENSATE");
    }

    [Fact]
    public void Evaluate_Transitions_ReturnsUnsupported()
    {
        var wfRegistry = new Mock<IWorkflowRegistry>();
        var schemaRegistry = new Mock<ISchemaRegistry>();
        var capRegistry = new Mock<ICapabilityRegistry>();
        capRegistry.Setup(r => r.GetByVersion("test.cap", 1))
            .Returns(new CapabilityDescriptor { Id = "test.cap", Name = "Test", Version = 1 });
        var taskRegistry = new Mock<IHumanTaskRegistry>();

        var contributor = new WorkflowBindingStatusContributor(wfRegistry.Object, schemaRegistry.Object, capRegistry.Object, taskRegistry.Object);
        var wf = new WorkflowDescriptor
        {
            Id = "test.wf", Name = "Test", Version = 1,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "s1", Name = "Step1",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("test.cap", 1) },
                    Transitions = new List<string> { "step2" } }
            }
        };

        var result = contributor.Evaluate(wf);
        result.Status.Should().Be(DescriptorBindingStatus.Unsupported);
        result.Issues.Should().Contain(i => i.Code == "UNSUPPORTED_TRANSITIONS");
    }

    [Fact]
    public void Evaluate_SupportedSteps_ReturnsRuntimeReady()
    {
        var wfRegistry = new Mock<IWorkflowRegistry>();
        var schemaRegistry = new Mock<ISchemaRegistry>();
        var capRegistry = new Mock<ICapabilityRegistry>();
        capRegistry.Setup(r => r.GetByVersion("test.cap", 1))
            .Returns(new CapabilityDescriptor { Id = "test.cap", Name = "Test", Version = 1 });
        var taskRegistry = new Mock<IHumanTaskRegistry>();

        var contributor = new WorkflowBindingStatusContributor(wfRegistry.Object, schemaRegistry.Object, capRegistry.Object, taskRegistry.Object);
        var wf = new WorkflowDescriptor
        {
            Id = "test.wf", Name = "Test", Version = 1,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "s1", Name = "Step1",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("test.cap", 1) },
                    OnError = StepErrorBehavior.Fail }
            }
        };

        var result = contributor.Evaluate(wf);
        result.Status.Should().Be(DescriptorBindingStatus.RuntimeReady);
    }
}
