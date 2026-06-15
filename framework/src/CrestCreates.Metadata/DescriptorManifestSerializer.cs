using System.Text.Json;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public static class DescriptorManifestSerializer
{
    public static string Serialize(DescriptorManifest manifest)
    {
        return JsonSerializer.Serialize(manifest,
            CrestCreatesMetadataJsonContext.Default.DescriptorManifest);
    }

    public static DescriptorManifest? Deserialize(string json)
    {
        return JsonSerializer.Deserialize(json,
            CrestCreatesMetadataJsonContext.Default.DescriptorManifest);
    }
}