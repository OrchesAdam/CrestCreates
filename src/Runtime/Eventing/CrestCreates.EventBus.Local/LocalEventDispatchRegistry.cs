using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.EventBus.Abstractions;

namespace CrestCreates.EventBus.Local;

/// <summary>
/// Frozen implementation of the dispatch index. Built from explicitly activated contributor
/// declarations plus optional manual declarations, with per-host codec contributions resolved
/// exactly once at freeze: every input is validated (no contributor-id skipping, no first-wins),
/// metadata must agree, collections and codec options are locked, and then the registry holds
/// static metadata, closed preparers and resolved codecs only.
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
        IReadOnlyList<Action<LocalEventDispatchRegistrationBuilder>>? configureCallbacks,
        IReadOnlyList<LocalEventJsonCodecRegistration>? codecContributions)
    {
        var builder = new LocalEventDispatchRegistrationBuilder();

        // Every contributor's inputs participate in validation; repeated activation of the same
        // generated contribution is idempotent through metadata agreement below, not through
        // skipping same-id contributors (an inconsistent duplicate must surface).
        foreach (var contributor in contributors)
        {
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
        var codecIndex = BuildCodecIndex(builder.Registrations, codecContributions, violations);

        var merged = MergeRegistrations(builder.Registrations, violations);
        var frozen = new Dictionary<Type, LocalEventBinding>();
        var byReplayKey = new Dictionary<(string, int), LocalEventBinding>();
        var byPayloadIdentity = new Dictionary<(string, int), LocalEventBinding>();
        var byLegacyAlias = new Dictionary<(string, int), LocalEventBinding>();

        foreach (var registration in merged.Values.OrderBy(r => r.EventType.FullName, StringComparer.Ordinal))
        {
            JsonTypeInfo? codec = null;
            if (registration.Replayable)
            {
                if (!codecIndex.TryGetValue(registration.EventType, out codec))
                {
                    violations.Add(
                        $"Replayable event '{registration.EventType.FullName}' has no JSON codec contribution. " +
                        "Import the CrestCreates.JsonContracts.Build toolchain in the assembly that owns the event " +
                        "type so its generated AddLocalEvents() extension can register the source-generated codec " +
                        "(or pass an explicit typed codec to the declaration).");
                    continue;
                }
            }

            var binding = new LocalEventBinding(
                registration.Invoker,
                registration.EventType,
                registration.ReplayVersion,
                registration.Replayable,
                codec,
                registration.ExpectedHandlerTypes.ToImmutableArray(),
                registration.LegacyIdentities.ToImmutableArray(),
                registration.SourceAssembly);
            frozen[registration.EventType] = binding;

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
                if (other.EventType == registration.EventType)
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
            var ordered = violations.Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToList();
            throw LocalEventDispatchConfigurationException.ForViolations(ordered);
        }

        return new LocalEventDispatchRegistry(frozen, byReplayKey, byPayloadIdentity, byLegacyAlias);
    }

    private static Dictionary<Type, JsonTypeInfo> BuildCodecIndex(
        IReadOnlyList<LocalEventRegistration> registrations,
        IReadOnlyList<LocalEventJsonCodecRegistration>? codecContributions,
        List<string> violations)
    {
        var candidates = new List<LocalEventJsonCodecRegistration>();
        if (codecContributions is not null)
        {
            candidates.AddRange(codecContributions);
        }

        foreach (var registration in registrations)
        {
            if (registration.CodecFactory is not null)
            {
                candidates.Add(new LocalEventJsonCodecRegistration
                {
                    PayloadType = registration.EventType,
                    CodecFactory = registration.CodecFactory,
                    Origin = registration.SourceAssembly,
                });
            }
        }

        var index = new Dictionary<Type, JsonTypeInfo>();
        foreach (var group in candidates.GroupBy(c => c.PayloadType).OrderBy(g => g.Key.FullName, StringComparer.Ordinal))
        {
            var origins = group.Select(c => c.Origin).Distinct(StringComparer.Ordinal).OrderBy(o => o, StringComparer.Ordinal).ToList();
            if (origins.Count > 1)
            {
                violations.Add(
                    $"Local event codec for '{group.Key.FullName}' is contributed by multiple origins " +
                    $"({string.Join(", ", origins)}); one payload type has exactly one owning assembly.");
                continue;
            }

            JsonTypeInfo? resolvedInfo = null;
            string? groupProblem = null;
            foreach (var registration in group)
            {
                JsonTypeInfo? info;
                try
                {
                    info = registration.CodecFactory();
                }
                catch (Exception factoryFailure)
                {
                    groupProblem = $"the codec factory (origin '{registration.Origin}') threw " +
                                   $"{factoryFailure.GetType().Name}: {factoryFailure.Message}";
                    break;
                }

                if (info is null)
                {
                    groupProblem = $"a contribution (origin '{registration.Origin}') resolved no JSON type info";
                    break;
                }

                if (info.Type != group.Key)
                {
                    groupProblem = $"a contribution (origin '{registration.Origin}') describes '{info.Type.FullName}' instead";
                    break;
                }

                if (!info.Options.IsReadOnly)
                {
                    groupProblem = $"a contribution (origin '{registration.Origin}') carries mutable serializer options; " +
                                   "lock them before freeze so the execution contract cannot change";
                    break;
                }

                if (resolvedInfo is null)
                {
                    resolvedInfo = info;
                    continue;
                }

                if (!ReferenceEquals(resolvedInfo, info))
                {
                    groupProblem = $"the same origin ('{registration.Origin}') contributes different codec metadata " +
                                   "for this payload type; repeated activation must resolve to the same contract";
                    break;
                }
            }

            if (groupProblem is not null)
            {
                violations.Add($"Local event codec contribution for '{group.Key.FullName}' is invalid: {groupProblem}.");
                continue;
            }

            index[group.Key] = resolvedInfo!;
        }

        return index;
    }

    private static Dictionary<Type, MergedRegistration> MergeRegistrations(
        IReadOnlyList<LocalEventRegistration> registrations,
        List<string> violations)
    {
        var merged = new Dictionary<Type, MergedRegistration>();
        foreach (var registration in registrations.OrderBy(r => r.EventType.FullName, StringComparer.Ordinal))
        {
            if (merged.TryGetValue(registration.EventType, out var existing))
            {
                merged[registration.EventType] = Merge(existing, registration, violations);
            }
            else
            {
                merged[registration.EventType] = new MergedRegistration(
                    registration.EventType,
                    registration.Invoker,
                    registration.ReplayVersion,
                    registration.Replayable,
                    registration.ExpectedHandlerTypes.ToArray(),
                    registration.LegacyIdentities.ToArray(),
                    new[] { registration.SourceAssembly });
            }
        }

        foreach (var registration in merged.Values)
        {
            if (registration.ReplayVersion < 1)
            {
                violations.Add(
                    $"Event '{registration.EventType.FullName}' declares invalid replay version v{registration.ReplayVersion} " +
                    "(must be >= 1).");
            }
        }

        return merged;
    }

    private static MergedRegistration Merge(
        MergedRegistration existing,
        LocalEventRegistration next,
        List<string> violations)
    {
        if (existing.Replayable != next.Replayable)
        {
            violations.Add(
                $"Event '{existing.EventType.FullName}' is declared both as replayable ({Describe(existing.SourceAssemblies)}) " +
                $"and as dispatch-only ({Describe(next.SourceAssembly)}).");
        }

        if (existing.ReplayVersion != next.ReplayVersion)
        {
            violations.Add(
                $"Event '{existing.EventType.FullName}' declares conflicting replay versions " +
                $"v{existing.ReplayVersion} ({Describe(existing.SourceAssemblies)}) and v{next.ReplayVersion} " +
                $"({Describe(next.SourceAssembly)}).");
        }

        if (!existing.Replayable && next.Replayable && next.CodecFactory is not null)
        {
            violations.Add(
                $"Dispatch-only event '{existing.EventType.FullName}' must not carry a replay codec " +
                $"({Describe(next.SourceAssembly)}).");
        }

        // Both a generated and a manual typed registration use the unified generic preparer; the
        // retained instance therefore never changes execution semantics.
        var invoker = existing.Invoker.IsInert ? next.Invoker : existing.Invoker;

        return existing with
        {
            Invoker = invoker,
            ExpectedHandlerTypes = existing.ExpectedHandlerTypes
                .Concat(next.ExpectedHandlerTypes)
                .Distinct()
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray(),
            LegacyIdentities = existing.LegacyIdentities
                .Concat(next.LegacyIdentities)
                .Distinct()
                .OrderBy(identity => identity.LegacyTypeName, StringComparer.Ordinal)
                .ThenBy(identity => identity.Version)
                .ToArray(),
            SourceAssemblies = existing.SourceAssemblies
                .Concat(new[] { next.SourceAssembly })
                .Where(source => !string.IsNullOrEmpty(source))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(source => source, StringComparer.Ordinal)
                .ToArray(),
        };
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
                $"({Describe(existing.SourceAssembly)}) and '{binding.EventType.FullName}' ({Describe(binding.SourceAssembly)}).");
            return;
        }

        index[key] = binding;
    }

    private static string Describe(string sourceAssembly)
        => string.IsNullOrEmpty(sourceAssembly) ? "unknown assembly" : sourceAssembly;

    private static string Describe(string[] sourceAssemblies)
        => sourceAssemblies.Length == 0 ? "unknown assembly" : string.Join("; ", sourceAssemblies);

    private sealed record MergedRegistration(
        Type EventType,
        LocalEventInvoker Invoker,
        int ReplayVersion,
        bool Replayable,
        Type[] ExpectedHandlerTypes,
        LocalEventLegacyIdentity[] LegacyIdentities,
        string[] SourceAssemblies)
    {
        public string SourceAssembly => Describe(SourceAssemblies);
    }

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
