using System.Text.Json.Serialization;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Mcp.Memory.Json;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(RecallAgentContextInput))]
[JsonSerializable(typeof(RecallAgentContextResult))]
[JsonSerializable(typeof(BuildAgentMemoryPackInput))]
[JsonSerializable(typeof(BuildAgentMemoryPackResult))]
[JsonSerializable(typeof(ExpandAgentMemorySourceInput))]
[JsonSerializable(typeof(ExpandAgentMemorySourceResult))]
[JsonSerializable(typeof(AgentMemoryToolBlockDto))]
[JsonSerializable(typeof(AgentMemoryToolDiagnosticDto))]
[JsonSerializable(typeof(AgentMemoryToolCanonicalHashDto))]
[JsonSerializable(typeof(AgentMemoryToolOperationStatus))]
[JsonSerializable(typeof(AgentMemoryToolSourceKind))]
[JsonSerializable(typeof(AgentMemoryToolMemoryStatus))]
[JsonSerializable(typeof(AgentMemoryToolKind))]
[JsonSerializable(typeof(AgentMemoryToolConfidence))]
[JsonSerializable(typeof(AgentMemoryToolDiagnosticSeverity))]
[JsonSerializable(typeof(AgentMemorySourceGrantDto))]
[JsonSerializable(typeof(AgentMemoryToolItemDto))]
public partial class McpMemoryJsonSerializerContext : JsonSerializerContext;
