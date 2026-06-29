using System.Text.Json.Serialization;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Abstractions.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AgentMemoryPack))]
[JsonSerializable(typeof(AgentAuthoringContext))]
[JsonSerializable(typeof(AgentAuthoringRequest))]
[JsonSerializable(typeof(AgentCompressedContext))]
[JsonSerializable(typeof(AgentCompressedContextBlock))]
[JsonSerializable(typeof(AgentMemoryCandidate))]
[JsonSerializable(typeof(AgentMemoryItem))]
[JsonSerializable(typeof(AgentMemoryQuery))]
[JsonSerializable(typeof(AgentContextSourceRef))]
[JsonSerializable(typeof(AgentContextEvidenceRef))]
[JsonSerializable(typeof(AgentConversationRecord))]
[JsonSerializable(typeof(AgentConversationTurn))]
[JsonSerializable(typeof(AgentTaskRecord))]
[JsonSerializable(typeof(AgentTaskEvent))]
[JsonSerializable(typeof(AgentSourceExpansionResult))]
[JsonSerializable(typeof(SanitizedAgentContent))]
[JsonSerializable(typeof(AgentMemoryOperationRequest))]
[JsonSerializable(typeof(AgentMemoryInvocationContext))]
[JsonSerializable(typeof(AgentMemoryDiagnostic))]
[JsonSerializable(typeof(CanonicalHash))]
public sealed partial class AgentMemoryJsonSerializerContext : JsonSerializerContext;
