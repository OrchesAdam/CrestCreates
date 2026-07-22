using System.Text.Json.Serialization;
using CrestCreates.Agent.Memory.Projection.Abstractions;
using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Mcp.Memory.AotFixture;

[JsonSerializable(typeof(RecallAgentContextInput))]
[JsonSerializable(typeof(RecallAgentContextResult))]
[JsonSerializable(typeof(BuildAgentMemoryPackInput))]
[JsonSerializable(typeof(BuildAgentMemoryPackResult))]
[JsonSerializable(typeof(ExpandAgentMemorySourceInput))]
[JsonSerializable(typeof(ExpandAgentMemorySourceResult))]
[JsonSerializable(typeof(AgentMemoryToolOperationStatus))]
[JsonSerializable(typeof(AgentMemoryToolKind))]
[JsonSerializable(typeof(AgentMemoryToolConfidence))]
[JsonSerializable(typeof(AgentMemoryToolMemoryStatus))]
[JsonSerializable(typeof(AgentMemoryToolSourceKind))]
[JsonSerializable(typeof(AgentMemoryToolDiagnosticSeverity))]
[JsonSerializable(typeof(AgentMemoryToolCanonicalHashDto))]
[JsonSerializable(typeof(AgentMemoryToolItemDto))]
[JsonSerializable(typeof(AgentMemoryToolBlockDto))]
[JsonSerializable(typeof(AgentMemoryToolDiagnosticDto))]
[JsonSerializable(typeof(AgentMemorySourceGrantDto))]
internal partial class McpMemoryAotJsonContext : JsonSerializerContext;
