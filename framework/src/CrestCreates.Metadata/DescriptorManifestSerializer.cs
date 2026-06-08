using System.Text.Json;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public static class DescriptorManifestSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Serialize(DescriptorManifest manifest)
    {
        return JsonSerializer.Serialize(manifest, Options);
    }

    public static DescriptorManifest? Deserialize(string json)
    {
        return JsonSerializer.Deserialize<DescriptorManifest>(json, Options);
    }
}