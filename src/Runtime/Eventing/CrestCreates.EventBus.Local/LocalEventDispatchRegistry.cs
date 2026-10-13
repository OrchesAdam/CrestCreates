using System;
using System.Collections.Generic;
using System.Linq;
using CrestCreates.EventBus.Abstractions;

namespace CrestCreates.EventBus.Local;

/// <summary>
/// Frozen implementation of the dispatch index. Built from explicitly activated contributors plus
/// optional manual declarations; validates conflicts, replay capability and identity ambiguity
/// deterministically at freeze time, then holds static metadata and delegates only.
/// </summary>
internal sealed class LocalEventDispatchRegistry : ILocalEventDispatchRegistry
{
    private readonly Dictionary<Type, LocalEventBinding> _byType;
    private readonly Dictionary<(string Key, int Version), LocalEventBinding> _byReplayKey;
    private readonly Dictionary<(string Name, int Version), LocalEventBinding> _byPayloadIdentity;
    private readonly Dictionary<(string Name, int Version), LocalEventBinding> _byLegacyAlias;

    private LocalEventDispatchRegistry(
        Dictionary<Type, LocalEventBinding> byType,
        Dictionary<(string, int), LocalEventBinding> byReplayKey,
        Dictionary<(string, int), LocalEventBinding> byPayloadIdentity,
        Dictionary<(string, int), LocalEventBinding> byLegacyAlias)
    {
        _byType = byType;
        _byReplayKey = byReplayKey;
        _byPayloadIdentity = byPayloadIdentity;
        _byLegacyAlias = byLegacyAlias;
        Bindings = byType.Values.ToArray();
    }

    public IReadOnlyCollection<LocalEventBinding> Bindings { get; }

    public static LocalEventDispatchRegistry Freeze(
        IEnumerable<ILocalEventRegistrationContributor> contributors,
        IReadOnlyList<Action<LocalEventDispatchRegistrationBuilder>>? configureCallbacks)
    {
        var builder = new LocalEventDispatchRegistrationBuilder();
        var seenContributorIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var contributor in contributors)
        {
            if (!seenContributorIds.Add(contributor.ContributorId))
            {
                continue;
            }

            contributor.Register(builder);
        }

        if (configureCallbacks is not null)
        {
            foreach (var configure in configureCallbacks)
            {
                configure(builder);
            }
        }

        var violations = new List<string>();
        var merged = new Dictionary<Type, LocalEventBinding>();

        foreach (var binding in builder.Bindings)
        {
            if (merged.TryGetValue(binding.EventType, out var existing))
            {
                merged[binding.EventType] = Merge(existing, binding, violations);
            }
            else
            {
                merged[binding.EventType] = binding;
            }
        }

        var byReplayKey = new Dictionary<(string, int), LocalEventBinding>();
        var byPayloadIdentity = new Dictionary<(string, int), LocalEventBinding>();
        var byLegacyAlias = new Dictionary<(string, int), LocalEventBinding>();

        foreach (var binding in merged.Values)
        {
            ValidateBinding(binding, violations);

            AddUnique(byReplayKey, (binding.LocalReplayKey, binding.ReplayVersion), binding,
                $"replay key '{binding.LocalReplayKey}:v{binding.ReplayVersion}'", violations);
            AddUnique(byPayloadIdentity, (binding.LocalName, binding.ReplayVersion), binding,
                $"payload identity '{binding.LocalName}:v{binding.ReplayVersion}'", violations);

            foreach (var legacy in binding.LegacyIdentities)
            {
                AddUnique(byLegacyAlias, (legacy.LegacyTypeName, legacy.Version), binding,
                    $"legacy alias '{legacy.LegacyTypeName}:v{legacy.Version}'", violations);
            }

            foreach (var other in merged.Values)
            {
                if (ReferenceEquals(other, binding) || other.EventType == binding.EventType)
                {
                    continue;
                }

                foreach (var legacy in other.LegacyIdentities)
                {
                    if (string.Equals(legacy.LegacyTypeName, binding.LocalName, StringComparison.Ordinal)
                        && legacy.Version == binding.ReplayVersion)
                    {
                        violations.Add(
                            $"legacy alias '{legacy.LegacyTypeName}:v{legacy.Version}' of '{other.EventType.FullName}' " +
                            $"collides with the current identity of '{binding.EventType.FullName}'.");
                    }
                }
            }
        }

        if (violations.Count > 0)
        {
            throw LocalEventDispatchConfigurationException.ForViolations(violations);
        }

