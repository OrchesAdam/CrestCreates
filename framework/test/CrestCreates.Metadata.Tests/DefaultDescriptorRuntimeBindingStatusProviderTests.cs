using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DefaultDescriptorRuntimeBindingStatusProviderTests
{
    [Fact]
    public void GetAllStatuses_AggregatesFromAllContributors()
    {
        var contributor1 = new Mock<IDescriptorBindingStatusContributor>();
        contributor1.Setup(c => c.SupportedKind).Returns(DescriptorKind.Capability);
        contributor1.Setup(c => c.Order).Returns(10);
        contributor1.Setup(c => c.GetDescriptors()).Returns(new IDescriptor[] { Mock.Of<IDescriptor>(d => d.FullId == "capability.test" && d.Kind == DescriptorKind.Capability) });
        contributor1.Setup(c => c.Evaluate(It.IsAny<IDescriptor>())).Returns(new DescriptorBindingReport
        {
            DescriptorId = "capability.test",
            DescriptorKind = DescriptorKind.Capability,
            Status = DescriptorBindingStatus.RuntimeReady
        });

        var contributor2 = new Mock<IDescriptorBindingStatusContributor>();
        contributor2.Setup(c => c.SupportedKind).Returns(DescriptorKind.Form);
        contributor2.Setup(c => c.Order).Returns(20);
        contributor2.Setup(c => c.GetDescriptors()).Returns(new IDescriptor[] { Mock.Of<IDescriptor>(d => d.FullId == "form.test" && d.Kind == DescriptorKind.Form) });
        contributor2.Setup(c => c.Evaluate(It.IsAny<IDescriptor>())).Returns(new DescriptorBindingReport
        {
            DescriptorId = "form.test",
            DescriptorKind = DescriptorKind.Form,
            Status = DescriptorBindingStatus.PartiallyBound
        });

        var provider = new DefaultDescriptorRuntimeBindingStatusProvider(
            new[] { contributor1.Object, contributor2.Object });

        var report = provider.GetAllStatuses();

        report.Descriptors.Should().HaveCount(2);
        report.Descriptors[0].DescriptorId.Should().Be("capability.test");
        report.Descriptors[1].DescriptorId.Should().Be("form.test");
    }

    [Fact]
    public void GetAllStatuses_EmptyContributor_Skipped()
    {
        var contributor = new Mock<IDescriptorBindingStatusContributor>();
        contributor.Setup(c => c.SupportedKind).Returns(DescriptorKind.Event);
        contributor.Setup(c => c.Order).Returns(10);
        contributor.Setup(c => c.GetDescriptors()).Returns(Array.Empty<IDescriptor>());

        var provider = new DefaultDescriptorRuntimeBindingStatusProvider(new[] { contributor.Object });

        var report = provider.GetAllStatuses();

        report.Descriptors.Should().BeEmpty();
    }

    [Fact]
    public void GetStatus_UnknownKind_ReturnsPartiallyBound()
    {
        var provider = new DefaultDescriptorRuntimeBindingStatusProvider(
            Array.Empty<IDescriptorBindingStatusContributor>());

        var descriptor = Mock.Of<IDescriptor>(d =>
            d.FullId == "schema.test" && d.Kind == DescriptorKind.Schema);

        var result = provider.GetStatus(descriptor);

        result.Status.Should().Be(DescriptorBindingStatus.PartiallyBound);
        result.Issues.Should().ContainSingle(i => i.Code == "WARN_NO_BINDING_CONTRIBUTOR");
    }

    [Fact]
    public void GetStatus_KnownKind_DelegatesToContributor()
    {
        var contributor = new Mock<IDescriptorBindingStatusContributor>();
        contributor.Setup(c => c.SupportedKind).Returns(DescriptorKind.Workflow);
        contributor.Setup(c => c.Order).Returns(10);
        contributor.Setup(c => c.Evaluate(It.IsAny<IDescriptor>())).Returns(new DescriptorBindingReport
        {
            DescriptorId = "workflow.test",
            DescriptorKind = DescriptorKind.Workflow,
            Status = DescriptorBindingStatus.RuntimeReady
        });

        var provider = new DefaultDescriptorRuntimeBindingStatusProvider(new[] { contributor.Object });
        var descriptor = Mock.Of<IDescriptor>(d =>
            d.FullId == "workflow.test" && d.Kind == DescriptorKind.Workflow);

        var result = provider.GetStatus(descriptor);

        result.Status.Should().Be(DescriptorBindingStatus.RuntimeReady);
        contributor.Verify(c => c.Evaluate(descriptor), Times.Once);
    }
}
