using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Mcp.Tests;

public sealed class McpJsonSchemaProjectorTests
{
    private readonly McpJsonSchemaProjector _projector = new();

    [Fact]
    public void Empty_input_has_canonical_closed_object_schema()
        => _projector.ProjectInput(null).GetRawText().Should().Be(
            "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}");

    [Fact]
    public void Projection_orders_properties_and_encodes_required_nullability_independently()
    {
        var schema = Schema(
            new SchemaFieldDescriptor { Name = "zeta", FieldType = "string", IsNullable = true },
            new SchemaFieldDescriptor { Name = "alpha", FieldType = "int", IsRequired = true });

        _projector.ProjectInput(schema).GetRawText().Should().Be(
            "{\"type\":\"object\",\"properties\":{" +
            "\"alpha\":{\"type\":\"integer\",\"minimum\":-2147483648,\"maximum\":2147483647}," +
            "\"zeta\":{\"type\":[\"string\",\"null\"]}}," +
            "\"required\":[\"alpha\"],\"additionalProperties\":false}");
    }

    [Fact]
    public void Nullable_collection_applies_nullability_to_array_not_items()
    {
        var schema = Schema(new SchemaFieldDescriptor
        {
            Name = "ids",
            FieldType = "IList<Guid>",
            IsCollection = true,
            CollectionElementType = "Guid",
            IsNullable = true
        });

        _projector.ProjectInput(schema).GetRawText().Should().Be(
            "{\"type\":\"object\",\"properties\":{" +
            "\"ids\":{\"type\":[\"array\",\"null\"],\"items\":{\"type\":\"string\",\"format\":\"uuid\"}}}," +
            "\"additionalProperties\":false}");
    }

    [Fact]
    public void Long_inherent_bounds_are_exact_integer_literals()
    {
        var schema = Schema(new SchemaFieldDescriptor { Name = "value", FieldType = "long" });

        _projector.ProjectInput(schema).GetRawText().Should().Contain(
            "\"minimum\":-9223372036854775808,\"maximum\":9223372036854775807");
    }

    [Theory]
    [InlineData("guid", "uuid")]
    [InlineData("Guid", "uuid")]
    [InlineData("date", "date")]
    [InlineData("DateOnly", "date")]
    [InlineData("DateTimeOffset", "date-time")]
    public void Legacy_and_canonical_tokens_map_to_formats(string token, string format)
    {
        var schema = Schema(new SchemaFieldDescriptor { Name = "value", FieldType = token });

        _projector.ProjectInput(schema).GetRawText().Should().Contain($"\"format\":\"{format}\"");
    }

    [Theory]
    [InlineData("GUID", "MCP113")]
    [InlineData("System.Guid", "MCP113")]
    [InlineData("uuid", "MCP113")]
    public void Unlisted_tokens_fail_closed(string token, string code)
    {
        var schema = Schema(new SchemaFieldDescriptor { Name = "value", FieldType = token });

        var action = () => _projector.ProjectInput(schema);

        action.Should().Throw<McpToolConfigurationException>().Which.Code.Should().Be(code);
    }

    [Fact]
    public void Pattern_is_rejected_only_by_mcp_projection()
    {
        var schema = Schema(new SchemaFieldDescriptor { Name = "value", FieldType = "string", Pattern = "^x$" });

        var action = () => _projector.ProjectInput(schema);

        action.Should().Throw<McpToolConfigurationException>().Which.Code.Should().Be("MCP120");
    }

    private static SchemaDescriptor Schema(params SchemaFieldDescriptor[] fields) => new()
    {
        Id = "schema.test",
        Name = "Test",
        Version = 1,
        Fields = fields
    };
}
