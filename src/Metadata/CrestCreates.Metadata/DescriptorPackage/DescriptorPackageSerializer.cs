using System.Text.Json;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using Package = CrestCreates.Metadata.Abstractions.DescriptorPackage.DescriptorPackage;

namespace CrestCreates.Metadata.DescriptorPackage;

public sealed class DescriptorPackageSerializer : IDescriptorPackageSerializer
{
    public string Serialize(Package package)
    {
        return JsonSerializer.Serialize(package,
            CrestCreatesMetadataJsonContext.Default.DescriptorPackage);
    }

    public Package Deserialize(string content)
    {
        return JsonSerializer.Deserialize(content,
            CrestCreatesMetadataJsonContext.Default.DescriptorPackage)
               ?? throw new InvalidOperationException("Failed to deserialize DescriptorPackage.");
    }
}
