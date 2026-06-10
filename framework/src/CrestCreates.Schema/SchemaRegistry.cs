using System.Collections.Frozen;
using System.Collections.Immutable;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Schema;

public sealed class SchemaRegistry : RegistryBase<SchemaDescriptor>, ISchemaRegistry
{
    protected override string RegistryNamespace => "schema";

    public SchemaRegistry(IRegistryValidationEngine<SchemaDescriptor> validationEngine)
        : base(validationEngine) { }

    protected override RegistrySnapshot<SchemaDescriptor> BuildSnapshot(
        List<SchemaDescriptor> descriptors)
    {
        var byId = descriptors
            .GroupBy(d => d.Id)
            .ToFrozenDictionary(g => g.Key, g => g.OrderByDescending(d => d.Version).First());

        var byName = descriptors
            .GroupBy(d => d.Name)
            .ToFrozenDictionary(g => g.Key, g => g.ToImmutableArray());

        var byVersion = descriptors
            .ToFrozenDictionary(d => new DescriptorKey(d.Namespace, d.Id, d.Version), d => d);

        return new RegistrySnapshot<SchemaDescriptor>(
            byId, byName, byVersion,
            descriptors.ToImmutableArray(),
            ImmutableDictionary<Type, IRegistryIndex>.Empty);
    }

    public new SchemaDescriptor? GetByName(string name)
    {
        var versions = base.GetByName(name);
        return versions.FirstOrDefault(v => v.State == DescriptorState.Active);
    }

    public SchemaDescriptor? GetByNameAndVersion(string name, int version)
    {
        var versions = base.GetByName(name);
        return versions.FirstOrDefault(v => v.Version == version);
    }

    public IReadOnlyList<SchemaDescriptor> GetAllByName(string name)
        => base.GetByName(name);

    public SchemaDescriptor? GetActiveVersion(string name)
    {
        var versions = base.GetByName(name);
        return versions.Where(v => v.State == DescriptorState.Active).MaxBy(v => v.Version);
    }

    public SchemaDescriptor? GetLatestVersion(string name)
    {
        var versions = base.GetByName(name);
        return versions.MaxBy(v => v.Version);
    }

    public IReadOnlyList<SchemaDescriptor> GetDeprecatedVersions(string name)
    {
        var versions = base.GetByName(name);
        return versions.Where(v => v.State == DescriptorState.Deprecated).ToList().AsReadOnly();
    }
}
