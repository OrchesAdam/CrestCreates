using System;
using System.Collections.Generic;
using System.Reflection;

namespace CrestCreates.Web.Legacy;

/// <summary>
/// Explicit legacy activation contract: every participating assembly is declared, and every
/// service/action the legacy provider may publish must be listed here. There is no wildcard and no
/// whole-sample/whole-namespace exemption; the formal Ticket projection is therefore never allowed.
/// </summary>
public sealed class LegacyGeneratedApiOptions
{
    private readonly List<Type> _serviceMarkerTypes = new();
    private readonly Dictionary<string, HashSet<string>> _allowedActions = new(StringComparer.Ordinal);

    internal IReadOnlyList<Type> ServiceMarkerTypes => _serviceMarkerTypes;

    internal IReadOnlyDictionary<string, HashSet<string>> AllowedActions => _allowedActions;

    public LegacyGeneratedApiOptions AddApplicationServiceAssembly<TMarker>()
    {
        var markerType = typeof(TMarker);
        if (!_serviceMarkerTypes.Contains(markerType))
        {
            _serviceMarkerTypes.Add(markerType);
        }

        return this;
    }

    /// <summary>Allows exactly the named actions for the contract type; unlisted actions fail the map.</summary>
    public LegacyGeneratedApiOptions Allow<TService>(params string[] actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        if (actions.Length == 0)
        {
            throw new ArgumentException("A legacy allowlist entry must name at least one action.", nameof(actions));
        }

        var service = typeof(TService).FullName
            ?? throw new ArgumentException($"The legacy contract '{typeof(TService)}' has no full name.", nameof(TService));
        if (!_allowedActions.TryGetValue(service, out var allowed))
        {
            allowed = new HashSet<string>(StringComparer.Ordinal);
            _allowedActions.Add(service, allowed);
        }

        foreach (var action in actions)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                throw new ArgumentException("A legacy allowlist action name cannot be empty.", nameof(actions));
            }

            allowed.Add(action);
        }

        return this;
    }
}
