using System.Collections.Frozen;
using System.Collections.Immutable;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class WorkflowRegistry : RegistryBase<WorkflowDescriptor>, IWorkflowRegistry
{
    protected override string RegistryNamespace => "workflow";

    public WorkflowRegistry(IRegistryValidationEngine<WorkflowDescriptor> validationEngine)
        : base(validationEngine) { }

    protected override RegistrySnapshot<WorkflowDescriptor> BuildSnapshot(
        List<WorkflowDescriptor> descriptors)
    {
        var byId = descriptors
            .GroupBy(d => d.Id)
            .ToFrozenDictionary(g => g.Key, g => g.OrderByDescending(d => d.Version).First());

        var byName = descriptors
            .GroupBy(d => d.Name)
            .ToFrozenDictionary(g => g.Key, g => g.ToImmutableArray());

        var byVersion = descriptors
            .ToFrozenDictionary(d => new DescriptorKey(d.Namespace, d.Id, d.Version), d => d);

        return new RegistrySnapshot<WorkflowDescriptor>(
            byId, byName, byVersion,
            descriptors.ToImmutableArray(),
            ImmutableDictionary<Type, IRegistryIndex>.Empty);
    }

    public new WorkflowDescriptor? GetByName(string name)
    {
        var versions = base.GetByName(name);
        return versions.FirstOrDefault(v => v.State == DescriptorState.Active);
    }

    public WorkflowDescriptor? GetByNameAndVersion(string name, int version)
    {
        var versions = base.GetByName(name);
        return versions.FirstOrDefault(v => v.Version == version);
    }

    public IReadOnlyList<WorkflowDescriptor> GetAllByName(string name)
        => base.GetByName(name);

    public WorkflowDescriptor? GetActiveVersion(string name)
    {
        var versions = base.GetByName(name);
        return versions.Where(v => v.State == DescriptorState.Active).MaxBy(v => v.Version);
    }

    public WorkflowDescriptor? GetLatestVersion(string name)
    {
        var versions = base.GetByName(name);
        return versions.MaxBy(v => v.Version);
    }

    public IReadOnlyList<WorkflowDescriptor> GetDeprecatedVersions(string name)
    {
        var versions = base.GetByName(name);
        return versions.Where(v => v.State == DescriptorState.Deprecated).ToList().AsReadOnly();
    }
}
