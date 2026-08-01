using CrestCreates.Metadata.Abstractions;
using CrestCreates.Runtime.Persistence.Abstractions.Errors;
using CrestCreates.Runtime.Persistence.State;
using CrestCreates.Runtime.Persistence.Tests.Json;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Runtime.Persistence.Tests.State;

public sealed class RuntimeStateSchemaValidationTests
{
    [Fact]
    public void RuntimeStateContractStartup_ShouldRejectMissingSchemaRef()
    {
        var registry = new Mock<ISchemaRegistry>();
        registry.Setup(x => x.GetByVersion("missing", 1)).Returns((SchemaDescriptor?)null);

        var act = () => Build(registry.Object, new DescriptorRef("schema", "missing", 1));

        act.Should().Throw<RuntimeStateContractException>();
    }

    [Fact]
    public void RuntimeStateContractStartup_ShouldRejectNonExactSchemaRef()
    {
        var registry = new Mock<ISchemaRegistry>();

        var act = () => Build(registry.Object, new DescriptorRef("schema", "known", null));

        act.Should().Throw<RuntimeStateContractException>();
        registry.Verify(x => x.GetByVersion(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void RuntimeStateContractStartup_ShouldRejectWrongDescriptorKind()
    {
        var registry = new Mock<ISchemaRegistry>();

        var act = () => Build(registry.Object, new DescriptorRef("workflow", "known", 1));

        act.Should().Throw<RuntimeStateContractException>();
        registry.Verify(x => x.GetByVersion(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    private static void Build(ISchemaRegistry schemaRegistry, DescriptorRef schemaRef)
    {
        var builder = new RuntimeStateContractBuilder();
        builder.Add(
            "test/runtime/schema-state/v1",
            TestRuntimeStateJsonSerializerContext.Default.MutableNestedRuntimeState,
            TestRuntimeStateJsonSerializerContext.TestRuntimeStateJsonSerializerContextRootManifest.AllDirectRootTypes,
            schemaRef);
        builder.Build(new RuntimeStateContractStartupValidator(schemaRegistry));
    }
}
