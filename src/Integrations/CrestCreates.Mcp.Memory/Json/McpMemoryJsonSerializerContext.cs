using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;

namespace CrestCreates.Mcp.Memory.Json;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonContractSurface(typeof(McpMemoryTools))]
public partial class McpMemoryJsonSerializerContext : JsonSerializerContext;
