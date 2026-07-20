using CrestCreates.Metadata.Abstractions;
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

    [Fact]
    public void Nested_object_is_projected_through_a_deterministic_definition_reference()
    {
        var address = Schema(
            "address",
            new SchemaFieldDescriptor { Name = "city", FieldType = "string", IsRequired = true });
        var root = Schema(
            "root",
            new SchemaFieldDescriptor
            {
                Name = "address",
                FieldType = "object",
                ObjectSchema = new VersionedDescriptorRef<SchemaDescriptor>("address", 1)
            });

        var projected = _projector.ProjectObject(root, [address]);
        var property = projected.GetProperty("properties").GetProperty("address");

        property.GetProperty("$ref").GetString().Should().StartWith("#/$defs/schema-");
        projected.GetProperty("$defs").EnumerateObject().Should().ContainSingle();
        projected.GetProperty("$defs").EnumerateObject().Single().Value
            .GetProperty("properties").GetProperty("city").GetProperty("type").GetString()
            .Should().Be("string");
    }

    [Fact]
    public void Nested_object_collection_uses_non_null_reference_items()
    {
        var item = Schema("item", new SchemaFieldDescriptor { Name = "value", FieldType = "int" });
        var root = Schema(
            "root",
            new SchemaFieldDescriptor
            {
                Name = "items",
                FieldType = "object",
                IsCollection = true,
                ObjectSchema = new VersionedDescriptorRef<SchemaDescriptor>("item", 1)
            });

        var projected = _projector.ProjectObject(root, [item]);
        var items = projected.GetProperty("properties").GetProperty("items");

        items.GetProperty("type").GetString().Should().Be("array");
        items.GetProperty("items").GetProperty("$ref").GetString().Should().StartWith("#/$defs/schema-");
    }

    [Fact]
    public void Direct_schema_references_are_resolved_into_defs()
    {
        var referenced = Schema("address", new SchemaFieldDescriptor
        {
            Name = "city", FieldType = "string"
        });
        var root = new SchemaDescriptor
        {
            Id = "root",
            Name = "Test",
            Version = 1,
            References = [new VersionedDescriptorRef<SchemaDescriptor>("address", 1)]
        };

        var projected = _projector.ProjectObject(root, [referenced]);

        projected.GetProperty("$defs").EnumerateObject().Should().ContainSingle();
    }

    [Fact]
    public void Unresolved_direct_schema_reference_fails_closed()
    {
        var root = new SchemaDescriptor
        {
            Id = "root",
            Name = "Test",
            Version = 1,
            References = [new VersionedDescriptorRef<SchemaDescriptor>("missing", 1)]
        };

        var action = () => _projector.ProjectObject(root, Array.Empty<SchemaDescriptor>());

        action.Should().Throw<SchemaJsonContractException>()
            .Which.Violation.Should().Be(SchemaJsonContractViolation.NestedSchemaNotFound);
    }

    [Fact]
    public void Nested_schema_cycle_fails_closed()
    {
        var root = Schema(
            "root",
            new SchemaFieldDescriptor
            {
                Name = "child",
                FieldType = "object",
                ObjectSchema = new VersionedDescriptorRef<SchemaDescriptor>("child", 1)
            });
        var child = Schema(
            "child",
            new SchemaFieldDescriptor
            {
                Name = "parent",
                FieldType = "object",
                ObjectSchema = new VersionedDescriptorRef<SchemaDescriptor>("root", 1)
            });

        var action = () => _projector.ProjectObject(root, [root, child]);

        action.Should().Throw<SchemaJsonContractException>()
            .Which.Violation.Should().Be(SchemaJsonContractViolation.NestedSchemaCycle);
    }

    private static SchemaDescriptor Schema(params SchemaFieldDescriptor[] fields) => Schema("schema.test", fields);

    private static SchemaDescriptor Schema(string id, params SchemaFieldDescriptor[] fields) => new()
    {
        Id = id,
        Name = "Test",
        Version = 1,
        Fields = fields
    };
}
