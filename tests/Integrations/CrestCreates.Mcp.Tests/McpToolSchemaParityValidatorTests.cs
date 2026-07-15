using System.Text.Json.Serialization;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Mcp.Tests;

public sealed class McpToolSchemaParityValidatorTests
{
    [Fact]
    public void Input_requires_object_json_contract()
    {
        var action = () => new McpToolSchemaParityValidator().ValidateInput(
            Schema(),
            McpTestJsonContext.Default.String);

        action.Should().Throw<McpToolConfigurationException>().Which.Code.Should().Be("MCP115");
    }

    [Fact]
    public void Input_requires_deserializable_property_with_matching_json_name()
    {
        var schema = Schema(new SchemaFieldDescriptor { Name = "name", FieldType = "string", IsNullable = true });

        var action = () => new McpToolSchemaParityValidator().ValidateInput(
            schema,
            McpTestJsonContext.Default.ReadOnlyInputDto);

        action.Should().Throw<McpToolConfigurationException>().Which.Code.Should().Be("MCP108");
    }

    [Fact]
    public void Directionally_valid_input_and_output_contracts_pass()
    {
        var schema = Schema(new SchemaFieldDescriptor { Name = "name", FieldType = "string", IsNullable = true });
        var validator = new McpToolSchemaParityValidator();

        validator.Invoking(instance => instance.ValidateInput(schema, McpTestJsonContext.Default.MutableDto))
            .Should().NotThrow();
        validator.Invoking(instance => instance.ValidateOutput(schema, McpTestJsonContext.Default.MutableDto))
            .Should().NotThrow();
    }

    [Fact]
    public void Input_json_required_metadata_is_rejected_so_schema_owns_presence_validation()
    {
        var schema = Schema(new SchemaFieldDescriptor { Name = "name", FieldType = "string", IsRequired = true });

        var action = () => new McpToolSchemaParityValidator().ValidateInput(
            schema,
            McpTestJsonContext.Default.JsonRequiredInputDto);

        action.Should().Throw<McpToolConfigurationException>().Which.Code.Should().Be("MCP108");
    }

    [Fact]
    public void Scalar_schema_type_must_match_json_property_type()
    {
        var schema = Schema(new SchemaFieldDescriptor { Name = "name", FieldType = "int", IsNullable = true });

        var action = () => new McpToolSchemaParityValidator().ValidateInput(
            schema,
            McpTestJsonContext.Default.MutableDto);

        action.Should().Throw<McpToolConfigurationException>().Which.Code.Should().Be("MCP108");
    }

    [Fact]
    public void Collection_shape_and_element_type_must_match_json_property_type()
    {
        var schema = Schema(new SchemaFieldDescriptor
        {
            Name = "values",
            FieldType = "IList<string>",
            IsCollection = true,
            CollectionElementType = "int"
        });

        var action = () => new McpToolSchemaParityValidator().ValidateOutput(
            schema,
            McpTestJsonContext.Default.StringCollectionDto);

        action.Should().Throw<McpToolConfigurationException>().Which.Code.Should().Be("MCP108");
    }

    [Fact]
    public void Directional_json_properties_not_declared_in_schema_fail_closed()
    {
        var schema = Schema(new SchemaFieldDescriptor { Name = "name", FieldType = "string", IsNullable = true });

        var action = () => new McpToolSchemaParityValidator().ValidateOutput(
            schema,
            McpTestJsonContext.Default.ExtraOutputDto);

        action.Should().Throw<McpToolConfigurationException>().Which.Code.Should().Be("MCP108");
    }

    private static SchemaDescriptor Schema(params SchemaFieldDescriptor[] fields) => new()
    {
        Id = "schema.test",
        Name = "Test",
        Version = 1,
        Fields = fields
    };
}

public sealed class MutableDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class ReadOnlyInputDto
{
    [JsonPropertyName("name")]
    public string? Name { get; }
}

public sealed class StringCollectionDto
{
    [JsonPropertyName("values")]
    public List<string> Values { get; init; } = [];
}

public sealed class ExtraOutputDto
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("extra")]
    public string? Extra { get; init; }
}

public sealed class JsonRequiredInputDto
{
    [JsonPropertyName("name")]
    [JsonRequired]
    public string Name { get; set; } = string.Empty;
}

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(MutableDto))]
[JsonSerializable(typeof(ReadOnlyInputDto))]
[JsonSerializable(typeof(StringCollectionDto))]
[JsonSerializable(typeof(ExtraOutputDto))]
[JsonSerializable(typeof(JsonRequiredInputDto))]
internal partial class McpTestJsonContext : JsonSerializerContext;
