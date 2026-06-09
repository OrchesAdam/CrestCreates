using System.Collections.Frozen;
using System.Collections.Immutable;
using CrestCreates.Event.Abstractions;

namespace CrestCreates.Event;

public sealed class EventRegistry : IEventRegistry, IEventMetadataProvider
{
    private EventRegistrySnapshot? _snapshot;
    private readonly object _buildLock = new();
    public RegistryState State { get; private set; } = RegistryState.Created;

    public void Build(IEnumerable<IEventDescriptorProvider> providers)
    {
        if (State == RegistryState.Built) return;
        lock (_buildLock)
        {
            if (State == RegistryState.Built) return;
            if (State == RegistryState.Failed)
                throw new InvalidOperationException(
                    "EventRegistry.Build() previously failed. Restart required.");
            State = RegistryState.Building;
        }

        var descriptors = providers.SelectMany(p => p.GetDescriptors()).ToList();
        try
        {
            ValidateNoDuplicateNameVersions(descriptors);
            ValidateVersionChain(descriptors);
            ValidateUniquePayloadType(descriptors);
            _snapshot = BuildSnapshot(descriptors);
            State = RegistryState.Built;
        }
        catch
        {
            State = RegistryState.Failed;
            throw;
        }
    }

    public GeneratedEventDescriptor? GetByName(string name)
        => _snapshot?.ByName.TryGetValue(name, out var versions) == true
            ? versions.Where(v => v.State == Metadata.Abstractions.DescriptorState.Active)
                       .MaxBy(v => v.Version)
            : null;

    public GeneratedEventDescriptor? GetByPayloadType(Type t)
        => _snapshot?.ByPayloadType.TryGetValue(t, out var d) == true ? d : null;

    public GeneratedEventDescriptor? GetLatestVersion(string name)
        => _snapshot?.ByName.TryGetValue(name, out var versions) == true
            ? versions.MaxBy(v => v.Version)
            : null;

    public GeneratedEventDescriptor? GetByNameAndVersion(string name, int version)
        => _snapshot?.ByName.TryGetValue(name, out var versions) == true
            ? versions.FirstOrDefault(v => v.Version == version)
            : null;

    public IReadOnlyList<GeneratedEventDescriptor> GetAllVersions(string name)
    {
        if (_snapshot?.ByName.TryGetValue(name, out var versions) == true)
            return versions;
        return Array.Empty<GeneratedEventDescriptor>();
    }

    public IReadOnlyList<GeneratedEventDescriptor> GetAll()
    {
        if (_snapshot is null) return Array.Empty<GeneratedEventDescriptor>();
        return _snapshot.ByName.Values.SelectMany(v => v).ToList().AsReadOnly();
    }

    private static EventRegistrySnapshot BuildSnapshot(List<GeneratedEventDescriptor> descriptors)
    {
        var byName = descriptors
            .GroupBy(d => d.Name)
            .ToFrozenDictionary(g => g.Key, g => g.ToImmutableArray());
        var byPayload = descriptors
            .GroupBy(d => d.PayloadType)
            .ToFrozenDictionary(g => g.Key, g => g.OrderByDescending(d => d.Version).First());
        return new EventRegistrySnapshot(byName, byPayload);
    }

    // ── Build-time validations ──

    private static void ValidateNoDuplicateNameVersions(List<GeneratedEventDescriptor> descriptors)
    {
        var duplicates = descriptors
            .GroupBy(d => (d.Name, d.Version))
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.Name} v{g.Key.Version}")
            .ToList();

        if (duplicates.Count > 0)
            throw new EventRegistryBuildException(
                $"Duplicate (name, version) pairs detected: {string.Join(", ", duplicates)}. " +
                "Each (name, version) pair must be declared by exactly one module. " +
                "Use a new Version to evolve an existing event name.");
    }

    private static void ValidateVersionChain(List<GeneratedEventDescriptor> descriptors)
    {
        foreach (var group in descriptors.GroupBy(d => d.Name))
        {
            var active = group.Where(d => d.State == Metadata.Abstractions.DescriptorState.Active).ToList();

            if (active.Count == 0)
                throw new EventRegistryBuildException(
                    $"Event '{group.Key}' has no Active version. " +
                    "At least one version must be Active.");

            if (active.Count > 1)
                throw new EventRegistryBuildException(
                    $"Event '{group.Key}' has {active.Count} Active versions: " +
                    $"{string.Join(", ", active.Select(a => $"v{a.Version}"))}. " +
                    "Exactly one version must be Active at any time.");

            var highest = group.MaxBy(d => d.Version)!;
            if (active[0].Version != highest.Version)
                throw new EventRegistryBuildException(
                    $"Event '{group.Key}': the highest version (v{highest.Version}) is {highest.State}, " +
                    $"but v{active[0].Version} is Active. The highest version must be Active.");
        }
    }

    private static void ValidateUniquePayloadType(List<GeneratedEventDescriptor> descriptors)
    {
        var violations = descriptors
            .GroupBy(d => d.PayloadType)
            .Where(g => g.Count(d => d.State == Metadata.Abstractions.DescriptorState.Active) > 1)
            .ToList();

        if (violations.Count > 0)
            throw new EventRegistryBuildException(
                "PayloadType uniqueness violation: one CLR type maps to multiple Active events. " +
                "A payload type may map to at most one Active event descriptor. " +
                "If you need multiple events with the same payload shape, use distinct CLR types.");
    }
}

public sealed class EventRegistryBuildException : Exception
{
    public EventRegistryBuildException(string message) : base(message) { }
}
