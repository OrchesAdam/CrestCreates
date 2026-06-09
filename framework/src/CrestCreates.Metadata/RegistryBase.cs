using System.Collections.Immutable;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public abstract class RegistryBase<TDescriptor>
    where TDescriptor : class, IDescriptor
{
    protected RegistrySnapshot<TDescriptor>? _snapshot;
    protected readonly object _buildLock = new();
    public RegistryState State { get; protected set; } = RegistryState.Created;

    /// <summary>
    /// Registry domain namespace. Subclasses must provide this.
    /// </summary>
    protected abstract string RegistryNamespace { get; }

    private readonly IRegistryValidationEngine<TDescriptor> _validationEngine;
    private readonly IEnumerable<IRegistryIndexBuilder<TDescriptor, IRegistryIndex>>? _indexBuilders;

    protected RegistryBase(
        IRegistryValidationEngine<TDescriptor> validationEngine,
        IEnumerable<IRegistryIndexBuilder<TDescriptor, IRegistryIndex>>? indexBuilders = null)
    {
        _validationEngine = validationEngine;
        _indexBuilders = indexBuilders;
    }

    public void Build(IEnumerable<IDescriptorProvider<TDescriptor>> providers)
    {
        if (State == RegistryState.Built) return;

        lock (_buildLock)
        {
            if (State == RegistryState.Built) return;
            if (State == RegistryState.Failed)
                throw new InvalidOperationException("Registry.Build() previously failed. Restart required.");
            State = RegistryState.Building;
        }

        var descriptors = providers.SelectMany(p => p.GetDescriptors()).ToList();

        try
        {
            var report = _validationEngine.Validate(descriptors);
            if (report.HasErrors)
                throw new RegistryValidationException(report.Issues);
            _snapshot = BuildSnapshot(descriptors);
            State = RegistryState.Built;
        }
        catch (RegistryValidationException)
        {
            State = RegistryState.Failed;
            throw;
        }
        catch
        {
            State = RegistryState.Failed;
            throw;
        }
    }

    public TDescriptor? GetById(string id)
        => _snapshot?.ById.TryGetValue(id, out var d) == true ? d : null;

    public IReadOnlyList<TDescriptor> GetByName(string name)
        => _snapshot?.ByName.TryGetValue(name, out var versions) == true ? versions : Array.Empty<TDescriptor>();

    public IReadOnlyList<TDescriptor> GetAll()
        => _snapshot?.All ?? ImmutableArray<TDescriptor>.Empty;

    public TDescriptor? GetByVersion(string id, int version)
        => _snapshot?.ByVersion.TryGetValue(new DescriptorKey(RegistryNamespace, id, version), out var d) == true ? d : null;

    protected abstract RegistrySnapshot<TDescriptor> BuildSnapshot(List<TDescriptor> descriptors);
}

public sealed class RegistryValidationException : Exception
{
    public IReadOnlyList<ValidationIssue> Issues { get; }

    public RegistryValidationException(IReadOnlyList<ValidationIssue> issues)
        : base($"Registry validation failed with {issues.Count(i => i.Severity == ValidationSeverity.Error)} error(s):\n" +
               string.Join("\n", issues.Where(i => i.Severity == ValidationSeverity.Error).Select(i => $"  - {i.Message}")))
    {
        Issues = issues;
    }
}
