using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.HumanTask.Tests;

public class HumanTaskBindingStatusContributorTests
{
    private static void SetupValidForm(Mock<IFormRegistry> formRegistry)
    {
        formRegistry.Setup(r => r.GetByVersion(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(new FormDescriptor { Id = "test.form", Name = "Form", Version = 1,
                Schema = new VersionedDescriptorRef<SchemaDescriptor>("s", 1) });
    }

    [Fact]
    public void Evaluate_RoundRobin_ReturnsUnsupported()
    {
        var taskRegistry = new Mock<IHumanTaskRegistry>();
        var formRegistry = new Mock<IFormRegistry>();
        SetupValidForm(formRegistry);
        var schemaRegistry = new Mock<ISchemaRegistry>();
        var capRegistry = new Mock<ICapabilityRegistry>();

        var contributor = new HumanTaskBindingStatusContributor(taskRegistry.Object, formRegistry.Object, schemaRegistry.Object, capRegistry.Object);
        var task = new HumanTaskDescriptor { Id = "test.task", Name = "Test", Version = 1,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("test.form", 1),
            AssigneeStrategy = AssigneeStrategy.RoundRobin };

        var result = contributor.Evaluate(task);
        result.Status.Should().Be(DescriptorBindingStatus.Unsupported);
        result.Issues.Should().Contain(i => i.Code == "UNSUPPORTED_ASSIGNEE_STRATEGY");
    }

    [Fact]
    public void Evaluate_LeastLoaded_ReturnsUnsupported()
    {
        var taskRegistry = new Mock<IHumanTaskRegistry>();
        var formRegistry = new Mock<IFormRegistry>();
        SetupValidForm(formRegistry);
        var schemaRegistry = new Mock<ISchemaRegistry>();
        var capRegistry = new Mock<ICapabilityRegistry>();

        var contributor = new HumanTaskBindingStatusContributor(taskRegistry.Object, formRegistry.Object, schemaRegistry.Object, capRegistry.Object);
        var task = new HumanTaskDescriptor { Id = "test.task", Name = "Test", Version = 1,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("test.form", 1),
            AssigneeStrategy = AssigneeStrategy.LeastLoaded };

        var result = contributor.Evaluate(task);
        result.Status.Should().Be(DescriptorBindingStatus.Unsupported);
    }

    [Fact]
    public void Evaluate_SingleUser_ReturnsRuntimeReady()
    {
        var taskRegistry = new Mock<IHumanTaskRegistry>();
        var formRegistry = new Mock<IFormRegistry>();
        SetupValidForm(formRegistry);
        var schemaRegistry = new Mock<ISchemaRegistry>();
        var capRegistry = new Mock<ICapabilityRegistry>();

        var contributor = new HumanTaskBindingStatusContributor(taskRegistry.Object, formRegistry.Object, schemaRegistry.Object, capRegistry.Object);
        var task = new HumanTaskDescriptor { Id = "test.task", Name = "Test", Version = 1,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("test.form", 1),
            AssigneeStrategy = AssigneeStrategy.SingleUser };

        var result = contributor.Evaluate(task);
        result.Status.Should().Be(DescriptorBindingStatus.RuntimeReady);
    }
}
