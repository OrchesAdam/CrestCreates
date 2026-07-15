using System.Text.Json;
using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

// semantic-string-guard: allow

namespace CrestCreates.Capability.Tests;

public sealed class ValidationMiddlewareJsonInputTests
{
    [Fact]
    public async Task InputJson_takes_precedence_and_object_path_is_not_called()
    {
        var validator = new Mock<ISchemaValidator>(MockBehavior.Strict);
        var capabilities = new Mock<ICapabilityRegistry>();
        var schemas = new Mock<ISchemaRegistry>();
        var schema = new SchemaDescriptor { Id = "input", Name = "Input", Version = 2 };
        var capability = new CapabilityDescriptor
        {
            Id = "orders.create",
            Name = "Create order",
            Version = 1,
            InputSchema = new VersionedDescriptorRef<SchemaDescriptor>(
                "input", 2, VersionSelectionMode.Exact)
        };
        using var json = JsonDocument.Parse("{\"name\":\"json\"}");
        var inputJson = json.RootElement.Clone();

        capabilities.Setup(registry => registry.GetByVersion(capability.Id, 1)).Returns(capability);
        schemas.Setup(registry => registry.GetByVersion("input", 2)).Returns(schema);
        validator.Setup(instance => instance.Validate(schema, inputJson))
            .Returns(SchemaValidationResult.Success());

        var middleware = new ValidationMiddleware(validator.Object, capabilities.Object, schemas.Object);
        var context = new CapabilityExecutionContext
        {
            ServiceProvider = null!,
            CapabilityId = capability.Id,
            CapabilityVersion = 1,
            Input = new InvalidOperationException("object path must not execute"),
            InputJson = inputJson
        };

        var result = await middleware.InvokeAsync(
            context,
            _ => Task.FromResult(CapabilityExecutionResult.Success(null, TimeSpan.Zero)));

        result.IsSuccess.Should().BeTrue();
        validator.Verify(instance => instance.Validate(schema, It.IsAny<object?>()), Times.Never);
        validator.Verify(instance => instance.Validate(schema, inputJson), Times.Once);
    }

    [Fact]
    public async Task Validation_failure_preserves_structured_issues()
    {
        var validator = new Mock<ISchemaValidator>();
        var capabilities = new Mock<ICapabilityRegistry>();
        var schemas = new Mock<ISchemaRegistry>();
        var schema = new SchemaDescriptor { Id = "input", Name = "Input", Version = 1 };
        var capability = new CapabilityDescriptor
        {
            Id = "orders.create",
            Name = "Create order",
            Version = 1,
            InputSchema = new VersionedDescriptorRef<SchemaDescriptor>("input", 1)
        };

        capabilities.Setup(registry => registry.GetByVersion(capability.Id, 1)).Returns(capability);
        schemas.Setup(registry => registry.GetByVersion("input", 1)).Returns(schema);
        validator.Setup(instance => instance.Validate(schema, It.IsAny<object?>()))
            .Returns(SchemaValidationResult.Failure([
                new SchemaValidationError
                {
                    FieldName = "name",
                    ErrorCode = SchemaValidationErrorCodes.FieldRequired,
                    Message = "Name is required."
                }
            ]));

        var middleware = new ValidationMiddleware(validator.Object, capabilities.Object, schemas.Object);
        var context = new CapabilityExecutionContext
        {
            ServiceProvider = null!,
            CapabilityId = capability.Id,
            CapabilityVersion = 1,
            Input = new object()
        };

        var result = await middleware.InvokeAsync(
            context,
            _ => Task.FromResult(CapabilityExecutionResult.Success(null, TimeSpan.Zero)));

        result.Issues.Should().ContainSingle().Which.Should().Be(
            new CapabilityExecutionIssue("FIELD_REQUIRED", "Name is required.", "name"));
    }
}
