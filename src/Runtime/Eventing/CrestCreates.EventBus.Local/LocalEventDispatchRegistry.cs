using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.EventBus.Local;

/// <summary>
/// Frozen implementation of the dispatch index. Built from explicitly activated contributor
/// declarations plus optional manual declarations, with per-host codec contributions resolved
/// exactly once at freeze: every input is validated (no contributor-id skipping, no first-wins),
/// metadata must agree, metadata/options and collections are locked, explicit execution checks are
/// validated against DI, and then the registry holds static metadata, closed preparers and resolved
/// codecs only.
/// </summary>
internal sealed class LocalEventDispatchRegistry : ILocalEventDispatchRegistry
{
    private readonly Dictionary<Type, LocalEventBinding> _byType;
    private readonly Dictionary<(string Key, int Version), LocalEventBinding> _byReplayKey;
    private readonly Dictionary<(string Key, int Version), LocalEventBinding> _byReplayAlias;
    private readonly Dictionary<(string Name, int Version), LocalEventBinding> _byPayloadIdentity;
    private readonly Dictionary<(string Name, int Version), LocalEventBinding> _byLegacyAlias;

    private LocalEventDispatchRegistry(
        Dictionary<Type, LocalEventBinding> byType,
        Dictionary<(string, int), LocalEventBinding> byReplayKey,
        Dictionary<(string, int), LocalEventBinding> byReplayAlias,
        Dictionary<(string, int), LocalEventBinding> byPayloadIdentity,
        Dictionary<(string, int), LocalEventBinding> byLegacyAlias)
    {
        _byType = byType;
        _byReplayKey = byReplayKey;
        _byReplayAlias = byReplayAlias;
        _byPayloadIdentity = byPayloadIdentity;
        _byLegacyAlias = byLegacyAlias;
        Bindings = byType.Values.ToArray();
    }

    public IReadOnlyCollection<LocalEventBinding> Bindings { get; }

    /// <summary>True when at least one frozen binding demands governance; drives the readiness wiring.</summary>
    public bool RequiresGovernance { get; private init; }

    public static LocalEventDispatchRegistry Freeze(
        IEnumerable<ILocalEventRegistrationContributor> contributors,
        IReadOnlyList<Action<LocalEventDispatchRegistrationBuilder>>? configureCallbacks,
        IReadOnlyList<LocalEventJsonCodecRegistration>? codecContributions,
        IServiceProviderIsService? serviceProviderIsService = null,
        ILogger? migrationLogger = null)
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
        var byReplayAlias = new Dictionary<(string, int), LocalEventBinding>();
        var byPayloadIdentity = new Dictionary<(string, int), LocalEventBinding>();
        var byLegacyAlias = new Dictionary<(string, int), LocalEventBinding>();
        var requiresGovernance = false;

        foreach (var registration in merged.Values.OrderBy(r => r.EventType.FullName, StringComparer.Ordinal))
        {
            ValidatePolicy(registration, serviceProviderIsService, violations);
            if (registration.Policy.Governance == LocalEventGovernance.Required)
            {
                requiresGovernance = true;
            }

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
                registration.EffectiveReplayKey,
                registration.ReplayVersion,
                registration.Replayable,
                codec,
                registration.ExpectedHandlerTypes.ToImmutableArray(),
                registration.LegacyIdentities.ToImmutableArray(),
                registration.ReplayAliases.ToImmutableArray(),
                registration.ExecutionCheckTypes.ToImmutableArray(),
                registration.Policy,
                registration.SourceAssembly);
            frozen[registration.EventType] = binding;

            AddUnique(byReplayKey, (binding.LocalReplayKey, binding.ReplayVersion), binding,
                $"replay key '{binding.LocalReplayKey}:v{binding.ReplayVersion}'", violations);
            AddUnique(byPayloadIdentity, (binding.LocalName, binding.ReplayVersion), binding,
                $"payload identity '{binding.LocalName}:v{binding.ReplayVersion}'", violations);

            foreach (var alias in binding.ReplayAliases)
            {
                AddUnique(byReplayAlias, (alias.OldReplayKey, alias.Version), binding,
                    $"replay alias '{alias.OldReplayKey}:v{alias.Version}'", violations);
            }

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

