using System.Text.Json.Serialization;
using CrestCreates.Mcp;

namespace CrestCreates.Mcp.E2E.Tests;

public sealed class EchoInput
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

public sealed class EchoOutput
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

[McpToolSpecs]
public static partial class E2ETools
{
    [McpToolSpec(
        "e2e.echo",
        InputType = typeof(EchoInput),
        OutputType = typeof(EchoOutput),
        ToolName = "e2e.echo",
        Description = "Echoes one value.")]
    public sealed class Echo
    {
    }
}

[JsonSerializable(typeof(EchoInput))]
[JsonSerializable(typeof(EchoOutput))]
internal partial class E2EJsonContext : JsonSerializerContext;
