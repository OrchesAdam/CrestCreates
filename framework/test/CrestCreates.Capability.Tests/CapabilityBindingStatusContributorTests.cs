using CrestCreates.Capability;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityBindingStatusContributorTests
{
    private static CapabilityDescriptor CreateDescriptor(string id = "test.cap", string? inputSchemaId = null,
        int? inputSchemaVersion = null, string? outputSchemaId = null, int? outputSchemaVersion = null)
    {
        return new CapabilityDescriptor
        {
            Id = id, Name = "Test Capability", Version = 1, State = DescriptorState.Active,
            InputSchema = inputSchemaId != null
                ? new VersionedDescriptorRef<SchemaDescriptor> { Id = inputSchemaId, Version = inputSchemaVersion ?? 1 }
                : null,
            OutputSchema = outputSchemaId != null
                ? new VersionedDescriptorRef<SchemaDescriptor> { Id = outputSchemaId, Version = outputSchemaVersion ?? 1 }
                : null
        };
    }

    [Fact]
    public void Evaluate_NoHandler_ReturnsUnbound()
    {
        var capRegistry = new Mock<ICapabilityRegistry>();
        var handlerResolver = new Mock<ICapabilityHandlerResolver>();
        handlerResolver.Setup(r => r.Resolve("test.cap")).Returns((ICapabilityHandlerInvoker?)null);
        var schemaRegistry = new Mock<ISchemaRegistry>();
        schemaRegistry.Setup(r => r.GetByVersion(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(new SchemaDescriptor { Id = "input", Name = "Input", Version = 1, Fields = Array.Empty<SchemaFieldDescriptor>() });

        var contributor = new CapabilityBindingStatusContributor(capRegistry.Object, handlerResolver.Object, schemaRegistry.Object);
        var descriptor = CreateDescriptor("test.cap", "input", 1, "output", 1);

        var result = contributor.Evaluate(descriptor);

        result.Status.Should().Be(DescriptorBindingStatus.Unbound);
        result.Issues.Should().Contain(i => i.Code == "BIND_NO_HANDLER");
    }

    [Fact]
    public void Evaluate_MissingSchemaRef_ReturnsInvalid()
    {
        var capRegistry = new Mock<ICapabilityRegistry>();
        var handlerResolver = new Mock<ICapabilityHandlerResolver>();
        handlerResolver.Setup(r => r.Resolve("test.cap")).Returns(Mock.Of<ICapabilityHandlerInvoker>());
        var schemaRegistry = new Mock<ISchemaRegistry>();
        schemaRegistry.Setup(r => r.GetByVersion("missing_input", 1)).Returns((SchemaDescriptor?)null);
        schemaRegistry.Setup(r => r.GetByVersion("output", 1))
            .Returns(new SchemaDescriptor { Id = "output", Name = "Output", Version = 1, Fields = Array.Empty<SchemaFieldDescriptor>() });

        var contributor = new CapabilityBindingStatusContributor(capRegistry.Object, handlerResolver.Object, schemaRegistry.Object);
        var descriptor = CreateDescriptor("test.cap", "missing_input", 1, "output", 1);

        var result = contributor.Evaluate(descriptor);

        result.Status.Should().Be(DescriptorBindingStatus.Invalid);
        result.Issues.Should().Contain(i => i.Code == "REF_MISSING_INPUT_SCHEMA");
    }

    [Fact]
    public void Evaluate_HandlerAndSchemas_ReturnsRuntimeReady()
    {
        var capRegistry = new Mock<ICapabilityRegistry>();
        var handlerResolver = new Mock<ICapabilityHandlerResolver>();
        handlerResolver.Setup(r => r.Resolve("test.cap")).Returns(Mock.Of<ICapabilityHandlerInvoker>());
        var schemaRegistry = new Mock<ISchemaRegistry>();
        schemaRegistry.Setup(r => r.GetByVersion("input", 1))
            .Returns(new SchemaDescriptor { Id = "input", Name = "Input", Version = 1, Fields = Array.Empty<SchemaFieldDescriptor>() });
        schemaRegistry.Setup(r => r.GetByVersion("output", 1))
            .Returns(new SchemaDescriptor { Id = "output", Name = "Output", Version = 1, Fields = Array.Empty<SchemaFieldDescriptor>() });

        var contributor = new CapabilityBindingStatusContributor(capRegistry.Object, handlerResolver.Object, schemaRegistry.Object);
        var descriptor = CreateDescriptor("test.cap", "input", 1, "output", 1);

        var result = contributor.Evaluate(descriptor);

        result.Status.Should().Be(DescriptorBindingStatus.RuntimeReady);
    }
}
