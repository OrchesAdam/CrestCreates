using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Schema.Tests;

public sealed class SchemaJsonContractProjectorTests
{
    private readonly SchemaJsonContractProjector _projector = new();

    [Fact]
    public void Empty_object_projection_is_byte_stable()
        => _projector.ProjectObject(null).GetRawText().Should().Be(
            "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}");

    [Fact]
    public void Projection_preserves_ordinal_order_constraints_and_collection_shape()
    {
        var schema = Schema(
            new SchemaFieldDescriptor
            {
                Name = "zeta",
                FieldType = "IList<Guid>",
                IsCollection = true,
                CollectionElementType = "Guid",
                IsNullable = true
            },
            new SchemaFieldDescriptor
            {
                Name = "alpha",
                FieldType = "int",
                IsRequired = true,
                MinValue = 1,
                MaxValue = 10
            });

        _projector.ProjectObject(schema).GetRawText().Should().Be(
            "{\"type\":\"object\",\"properties\":{" +
            "\"alpha\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10}," +
            "\"zeta\":{\"type\":[\"array\",\"null\"],\"items\":{\"type\":\"string\",\"format\":\"uuid\"}}}," +
            "\"required\":[\"alpha\"],\"additionalProperties\":false}");
    }

    [Fact]
    public void Long_inherent_bounds_are_emitted_as_exact_integer_literals()
    {
        var schema = Schema(new SchemaFieldDescriptor { Name = "value", FieldType = "long" });

        _projector.ProjectObject(schema).GetRawText().Should().Contain(
            "\"minimum\":-9223372036854775808,\"maximum\":9223372036854775807");
    }

    [Theory]
    [InlineData("GUID", SchemaJsonContractViolation.ScalarTypeUnsupported)]
    [InlineData("System.Guid", SchemaJsonContractViolation.ScalarTypeUnsupported)]
    [InlineData("uuid", SchemaJsonContractViolation.ScalarTypeUnsupported)]
    public void Unsupported_scalar_tokens_fail_closed(
        string token,
        SchemaJsonContractViolation violation)
    {
        var schema = Schema(new SchemaFieldDescriptor { Name = "value", FieldType = token });

        var action = () => _projector.ProjectObject(schema);

        action.Should().Throw<SchemaJsonContractException>()
            .Which.Violation.Should().Be(violation);
    }

    [Fact]
    public void Pattern_outside_the_portable_subset_fails_closed()
    {
        var schema = Schema(new SchemaFieldDescriptor
        {
            Name = "value",
            FieldType = "string",
            Pattern = "^x$"
        });

        var action = () => _projector.ProjectObject(schema);

        action.Should().Throw<SchemaJsonContractException>()
            .Which.Violation.Should().Be(SchemaJsonContractViolation.PatternUnsupported);
    }

    private static SchemaDescriptor Schema(params SchemaFieldDescriptor[] fields) => new()
    {
        Id = "schema.test",
        Name = "Test",
        Version = 1,
        Fields = fields
    };
}
