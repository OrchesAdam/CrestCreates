using System.Text.Json.Serialization;
using CrestCreates.Agent.Authoring.Parsing;

namespace CrestCreates.Agent.Authoring.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DescriptorAuthoringProviderOutputDto))]
[JsonSerializable(typeof(DescriptorAuthoringProviderPlanDto))]
[JsonSerializable(typeof(DescriptorAuthoringProviderItemDto))]
[JsonSerializable(typeof(DescriptorAuthoringProviderDescriptorRefDto))]
internal sealed partial class DescriptorAuthoringParserJsonSerializerContext : JsonSerializerContext;
