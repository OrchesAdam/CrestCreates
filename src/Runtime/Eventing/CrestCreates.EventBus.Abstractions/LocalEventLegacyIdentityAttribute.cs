using System;

namespace CrestCreates.EventBus.Abstractions;

/// <summary>
/// Declares a legacy payload identity of an event type so old dead-letter records can be
/// statically aliased to the current registration. Aliases never trigger <c>Type.GetType</c>;
/// unmapped legacy records are reported and preserved.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = true)]
public sealed class LocalEventLegacyIdentityAttribute : Attribute
{
    public LocalEventLegacyIdentityAttribute(string legacyTypeName, int version = 1)
    {
        LegacyTypeName = legacyTypeName;
        Version = version;
    }

    /// <summary>Metadata type full name (namespace-qualified, no assembly part) used by old records.</summary>
    public string LegacyTypeName { get; }

    public int Version { get; }
}

public sealed record LocalEventLegacyIdentity(string LegacyTypeName, int Version);
