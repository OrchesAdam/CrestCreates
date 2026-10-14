using System;

namespace CrestCreates.EventBus.Abstractions;

/// <summary>
/// Declares a historical replay key of an event type: records whose <c>LocalReplayKey</c> equals the
/// old key (with the same version) resolve to the current declaration. Aliases are renames of the
/// same JSON contract — the alias version must equal the target replay version — and never trigger
/// payload migration.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = true)]
public sealed class LocalEventReplayAliasAttribute : Attribute
{
    public LocalEventReplayAliasAttribute(string oldReplayKey, int version = 1)
    {
        OldReplayKey = oldReplayKey;
        Version = version;
    }

    /// <summary>The historical replay key exactly as persisted; never trimmed, lower-cased or re-split.</summary>
    public string OldReplayKey { get; }

    public int Version { get; }
}

public sealed record LocalEventReplayAlias(string OldReplayKey, int Version);
