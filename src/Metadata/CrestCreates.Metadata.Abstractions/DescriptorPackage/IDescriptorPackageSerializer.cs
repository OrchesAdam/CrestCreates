namespace CrestCreates.Metadata.Abstractions.DescriptorPackage;

public interface IDescriptorPackageSerializer
{
    string Serialize(DescriptorPackage package);
    DescriptorPackage Deserialize(string content);
}
