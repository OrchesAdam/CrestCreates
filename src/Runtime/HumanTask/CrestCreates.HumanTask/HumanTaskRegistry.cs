using System.Collections.Frozen;
using System.Collections.Immutable;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Registry;
using CrestCreates.HumanTask.Abstractions;

namespace CrestCreates.HumanTask;

public sealed class HumanTaskRegistry : RegistryBase<HumanTaskDescriptor>, IHumanTaskRegistry
{
    protected override string RegistryNamespace => "humantask";

    public HumanTaskRegistry(IRegistryValidationEngine<HumanTaskDescriptor> validationEngine)
        : base(validationEngine) { }

    protected override RegistrySnapshot<HumanTaskDescriptor> BuildSnapshot(
        List<HumanTaskDescriptor> descriptors)
    {
        var byId = descriptors
            .GroupBy(d => d.Id)
            .ToFrozenDictionary(g => g.Key, g => g.OrderByDescending(d => d.Version).First());

        var byName = descriptors
            .GroupBy(d => d.Name)
            .ToFrozenDictionary(g => g.Key, g => g.ToImmutableArray());

        var byVersion = descriptors
            .ToFrozenDictionary(d => new DescriptorKey(d.Namespace, d.Id, d.Version), d => d);

        return new RegistrySnapshot<HumanTaskDescriptor>(
            byId, byName, byVersion,
            descriptors.ToImmutableArray(),
            ImmutableDictionary<Type, IRegistryIndex>.Empty);
    }

    public new HumanTaskDescriptor? GetByName(string name)
    {
        var versions = base.GetByName(name);
        return versions.FirstOrDefault(v => v.State == DescriptorState.Active);
    }

    public HumanTaskDescriptor? GetByNameAndVersion(string name, int version)
    {
        var versions = base.GetByName(name);
        return versions.FirstOrDefault(v => v.Version == version);
    }

    public IReadOnlyList<HumanTaskDescriptor> GetAllByName(string name)
        => base.GetByName(name);

    public HumanTaskDescriptor? GetActiveVersion(string name)
    {
        var versions = base.GetByName(name);
        return versions.Where(v => v.State == DescriptorState.Active).MaxBy(v => v.Version);
    }

    public HumanTaskDescriptor? GetLatestVersion(string name)
    {
        var versions = base.GetByName(name);
        return versions.MaxBy(v => v.Version);
    }

    public IReadOnlyList<HumanTaskDescriptor> GetDeprecatedVersions(string name)
    {
        var versions = base.GetByName(name);
        return versions.Where(v => v.State == DescriptorState.Deprecated).ToList().AsReadOnly();
    }
}
