using System.Text.Json.Serialization;
using CrestCreates.Mcp;

namespace CrestCreates.Mcp.AotFixture;

public sealed class FixtureInput
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

public sealed class FixtureOutput
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

[McpToolSpecs]
public static partial class FixtureTools
{
    [McpToolSpec(
        "fixture.echo",
        InputType = typeof(FixtureInput),
        OutputType = typeof(FixtureOutput),
        ToolName = "fixture.echo",
        Description = "Echoes one value.")]
    public sealed class Echo
    {
    }
}

[JsonSerializable(typeof(FixtureInput))]
[JsonSerializable(typeof(FixtureOutput))]
internal partial class McpFixtureJsonContext : JsonSerializerContext;
