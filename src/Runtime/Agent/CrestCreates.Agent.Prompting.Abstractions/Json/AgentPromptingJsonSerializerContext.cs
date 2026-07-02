using System.Text.Json.Serialization;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Prompting.Abstractions.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AgentPromptInputEvidenceSummary))]
[JsonSerializable(typeof(AgentPromptOutputEvidenceSummary))]
[JsonSerializable(typeof(AgentPromptProviderObservation))]
[JsonSerializable(typeof(AgentPromptDiagnostic))]
[JsonSerializable(typeof(AgentPromptPurpose))]
[JsonSerializable(typeof(CanonicalHash))]
public sealed partial class AgentPromptingJsonSerializerContext : JsonSerializerContext;
