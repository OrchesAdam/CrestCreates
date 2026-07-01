using System.Text.Json.Serialization;
using CrestCreates.Agent.Authoring.Http.OpenAICompatible;

namespace CrestCreates.Agent.Authoring.Http.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(OpenAICompatibleChatRequest))]
[JsonSerializable(typeof(OpenAICompatibleChatResponse))]
[JsonSerializable(typeof(OpenAICompatibleChatMessage))]
[JsonSerializable(typeof(OpenAICompatibleResponseFormat))]
[JsonSerializable(typeof(OpenAICompatibleChatChoice))]
internal sealed partial class OpenAICompatibleAuthoringJsonSerializerContext : JsonSerializerContext;
