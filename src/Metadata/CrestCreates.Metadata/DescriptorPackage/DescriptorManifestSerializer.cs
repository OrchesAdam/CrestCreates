using System.Text.Json;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.DescriptorPackage;

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