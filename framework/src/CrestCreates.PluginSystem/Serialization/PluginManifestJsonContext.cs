using System.Text.Json.Serialization;
using CrestCreates.PluginSystem.Models;

namespace CrestCreates.PluginSystem.Serialization;

[JsonSerializable(typeof(PluginManifest))]
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    AllowTrailingCommas = true)]
public partial class PluginManifestJsonContext : JsonSerializerContext
{
}
