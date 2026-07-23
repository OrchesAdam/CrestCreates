using System.Text.Json.Serialization;

namespace CrestCreates.Mcp.Memory.Security;

[JsonSerializable(typeof(McpInvocationBindingComponents))]
[JsonSerializable(typeof(McpSessionBindingComponents))]
internal partial class McpMemoryBindingJsonContext : JsonSerializerContext;
