using System;

namespace CrestCreates.EventBus.Abstractions;

/// <summary>
/// Local replay identity helpers. The local replay key is a compile-time deterministic identity
/// (assembly simple name + metadata type full name); it is a codec/lookup key only and never
/// impersonates a governance event descriptor id.
/// </summary>
public static class LocalEventIdentity
{
    public static string BuildLocalReplayKey(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        var assemblyName = eventType.Assembly.GetName().Name ?? string.Empty;
        var fullName = eventType.FullName ?? eventType.Name;
        return $"{assemblyName}:{fullName}";
    }

    /// <summary>Strips the assembly part of an assembly-qualified name; tolerates plain full names.</summary>
    public static string NormalizePayloadTypeName(string payloadTypeFullName)
    {
        ArgumentNullException.ThrowIfNull(payloadTypeFullName);

        var commaIndex = payloadTypeFullName.IndexOf(',');
        return commaIndex < 0
            ? payloadTypeFullName.Trim()
            : payloadTypeFullName[..commaIndex].Trim();
    }
}
