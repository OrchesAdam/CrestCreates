using System.Text.Json;

namespace CrestCreates.Mcp;

public sealed class McpJsonOptions
{
    public JsonSerializerOptions SerializerOptions { get; set; } = new();
}
