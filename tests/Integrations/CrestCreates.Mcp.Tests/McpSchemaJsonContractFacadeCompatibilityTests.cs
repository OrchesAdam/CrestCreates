using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Mcp.Tests;

public sealed class McpSchemaJsonContractFacadeCompatibilityTests
{
    [Fact]
    public void Mcp_facade_preserves_shared_projection_bytes()
    {
        var schema = Schema(new SchemaFieldDescriptor
        {
            Name = "value",
            FieldType = "long",
            IsRequired = true,
            IsNullable = true
        });

        var shared = new SchemaJsonContractProjector().ProjectObject(schema);
        var mcp = new McpJsonSchemaProjector().ProjectInput(schema);

        mcp.GetRawText().Should().Be(shared.GetRawText());
        mcp.GetRawText().Should().Be(
            "{\"type\":\"object\",\"properties\":{" +
            "\"value\":{\"type\":[\"integer\",\"null\"]," +
            "\"minimum\":-9223372036854775808,\"maximum\":9223372036854775807}}," +
            "\"required\":[\"value\"],\"additionalProperties\":false}");
    }

    [Fact]
    public void Mcp_facade_preserves_projection_error_code_and_message()
    {
        var schema = Schema(new SchemaFieldDescriptor
        {
            Name = "value",
            FieldType = "string",
            Pattern = "^x$"
        });

        var action = () => new McpJsonSchemaProjector().ProjectInput(schema);

        var exception = action.Should().Throw<McpToolConfigurationException>().Which;
        exception.Code.Should().Be("MCP120");
        exception.Message.Should().Be("Schema patterns are not portable to MCP JSON Schema.");
    }

    [Fact]
    public void Mcp_facade_preserves_parity_error_code_and_message()
    {
        var action = () => new McpToolSchemaParityValidator().ValidateInput(
            Schema(),
            McpTestJsonContext.Default.String);

        var exception = action.Should().Throw<McpToolConfigurationException>().Which;
        exception.Code.Should().Be("MCP115");
        exception.Message.Should().Be("MCP JSON contract root must be an object.");
    }

    private static SchemaDescriptor Schema(params SchemaFieldDescriptor[] fields) => new()
    {
        Id = "schema.test",
        Name = "Test",
        Version = 1,
        Fields = fields
    };
}
