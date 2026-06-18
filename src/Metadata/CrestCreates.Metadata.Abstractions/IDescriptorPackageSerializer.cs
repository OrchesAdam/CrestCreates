namespace CrestCreates.Metadata.Abstractions;

public interface IDescriptorPackageSerializer
{
    string Serialize(DescriptorPackage package);
    DescriptorPackage Deserialize(string content);
}
