namespace CrestCreates.Metadata.Abstractions;

public interface IDescriptorCatalog
{
    IDescriptor? Get(string id);
    IEnumerable<IDescriptor> GetAll();
    IEnumerable<IDescriptor> FindByKind(DescriptorKind kind);
    IEnumerable<IDescriptor> FindByPackage(string packageId);
    IEnumerable<IDescriptor> FindDependents(string descriptorId);
    IEnumerable<IDescriptor> FindDependencies(string descriptorId);
    ImpactReport AnalyzeImpact(string descriptorId, int fromVersion, int toVersion);
}
