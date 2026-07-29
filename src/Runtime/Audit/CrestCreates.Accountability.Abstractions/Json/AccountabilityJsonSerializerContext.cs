using System.Text.Json.Serialization;
using CrestCreates.Core.Abstractions.Serialization;

namespace CrestCreates.Accountability.Abstractions.Json;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonContractSurface(
    typeof(Sinks.IAuditSink),
    ExcludedParameterTypes = new[] { typeof(CancellationToken) })]
public sealed partial class AccountabilityJsonSerializerContext : JsonSerializerContext
{
}
