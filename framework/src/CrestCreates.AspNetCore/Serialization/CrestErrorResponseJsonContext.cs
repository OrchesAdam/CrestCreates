using System.Text.Json.Serialization;
using CrestCreates.AspNetCore.Errors;

namespace CrestCreates.AspNetCore.Serialization;

[JsonSerializable(typeof(CrestErrorResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class CrestErrorResponseJsonContext : JsonSerializerContext
{
}
