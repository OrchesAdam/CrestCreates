using System.Text.Json.Serialization;
using CrestCreates.Agent.Memory.Abstractions.Accountability;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Abstractions.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AgentMemoryRecallAccountabilityPayload))]
[JsonSerializable(typeof(AgentMemoryCurationAccountabilityPayload))]
[JsonSerializable(typeof(AgentMemorySourceExpansionAccountabilityPayload))]
public sealed partial class AgentMemoryAccountabilityJsonSerializerContext : JsonSerializerContext;
