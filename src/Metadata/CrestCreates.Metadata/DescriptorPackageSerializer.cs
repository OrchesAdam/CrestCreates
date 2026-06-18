using System.Text.Json;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public sealed class DescriptorPackageSerializer : IDescriptorPackageSerializer
{
    public string Serialize(DescriptorPackage package)
    {
        return JsonSerializer.Serialize(package,
            CrestCreatesMetadataJsonContext.Default.DescriptorPackage);
    }

    public DescriptorPackage Deserialize(string content)
    {
        return JsonSerializer.Deserialize(content,
            CrestCreatesMetadataJsonContext.Default.DescriptorPackage)
               ?? throw new InvalidOperationException("Failed to deserialize DescriptorPackage.");
    }
}
