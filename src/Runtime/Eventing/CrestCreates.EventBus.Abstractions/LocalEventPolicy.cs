using System;
using System.Collections.Generic;

namespace CrestCreates.EventBus.Abstractions;

/// <summary>
/// Assembly-time policy inputs of one declaration: stable key, replay aliases, governance/tenant
/// policy, explicit execution checks and the two narrowly-scoped historical adoption switches.
/// All switches default to the safe state and must be declared explicitly.
/// </summary>
public sealed record LocalEventPolicyOptions
{
    public static LocalEventPolicyOptions Default { get; } = new();

    /// <summary>Stable replay key; null keeps the deterministic default identity.</summary>
    public string? ReplayKey { get; init; }

    /// <summary>Historical replay keys (same JSON contract, same version) of this declaration.</summary>
    public IReadOnlyList<LocalEventReplayAlias> ReplayAliases { get; init; } = Array.Empty<LocalEventReplayAlias>();

    public LocalEventGovernance Governance { get; init; } = LocalEventGovernance.None;

    public LocalEventTenantMode TenantMode { get; init; } = LocalEventTenantMode.Optional;

    /// <summary>Scoped <see cref="ILocalEventExecutionCheck"/> implementations activated for this event.</summary>
    public IReadOnlyList<Type> ExecutionCheckTypes { get; init; } = Array.Empty<Type>();

    /// <summary>Routes the host's explicitly activated <c>IEventValidator</c> into the shared check core.</summary>
    public bool UseEventValidator { get; init; }

    /// <summary>
    /// Explicit per-event recognition that legacy rows without a context snapshot may execute as
    /// host context. Never applies to TenantMode.Required and never batch-marks history.
    /// </summary>
    public bool AllowHistoricalHostContext { get; init; }

    /// <summary>
    /// Explicit adoption mapping for governed rows with a null descriptor id: the row must carry a
    /// historical identity of this declaration (its resolved key/payload identity + version), and
    /// adoption binds the current same-version descriptor. Never upgrades unknown records.
    /// </summary>
    public bool AdoptGovernedHistory { get; init; }
}

/// <summary>
/// Governance policy of a local event declaration. Non-governed notifications keep the short path;
/// <see cref="Required"/> demands an exact, active event-registry descriptor association on every
/// execution entry and can never be satisfied by an optional validator being present or absent.
/// </summary>
public enum LocalEventGovernance
{
    None = 0,

    Required = 1,
}

/// <summary>
/// Tenant policy of a local event declaration. <see cref="Optional"/> still validates and restores
/// any captured non-empty TenantId; it never ignores an existing tenant.
/// </summary>
public enum LocalEventTenantMode
{
    Optional = 0,

    Required = 1,

    HostOnly = 2,
}

/// <summary>Execution entry that observes or enforces the shared policy checks.</summary>
public enum LocalEventExecutionSource
{
    Publish = 0,

    ChannelConsume = 1,

    Replay = 2,

    DirectDispatch = 3,
}