        return new LocalEventDispatchRegistry(merged, byReplayKey, byPayloadIdentity, byLegacyAlias);
    }

    private static LocalEventBinding Merge(
        LocalEventBinding existing,
        LocalEventBinding next,
        List<string> violations)
    {
        if (existing.Replayable != next.Replayable)
        {
            violations.Add(
                $"Event '{existing.EventType.FullName}' is declared both as replayable ({Describe(existing)}) " +
                $"and as dispatch-only ({Describe(next)}).");
        }

        if (existing.ReplayVersion != next.ReplayVersion)
        {
            violations.Add(
                $"Event '{existing.EventType.FullName}' declares conflicting replay versions " +
                $"v{existing.ReplayVersion} ({Describe(existing)}) and v{next.ReplayVersion} ({Describe(next)}).");
        }

        var invoker = existing.Invoker.IsInert ? next.Invoker : existing.Invoker;
        var codecFactory = existing.CodecFactory ?? next.CodecFactory;
        var expected = existing.ExpectedHandlerTypes
            .Concat(next.ExpectedHandlerTypes)
            .Distinct()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
        var legacy = existing.LegacyIdentities
            .Concat(next.LegacyIdentities)
            .Distinct()
            .OrderBy(identity => identity.LegacyTypeName, StringComparer.Ordinal)
            .ThenBy(identity => identity.Version)
            .ToArray();
        var sources = string.Join("; ", new[] { existing.SourceAssembly, next.SourceAssembly }
            .Where(source => !string.IsNullOrEmpty(source))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(source => source, StringComparer.Ordinal));

        return LocalEventBinding.Create(
            invoker,
            existing.EventType,
            existing.ReplayVersion,
            existing.Replayable,
            codecFactory,
            expected,
            legacy,
            sources);
    }

    private static void ValidateBinding(LocalEventBinding binding, List<string> violations)
    {
        if (binding.ReplayVersion < 1)
        {
            violations.Add(
                $"Event '{binding.EventType.FullName}' declares invalid replay version v{binding.ReplayVersion} " +
                "(must be >= 1).");
        }

        if (!binding.Replayable)
        {
            if (binding.CodecFactory is not null)
            {
                violations.Add(
                    $"Dispatch-only event '{binding.EventType.FullName}' must not carry a replay codec.");
            }

            return;
        }

        if (binding.CodecFactory is null)
        {
            violations.Add(
                $"Replayable event '{binding.EventType.FullName}' has no JSON codec provider. " +
                "Import the CrestCreates.JsonContracts.Build toolchain in the assembly that owns the event type " +
                "so its generated AddLocalEvents() extension can register the source-generated codec.");
            return;
        }

        var typeInfo = binding.CodecFactory();
        if (typeInfo is null)
        {
            violations.Add(
                $"Replayable event '{binding.EventType.FullName}' resolved no JSON type info at freeze. " +
                "The owning assembly's generated codec contribution was not registered; enable that assembly's " +
                "AddLocalEvents() extension.");
            return;
        }

        if (typeInfo.Type != binding.EventType)
        {
            violations.Add(
                $"Replayable event '{binding.EventType.FullName}' resolved a codec for " +
                $"'{typeInfo.Type.FullName}'; codec type identity mismatch.");
        }
    }

    private static void AddUnique(
        Dictionary<(string, int), LocalEventBinding> index,
        (string, int) key,
        LocalEventBinding binding,
        string description,
        List<string> violations)
    {
        if (index.TryGetValue(key, out var existing) && existing.EventType != binding.EventType)
        {
            violations.Add(
                $"{description} is contributed by both '{existing.EventType.FullName}' " +
                $"({Describe(existing)}) and '{binding.EventType.FullName}' ({Describe(binding)}).");
            return;
        }

        index[key] = binding;
    }

    private static string Describe(LocalEventBinding binding)
        => string.IsNullOrEmpty(binding.SourceAssembly) ? "unknown assembly" : binding.SourceAssembly;

    public bool TryGet(Type eventType, out LocalEventBinding binding)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        return _byType.TryGetValue(eventType, out binding!);
    }

    public bool TryGetByReplayKey(string replayKey, int version, out LocalEventBinding binding)
    {
        ArgumentNullException.ThrowIfNull(replayKey);
        return _byReplayKey.TryGetValue((replayKey, version), out binding!);
    }

    public bool TryResolvePayloadIdentity(string payloadTypeFullName, int eventVersion, out LocalEventBinding binding)
    {
        ArgumentNullException.ThrowIfNull(payloadTypeFullName);

        var normalized = LocalEventIdentity.NormalizePayloadTypeName(payloadTypeFullName);
        return _byPayloadIdentity.TryGetValue((normalized, eventVersion), out binding!)
            || _byLegacyAlias.TryGetValue((normalized, eventVersion), out binding!);
    }
}