        // Historical replay-key aliases and current keys share one collision space in both directions.
        foreach (var ((key, version), binding) in byReplayAlias)
        {
            if (byReplayKey.TryGetValue((key, version), out var current))
            {
                violations.Add(
                    $"replay alias '{key}:v{version}' of '{binding.EventType.FullName}' " +
                    $"collides with the current identity of '{current.EventType.FullName}'.");
            }
        }

        if (violations.Count > 0)
        {
            var ordered = violations.Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToList();
            throw LocalEventDispatchConfigurationException.ForViolations(ordered);
        }

        if (migrationLogger is not null)
        {
            LogValidatorMigrationDiagnostic(merged.Values, serviceProviderIsService, migrationLogger);
        }

        return new LocalEventDispatchRegistry(frozen, byReplayKey, byReplayAlias, byPayloadIdentity, byLegacyAlias)
        {
            RequiresGovernance = requiresGovernance,
        };
    }

    private static void ValidatePolicy(
        MergedRegistration registration,
        IServiceProviderIsService? serviceProviderIsService,
        List<string> violations)
    {
        var policy = registration.Policy;
        if (policy.ReplayKey is not null && !IsValidReplayKey(policy.ReplayKey))
        {
            violations.Add(
                $"Event '{registration.EventType.FullName}' declares invalid stable replay key " +
                $"'{policy.ReplayKey}' (must be non-empty without surrounding whitespace or control characters).");
        }

        foreach (var alias in registration.ReplayAliases)
        {
            if (!IsValidReplayKey(alias.OldReplayKey))
            {
                violations.Add(
                    $"Event '{registration.EventType.FullName}' declares invalid replay alias " +
                    $"'{alias.OldReplayKey}' (must be non-empty without surrounding whitespace or control characters).");
                continue;
            }

            if (alias.Version != registration.ReplayVersion)
            {
                violations.Add(
                    $"Replay alias '{alias.OldReplayKey}:v{alias.Version}' of '{registration.EventType.FullName}' " +
                    $"must use the declaration's replay version v{registration.ReplayVersion}; a rename alias never " +
                    "crosses versions and never implies payload migration.");
            }
        }

        foreach (var checkType in registration.ExecutionCheckTypes)
        {
            if (!typeof(ILocalEventExecutionCheck).IsAssignableFrom(checkType))
            {
                violations.Add(
                    $"Execution check '{checkType.FullName}' of '{registration.EventType.FullName}' does not " +
                    $"implement {nameof(ILocalEventExecutionCheck)}.");
                continue;
            }

            if (serviceProviderIsService is not null && !serviceProviderIsService.IsService(checkType))
            {
                violations.Add(
                    $"Execution check '{checkType.FullName}' of '{registration.EventType.FullName}' has no DI " +
                    "registration; a declared check must be registered or the assembly fails.");
            }
        }

        if (policy.UseEventValidator
            && serviceProviderIsService is not null
            && !serviceProviderIsService.IsService(typeof(CrestCreates.Event.Abstractions.IEventValidator)))
        {
            violations.Add(
                $"Event '{registration.EventType.FullName}' activates validator checks but no IEventValidator " +
                "registration exists in this host.");
        }
    }

    private static void LogValidatorMigrationDiagnostic(
        IEnumerable<MergedRegistration> registrations,
        IServiceProviderIsService? serviceProviderIsService,
        ILogger logger)
    {
        if (serviceProviderIsService is null
            || !serviceProviderIsService.IsService(typeof(CrestCreates.Event.Abstractions.IEventValidator)))
        {
            return;
        }

        if (registrations.Any(r => r.Policy.UseEventValidator))
        {
            return;
        }

        logger.LogWarning(
            "This host registers IEventValidator but no local event activates it. The former implicit " +
            "publish-time validation is now an explicit per-event activation; add " +
            "UseEventValidator = true to the affected declarations so the existing checks are not silently lost.");
    }

    private static bool IsValidReplayKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (char.IsControl(character))
            {
                return false;
            }
        }

        return true;
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

                // Read-only options do not make the metadata itself immutable: a hand-built typed
                // JsonTypeInfo can still be reshaped (property renames, added members) after freeze,
                // silently rewriting the persisted JSON contract. Take ownership by locking the
                // metadata now; a lock that does not stick is rejected deterministically.
                if (!info.IsReadOnly)
                {
                    try
                    {
                        info.MakeReadOnly();
                    }
                    catch (Exception metadataLockFailure)
                    {
                        groupProblem = $"a contribution (origin '{registration.Origin}') carries JSON type metadata " +
                                       $"that cannot be locked ({metadataLockFailure.GetType().Name}: " +
                                       $"{metadataLockFailure.Message}); a mutable execution contract must not be frozen";
                        break;
                    }

                    if (!info.IsReadOnly)
                    {
                        groupProblem = $"a contribution (origin '{registration.Origin}') still reports mutable JSON " +
                                       "type metadata after locking; a mutable execution contract must not be frozen";
                        break;
                    }
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
                    registration.Policy.ReplayAliases.ToArray(),
                    registration.Policy.ExecutionCheckTypes.ToArray(),
                    registration.Policy,
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

        if (existing.Policy.Governance != next.Policy.Governance)
        {
            violations.Add(
                $"Event '{existing.EventType.FullName}' declares conflicting governance policies " +
                $"({existing.Policy.Governance} vs {next.Policy.Governance}).");
        }

        if (existing.Policy.TenantMode != next.Policy.TenantMode)
        {
            violations.Add(
                $"Event '{existing.EventType.FullName}' declares conflicting tenant modes " +
                $"({existing.Policy.TenantMode} vs {next.Policy.TenantMode}).");
        }

        if (!string.Equals(existing.Policy.ReplayKey, next.Policy.ReplayKey, StringComparison.Ordinal))
        {
            violations.Add(
                $"Event '{existing.EventType.FullName}' declares conflicting stable replay keys " +
                $"('{existing.Policy.ReplayKey}' vs '{next.Policy.ReplayKey}').");
        }

        // Both a generated and a manual typed registration use the unified generic preparer; the
        // retained instance therefore never changes execution semantics.
        var invoker = existing.Invoker.IsInert ? next.Invoker : existing.Invoker;

        // Activation switches are additive: any contributor activating a check/history policy adds
        // it; only identity metadata (key/governance/tenant mode) must agree exactly.
        var mergedPolicy = existing.Policy with
        {
            UseEventValidator = existing.Policy.UseEventValidator || next.Policy.UseEventValidator,
            AllowHistoricalHostContext = existing.Policy.AllowHistoricalHostContext || next.Policy.AllowHistoricalHostContext,
            AdoptGovernedHistory = existing.Policy.AdoptGovernedHistory || next.Policy.AdoptGovernedHistory,
            ReplayAliases = existing.Policy.ReplayAliases
                .Concat(next.Policy.ReplayAliases)
                .Distinct()
                .OrderBy(alias => alias.OldReplayKey, StringComparer.Ordinal)
                .ThenBy(alias => alias.Version)
                .ToArray(),
            ExecutionCheckTypes = existing.Policy.ExecutionCheckTypes
                .Concat(next.Policy.ExecutionCheckTypes)
                .Distinct()
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray(),
        };

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
            ReplayAliases = mergedPolicy.ReplayAliases.ToArray(),
            ExecutionCheckTypes = mergedPolicy.ExecutionCheckTypes.ToArray(),
            Policy = mergedPolicy,
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
        LocalEventReplayAlias[] ReplayAliases,
        Type[] ExecutionCheckTypes,
        LocalEventPolicyOptions Policy,
        string[] SourceAssemblies)
    {
        public string SourceAssembly => Describe(SourceAssemblies);

        public string EffectiveReplayKey => Policy.ReplayKey ?? LocalEventIdentity.BuildLocalReplayKey(EventType);
    }

    public bool TryGet(Type eventType, out LocalEventBinding binding)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        return _byType.TryGetValue(eventType, out binding!);
    }

    public bool TryGetByReplayKey(string replayKey, int version, out LocalEventBinding binding)
    {
        ArgumentNullException.ThrowIfNull(replayKey);
        return _byReplayKey.TryGetValue((replayKey, version), out binding!)
            || _byReplayAlias.TryGetValue((replayKey, version), out binding!);
    }

    public bool TryResolvePayloadIdentity(string payloadTypeFullName, int eventVersion, out LocalEventBinding binding)
    {
        ArgumentNullException.ThrowIfNull(payloadTypeFullName);

        var normalized = LocalEventIdentity.NormalizePayloadTypeName(payloadTypeFullName);
        return _byPayloadIdentity.TryGetValue((normalized, eventVersion), out binding!)
            || _byLegacyAlias.TryGetValue((normalized, eventVersion), out binding!);
    }
}
