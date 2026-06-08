using System.Collections.Concurrent;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class WorkflowRegistry : IWorkflowRegistry
{
    private readonly ConcurrentDictionary<string, WorkflowDescriptor> _byId = new();
    private readonly ConcurrentDictionary<string, List<WorkflowDescriptor>> _byName = new();

    public void Register(WorkflowDescriptor descriptor)
    {
        _byId[descriptor.Id] = descriptor;
        _byName.GetOrAdd(descriptor.Name, _ => new()).Add(descriptor);
    }

    public WorkflowDescriptor? GetById(string id) =>
        _byId.TryGetValue(id, out var d) ? d : null;

    public WorkflowDescriptor? GetByName(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.FirstOrDefault(v => v.State == DescriptorState.Active)
            : null;

    public WorkflowDescriptor? GetByNameAndVersion(string name, int version) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.FirstOrDefault(v => v.Version == version)
            : null;

    public WorkflowDescriptor? GetActiveVersion(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.Where(v => v.State == DescriptorState.Active).MaxBy(v => v.Version)
            : null;

    public WorkflowDescriptor? GetLatestVersion(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.MaxBy(v => v.Version)
            : null;

    public IReadOnlyList<WorkflowDescriptor> GetAllByName(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.AsReadOnly()
            : Array.Empty<WorkflowDescriptor>();

    public IReadOnlyList<WorkflowDescriptor> GetDeprecatedVersions(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.Where(v => v.State == DescriptorState.Deprecated).ToList().AsReadOnly()
            : Array.Empty<WorkflowDescriptor>();

    public IReadOnlyList<WorkflowDescriptor> GetAll() =>
        _byId.Values.ToList().AsReadOnly();
}
